using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>Reports are open to any visitor; everything else requires an admin account.</summary>
[ApiController]
[Route("api")]
public class AdminController(
    StayHostDbContext db, AuthService auth, NotificationService notifications, AdminAudit audit,
    AdminGate gate, PresenceTracker presence)
    : ControllerBase
{
    /* ------------------------------------------------------------- reports */

    /// <summary>docs/01 AT-02 — the reasons offered for one kind of subject.</summary>
    [HttpGet("reports/reasons/{target}")]
    public ActionResult<ReportReasonsDto> ReportReasons(string target)
    {
        if (!Reports.TryParseTarget(target, out var parsed))
            return BadRequest(new { message = "Loại báo cáo không hợp lệ." });

        return Ok(new ReportReasonsDto(
            parsed.ToString(), Reports.TargetLabel(parsed), Reports.ReasonsFor(parsed)));
    }

    [HttpPost("reports")]
    public async Task<ActionResult<object>> Report([FromBody] CreateReportRequest req, CancellationToken ct)
    {
        if (!Reports.TryParseTarget(req.Target, out var target))
            return BadRequest(new { message = "Loại báo cáo không hợp lệ." });

        if (Reports.Validate(target, req.SubjectId, req.Reason, req.Detail) is { } invalid)
            return BadRequest(new { message = invalid });

        var user = await auth.CurrentUserAsync(ct);

        var report = new AbuseReport
        {
            Target = target,
            ReporterUserId = user?.Id,
            SessionId = HttpContext.SessionId(),
            Reason = req.Reason.Trim(),
            Detail = string.IsNullOrWhiteSpace(req.Detail) ? null : req.Detail.Trim()
        };

        // Who the subject belongs to. Needed to refuse a self-report, and for a
        // message to refuse anybody outside the conversation — otherwise guessing
        // message ids would turn this endpoint into a way to confirm that a private
        // thread exists.
        int? subjectOwnerId;

        switch (target)
        {
            case ReportTarget.Listing:
            {
                var listing = await db.Listings
                    .Include(l => l.Host)
                    .FirstOrDefaultAsync(l => l.Id == req.SubjectId, ct);
                if (listing is null) return NotFound(new { message = "Không tìm thấy tin đăng." });

                report.ListingId = listing.Id;
                subjectOwnerId = listing.Host?.UserId;
                break;
            }

            case ReportTarget.User:
            {
                var reported = await db.Users.FirstOrDefaultAsync(u => u.Id == req.SubjectId, ct);
                if (reported is null) return NotFound(new { message = "Không tìm thấy người dùng." });

                report.ReportedUserId = reported.Id;
                subjectOwnerId = reported.Id;
                break;
            }

            case ReportTarget.Message:
            {
                if (user is null)
                    return Unauthorized(new { message = "Bạn cần đăng nhập để báo cáo tin nhắn." });

                var message = await db.Messages
                    .Include(m => m.Thread)
                    .FirstOrDefaultAsync(m => m.Id == req.SubjectId, ct);

                // Same answer whether the message is missing or simply none of the
                // reporter's business: a different one would leak which ids exist.
                var inThread = message?.Thread is { } t
                               && (t.GuestUserId == user.Id || t.HostUserId == user.Id);
                if (message is null || !inThread)
                    return NotFound(new { message = "Không tìm thấy tin nhắn." });

                if (message.IsSystem)
                    return BadRequest(new { message = "Đây là tin nhắn tự động của hệ thống, không thể báo cáo." });

                report.MessageId = message.Id;
                subjectOwnerId = message.SenderUserId;
                break;
            }

            default:
            {
                var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == req.SubjectId, ct);
                if (review is null) return NotFound(new { message = "Không tìm thấy đánh giá." });

                report.ReviewId = review.Id;
                subjectOwnerId = review.AuthorUserId;
                break;
            }
        }

        if (Reports.IsSelfReport(user?.Id, subjectOwnerId))
            return BadRequest(new { message = "Bạn không thể báo cáo chính mình." });

        // One open report per person per subject. Without this a single upset
        // reporter can fill the moderation queue with the same complaint, and
        // BadgeService would read the pile as many separate upheld reports.
        if (user is not null)
        {
            var already = await db.AbuseReports.AnyAsync(
                r => r.ReporterUserId == user.Id
                     && r.Target == target
                     && r.Status != ReportStatus.Resolved && r.Status != ReportStatus.Dismissed
                     && (target == ReportTarget.Listing ? r.ListingId == req.SubjectId
                         : target == ReportTarget.User ? r.ReportedUserId == req.SubjectId
                         : target == ReportTarget.Message ? r.MessageId == req.SubjectId
                         : r.ReviewId == req.SubjectId), ct);

            if (already)
                return Ok(new { message = "Bạn đã báo cáo nội dung này. Đội an toàn đang xem xét." });
        }

        db.AbuseReports.Add(report);
        await db.SaveChangesAsync(ct);

        return Ok(new { message = "Cảm ơn bạn. Chúng tôi sẽ xem xét báo cáo này." });
    }

    /* --------------------------------------------------------------- admin */

    // docs/00 §3.4 — reading the console needs the Support scope; each action
    // below asks for the narrower scope it actually requires.
    private Task<User?> RequireAdminAsync(CancellationToken ct) =>
        audit.RequireAsync(AdminScope.Support, ct);

    /// <summary>
    /// How many people are on the site right now.
    ///
    /// Its own endpoint rather than a field on the overview, because the two
    /// have nothing in common but the page they appear on: this one is cheap
    /// enough to poll every half minute and answers from memory, while the
    /// overview runs a dozen aggregate queries and would be wasteful at that
    /// rate. Support scope, the same as the rest of the dashboard — it names no
    /// individual, so there is nothing here a support admin should not see.
    /// </summary>
    [HttpGet("admin/presence")]
    public async Task<ActionResult<PresenceDto>> Presence(CancellationToken ct)
    {
        if (await RequireAdminAsync(ct) is null)
            return StatusCode(403, new { message = "Chỉ quản trị viên mới xem được trang này." });

        var now = DateTime.UtcNow;
        var live = presence.Read(now);

        return Ok(new PresenceDto(
            live.Total, live.SignedIn, live.Guests,
            live.Peak, live.Since, live.WindowMinutes, live.At));
    }

    [HttpGet("admin/overview")]
    public async Task<ActionResult<AdminOverviewDto>> Overview(CancellationToken ct)
    {
        if (await RequireAdminAsync(ct) is null)
            return StatusCode(403, new { message = "Chỉ quản trị viên mới xem được trang này." });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var payments = await db.Payments
            .Where(p => p.Status != PaymentStatus.Refunded && p.Status != PaymentStatus.Failed)
            .Select(p => new { p.Amount, p.PlatformFee })
            .ToListAsync(ct);

        var recent = await db.Listings
            .Include(l => l.Host)
            .OrderByDescending(l => l.CreatedAt)
            .Take(12)
            .ToListAsync(ct);

        var reports = await LoadReportsAsync(ct);

        return Ok(new AdminOverviewDto(
            await db.Users.CountAsync(ct),
            await db.Hosts.CountAsync(ct),
            await db.Listings.CountAsync(ct),
            await db.Listings.CountAsync(l => l.IsPublished, ct),
            await db.Listings.CountAsync(l => !l.IsPublished, ct),
            await db.Bookings.CountAsync(ct),
            await db.Bookings.CountAsync(b => BookingLifecycle.BlocksDates.Contains(b.Status) && b.CheckOut >= today, ct),
            payments.Sum(p => p.Amount),
            payments.Sum(p => p.PlatformFee),
            await db.AbuseReports.CountAsync(r => r.Status == ReportStatus.Open, ct),
            // Undeliverable mail is finished, not pending — without the filter
            // the operator's "waiting to send" number would never drain.
            await db.EmailMessages.CountAsync(e => e.SentAt == null && !e.Undeliverable, ct),
            recent.Select(l => new AdminListingDto(
                l.Id, l.Slug, l.Title, l.City, l.Host?.Name ?? "",
                l.IsPublished, Math.Round(l.Rating, 2), l.ReviewCount, l.PricePerNight, l.CreatedAt)).ToList(),
            reports,
            await ReconcileAsync(ct),
            await audit.RecentAsync(30, ct),
            await LoadSettingsAsync(ct)));
    }

    /* --------------------------------------------------------------- QT-06 */

    /// <summary>
    /// docs/01 QT-06 — the fee rates and the regional tax rules, the two things
    /// an operator has to be able to change without a deploy.
    /// </summary>
    private async Task<PlatformSettingsDto> LoadSettingsAsync(CancellationToken ct)
    {
        var taxes = await db.TaxRules
            .OrderBy(r => r.Country).ThenBy(r => r.City).ThenBy(r => r.SortOrder)
            .Select(r => new TaxRuleDto(
                r.Id, r.Country, r.City, r.Name, r.Method.ToString(), r.Base.ToString(),
                r.Value, r.SortOrder, r.IsActive))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var rates = await db.ExchangeRates
            .OrderBy(r => r.SortOrder)
            .ToListAsync(ct);
        var rateDtos = rates.Select(r => new ExchangeRateDto(
            r.Id, r.Code, r.Label, r.Symbol, r.RateFromVnd, r.SortOrder, r.IsActive,
            r.Source.ToString(), r.UpdatedAt, Fx.Stale(r.UpdatedAt, now),
            r.FeedRate, r.FeedFetchedAt)).ToList();

        var settings = PricingSettings.Current;
        return new PlatformSettingsDto(
            settings.GuestServiceFeeRate, settings.HostServiceFeeRate,
            settings.MaxDiscountPercent, settings.DefaultCleaningFee, taxes, rateDtos);
    }

    /// <summary>
    /// docs/01 QT-06/TC-12 — an operator sets a display rate by hand. Setting it
    /// flips the row to Manual so a future feed never overwrites a person; the
    /// rate itself only reaches what the guest SEES — money is charged in the
    /// listing's own currency (docs/07 §6) and Pricing never reads this table.
    /// </summary>
    [HttpPut("admin/exchange-rates/{code}")]
    public async Task<IActionResult> SaveExchangeRate(
        string code, [FromBody] SaveExchangeRateRequest req, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Finance, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền cấu hình tỉ giá." });

        var wanted = (code ?? "").Trim().ToUpperInvariant();
        var rate = await db.ExchangeRates.FirstOrDefaultAsync(r => r.Code == wanted, ct);
        if (rate is null) return NotFound();

        if (!Fx.IsValidRate(rate.Code, req.RateFromVnd))
            return BadRequest(new
            {
                message = rate.Code == Fx.Base
                    ? "VND là tiền gốc — tỉ giá luôn là 1."
                    : "Tỉ giá phải lớn hơn 0."
            });

        var before = Describe(rate);

        rate.RateFromVnd = req.RateFromVnd;
        rate.IsActive = req.IsActive;
        rate.Source = ExchangeRateSource.Manual;
        rate.UpdatedAt = DateTime.UtcNow;
        rate.UpdatedByAdminId = admin.Id;

        audit.Record(admin, "fx.update", $"fx:{rate.Code}", before, Describe(rate));

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string Describe(ExchangeRate r) =>
        $"{r.Code}: {r.RateFromVnd} ({(r.IsActive ? "bật" : "tắt")}, {r.Source})";

    [HttpPut("admin/tax-rules/{id:int}")]
    public async Task<IActionResult> SaveTaxRule(int id, [FromBody] TaxRuleDto req, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Finance, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền cấu hình thuế." });

        var rule = await db.TaxRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();

        var before = Describe(rule);

        rule.Name = string.IsNullOrWhiteSpace(req.Name) ? rule.Name : req.Name.Trim();
        rule.Value = Math.Max(0m, req.Value);
        rule.IsActive = req.IsActive;
        rule.SortOrder = req.SortOrder;
        if (Enum.TryParse<TaxMethod>(req.Method, true, out var method)) rule.Method = method;
        if (Enum.TryParse<TaxBase>(req.Base, true, out var basis)) rule.Base = basis;

        audit.Record(admin, "tax.update", $"tax:{rule.Id}", before, Describe(rule));

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("admin/tax-rules")]
    public async Task<ActionResult<TaxRuleDto>> CreateTaxRule([FromBody] TaxRuleDto req, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Finance, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền cấu hình thuế." });

        var rule = new TaxRule
        {
            Country = string.IsNullOrWhiteSpace(req.Country) ? "Việt Nam" : req.Country.Trim(),
            City = string.IsNullOrWhiteSpace(req.City) ? null : req.City.Trim(),
            Name = string.IsNullOrWhiteSpace(req.Name) ? "Thuế mới" : req.Name.Trim(),
            Value = Math.Max(0m, req.Value),
            SortOrder = req.SortOrder,
            IsActive = req.IsActive
        };
        if (Enum.TryParse<TaxMethod>(req.Method, true, out var method)) rule.Method = method;
        if (Enum.TryParse<TaxBase>(req.Base, true, out var basis)) rule.Base = basis;

        db.TaxRules.Add(rule);
        await db.SaveChangesAsync(ct);

        audit.Record(admin, "tax.create", $"tax:{rule.Id}", null, Describe(rule));
        await db.SaveChangesAsync(ct);

        return Ok(new TaxRuleDto(rule.Id, rule.Country, rule.City, rule.Name,
            rule.Method.ToString(), rule.Base.ToString(), rule.Value, rule.SortOrder, rule.IsActive));
    }

    private static string Describe(TaxRule r) =>
        $"{r.Name}: {r.Method} {r.Value} ({(r.IsActive ? "bật" : "tắt")})";

    private static readonly Dictionary<LedgerAccount, string> AccountLabels = new()
    {
        [LedgerAccount.GuestFunds] = "Tiền khách đang giữ",
        [LedgerAccount.HostPayable] = "Phải trả chủ nhà",
        [LedgerAccount.GuestServiceFeeRevenue] = "Doanh thu phí dịch vụ khách",
        [LedgerAccount.HostServiceFeeRevenue] = "Doanh thu phí dịch vụ chủ nhà",
        [LedgerAccount.TaxPayable] = "Thuế phải nộp",
        [LedgerAccount.GuestRefundPayable] = "Phải hoàn cho khách",
        [LedgerAccount.PromotionalCredit] = "Số dư khuyến mãi đã cấp",
        [LedgerAccount.PlatformExpense] = "Chi phí sàn"
    };

    /// <summary>
    /// docs/03 §5: total in must equal total out, checked every day. Anything
    /// other than zero here is the alarm the spec calls for.
    /// </summary>
    private async Task<LedgerReportDto> ReconcileAsync(CancellationToken ct)
    {
        var rows = await db.LedgerEntries
            .GroupBy(e => new { e.Account, e.Direction })
            .Select(g => new { g.Key.Account, g.Key.Direction, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(ct);

        var accounts = rows
            .GroupBy(r => r.Account)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var debits = g.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Total);
                var credits = g.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Total);
                return new LedgerAccountDto(
                    g.Key.ToString(), AccountLabels.GetValueOrDefault(g.Key, g.Key.ToString()),
                    debits, credits, debits - credits);
            })
            .ToList();

        return new LedgerReportDto(
            accounts.Sum(a => a.Balance),
            rows.Sum(r => r.Count),
            await db.LedgerEntries.Select(e => e.TransactionId).Distinct().CountAsync(ct),
            accounts);
    }

    /* ------------------------------------------------------------- AT-11 */

    /// <summary>
    /// docs/01 AT-11 — the accounts the checks flagged, worst first. These are
    /// hints for a person, not verdicts: nothing was blocked on the way in.
    /// </summary>
    [HttpGet("admin/risk")]
    public async Task<ActionResult<IReadOnlyList<RiskFlagDto>>> RiskFlags(
        [FromQuery] bool includeResolved = false, CancellationToken ct = default)
    {
        var admin = await audit.RequireAsync(AdminScope.Moderation, ct);
        if (admin is null)
            return StatusCode(403, new { message = "Bạn không có quyền kiểm duyệt." });

        var query = db.RiskFlags.AsQueryable();
        if (!includeResolved) query = query.Where(f => f.Status == RiskFlagStatus.Open);

        return Ok(await query
            .OrderByDescending(f => f.Severity).ThenByDescending(f => f.CreatedAt)
            .Take(200)
            .Select(f => new RiskFlagDto(
                f.Id, f.UserId, f.User!.FullName, f.User.Email,
                f.BookingId, f.Booking!.Reference,
                f.Kind.ToString(), f.Severity.ToString(),
                RiskSignals.Label(f.Severity), RiskSignals.BadgeClass(f.Severity),
                f.Summary, f.Detail, f.Status.ToString(), f.Resolution, f.CreatedAt))
            .ToListAsync(ct));
    }

    [HttpPost("admin/risk/{id:int}/resolve")]
    public async Task<IActionResult> ResolveRiskFlag(
        int id, [FromBody] ResolveRiskFlagRequest req, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Moderation, ct);
        if (admin is null)
            return StatusCode(403, new { message = "Bạn không có quyền kiểm duyệt." });

        var flag = await db.RiskFlags.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flag is null) return NotFound();

        audit.Record(admin, "risk.resolve", $"risk:{flag.Id}",
            flag.Status.ToString(), req.Acted ? "Acted" : "Cleared", req.Resolution);

        flag.Status = req.Acted ? RiskFlagStatus.Acted : RiskFlagStatus.Cleared;
        flag.Resolution = req.Resolution?.Trim();
        flag.ResolvedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /* ------------------------------------------- docs/01 TK-06: identity */

    [HttpGet("admin/identity")]
    public async Task<ActionResult<IReadOnlyList<IdentityReviewDto>>> IdentityQueue(
        [FromQuery] bool includeDecided = false, CancellationToken ct = default)
    {
        var admin = await audit.RequireAsync(AdminScope.Moderation, ct);
        if (admin is null)
            return StatusCode(403, new { message = "Bạn không có quyền kiểm duyệt." });

        var query = db.IdentityChecks.AsQueryable();
        if (!includeDecided) query = query.Where(c => c.Status == IdentityCheckStatus.Pending);

        var rows = await query
            .OrderBy(c => c.SubmittedAt)
            .Take(200)
            .Select(c => new
            {
                c.Id, c.UserId,
                UserName = c.User!.DisplayName ?? c.User.FullName,
                c.User.Email,
                c.Document, c.DocumentLast4,
                c.FrontImageUrl, c.BackImageUrl, c.SelfieImageUrl,
                c.Status, c.SubmittedAt
            })
            .ToListAsync(ct);

        return Ok(rows.Select(c => new IdentityReviewDto(
            c.Id, c.UserId, c.UserName, c.Email,
            IdentityChecks.DocumentLabel(c.Document), c.DocumentLast4,
            c.FrontImageUrl, c.BackImageUrl, c.SelfieImageUrl,
            c.Status.ToString(), c.SubmittedAt)).ToList());
    }

    /// <summary>
    /// docs/01 TK-06 — a person decides, and the badge on the public profile is
    /// the direct consequence, so the decision is written to the audit log like
    /// every other thing an admin does (docs/01 QT-09).
    /// </summary>
    [HttpPost("admin/identity/{id:int}/decide")]
    public async Task<IActionResult> DecideIdentity(
        int id, [FromBody] DecideIdentityRequest req, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Moderation, ct);
        if (admin is null)
            return StatusCode(403, new { message = "Bạn không có quyền kiểm duyệt." });

        var check = await db.IdentityChecks.Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (check is null) return NotFound();
        if (check.Status != IdentityCheckStatus.Pending)
            return Conflict(new { message = "Hồ sơ này đã được xử lý." });

        if (!req.Approve && string.IsNullOrWhiteSpace(req.Note))
            return BadRequest(new { message = "Từ chối thì phải nêu lý do để người dùng nộp lại được." });

        audit.Record(admin, "identity.decide", $"identity:{check.Id}",
            "Đang chờ duyệt", req.Approve ? "Đã xác minh" : "Bị từ chối", req.Note);

        check.Status = req.Approve ? IdentityCheckStatus.Approved : IdentityCheckStatus.Rejected;
        check.Note = Profiles.Tidy(req.Note, 500);
        check.DecidedAt = DateTime.UtcNow;
        check.DecidedByUserId = admin.Id;

        // The badge on the public profile is this flag and nothing else.
        if (check.User is { } person) person.IsIdentityVerified = req.Approve;

        await notifications.QueueWithEmailAsync(
            check.User,
            req.Approve ? NotificationKind.ListingApproved : NotificationKind.ListingRejected,
            req.Approve ? "Danh tính đã được xác minh" : "Hồ sơ xác minh danh tính bị từ chối",
            req.Approve
                ? "Hồ sơ của bạn giờ có huy hiệu đã xác minh danh tính."
                : $"Lý do: {check.Note}. Bạn có thể nộp lại hồ sơ khác.",
            "/", ct);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("admin/listings/{id:int}/publish")]
    public async Task<IActionResult> SetPublished(
        int id, [FromQuery] bool published, [FromBody] RestoreRequest? req, CancellationToken ct)
    {
        var listing = await db.Listings.Include(l => l.Host!).ThenInclude(h => h.User)
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing is null) return NotFound();

        // docs/08 §2 TakeDownContent and §1.4 — taking somebody's listing down is
        // a decision, and decisions carry a reason and the conflict check.
        var v = await gate.AllowAsync(AdminAction.TakeDownContent, req?.Reason, ct, listing.Host?.UserId);
        if (!v.Ok) return StatusCode(v.Status ?? 403, new { message = v.Refusal });

        var admin = v.Admin!;

        audit.Record(admin, "listing.publish", $"listing:{listing.Id}",
            listing.IsPublished ? "Đang hiển thị" : "Bản nháp",
            published ? "Đang hiển thị" : "Đã gỡ hiển thị", req?.Reason);

        listing.IsPublished = published;

        await notifications.QueueWithEmailAsync(
            listing.Host?.User,
            published ? NotificationKind.ListingApproved : NotificationKind.ListingRejected,
            published ? "Chỗ nghỉ đã được duyệt" : "Chỗ nghỉ đã bị gỡ hiển thị",
            published
                ? $"\"{listing.Title}\" đã hiển thị công khai và có thể nhận đặt chỗ."
                : $"\"{listing.Title}\" đã được gỡ khỏi kết quả tìm kiếm. Vui lòng liên hệ hỗ trợ để biết thêm.",
            "/hosting", ct);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /* ------------------------------------------ docs/01 AT-01, review queue */

    /// <summary>
    /// docs/01 AT-01 — the places waiting on review before they can be seen. Empty
    /// unless <c>Moderation:NewListingsRequireApproval</c> is on, since nothing
    /// enters the queue otherwise.
    /// </summary>
    [HttpGet("admin/listings/pending")]
    public async Task<ActionResult<IReadOnlyList<PendingListingDto>>> PendingListings(CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Moderation, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền kiểm duyệt." });

        var rows = await db.Listings
            .Where(l => l.ReviewStatus == ListingReviewStatus.Pending && l.IsPublished)
            .OrderBy(l => l.SubmittedForReviewAt)
            .Include(l => l.Images)
            .Include(l => l.Host!).ThenInclude(h => h.User)
            .Select(l => new PendingListingDto(
                l.Id, l.Slug, l.Title, l.City,
                l.Host!.User!.DisplayName, l.Host.UserId ?? 0,
                l.PricePerNight,
                l.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                l.SubmittedForReviewAt))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// docs/01 AT-01 — approve a listing so the public can see it, or reject it
    /// with a reason the host can act on. Both are moderation decisions, so they
    /// go through the same gate and audit line as any takedown (docs/08 §2, §1.4).
    /// <paramref name="decision"/> is "approve" or "reject"; {decision} rather than
    /// {action} because ASP.NET treats {action} as the method name.
    /// </summary>
    [HttpPost("admin/listings/{id:int}/review/{decision}")]
    public async Task<IActionResult> ReviewListing(
        int id, string decision, [FromBody] ModerationDecisionRequest? req, CancellationToken ct)
    {
        var approve = decision.Equals("approve", StringComparison.OrdinalIgnoreCase);
        if (!approve && !decision.Equals("reject", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Quyết định phải là approve hoặc reject." });

        var listing = await db.Listings.Include(l => l.Host!).ThenInclude(h => h.User)
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing is null) return NotFound();

        if (listing.ReviewStatus != ListingReviewStatus.Pending)
            return BadRequest(new { message = "Tin này không nằm trong hàng chờ duyệt." });

        var v = await gate.AllowAsync(AdminAction.TakeDownContent, req?.Reason, ct, listing.Host?.UserId);
        if (!v.Ok) return StatusCode(v.Status ?? 403, new { message = v.Refusal });
        var admin = v.Admin!;

        audit.Record(admin, "listing.review", $"listing:{listing.Id}",
            ListingModeration.Label(listing.ReviewStatus),
            approve ? "Đã duyệt" : "Bị từ chối", req?.Reason);

        listing.ReviewStatus = approve ? ListingReviewStatus.Approved : ListingReviewStatus.Rejected;
        listing.ReviewNote = approve ? null : req?.Reason?.Trim();
        listing.ReviewedAt = DateTime.UtcNow;
        listing.ReviewedByUserId = admin.Id;

        await notifications.QueueWithEmailAsync(
            listing.Host?.User,
            approve ? NotificationKind.ListingApproved : NotificationKind.ListingRejected,
            approve ? "Chỗ nghỉ đã được duyệt" : "Chỗ nghỉ cần chỉnh sửa",
            approve
                ? $"\"{listing.Title}\" đã qua kiểm duyệt và hiển thị công khai."
                : $"\"{listing.Title}\" chưa được duyệt. Lý do: {req?.Reason?.Trim()}. " +
                  "Bạn có thể chỉnh sửa và gửi lại để duyệt.",
            "/hosting", ct);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /* -------------------------------------- docs/01 ĐG-11, review fraud */

    /// <summary>
    /// docs/01 ĐG-11 — reviews that look planted through a secondary account. Every
    /// review sits on a real booking, so the tell is the shape around it: the
    /// reviewer being the host, sharing a creation session, or a fresh account that
    /// only ever stayed with this one host. Flags are for a human, never automatic.
    /// </summary>
    [HttpGet("admin/review-fraud")]
    public async Task<ActionResult<IReadOnlyList<ReviewFraudDto>>> ReviewFraudReport(CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Moderation, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền kiểm duyệt." });

        // Candidate reviews: written by a real account, on a listing whose host has one.
        var reviews = await db.Reviews
            .Where(r => r.AuthorUserId != null && r.PublishedAt != null && r.Listing!.Host!.UserId != null)
            .Select(r => new
            {
                r.Id, r.ListingId, ListingTitle = r.Listing!.Title, r.Rating, r.CreatedAt,
                ReviewerId = r.AuthorUserId!.Value,
                ReviewerName = r.AuthorName,
                ReviewerCreatedAt = r.AuthorUser!.CreatedAt,
                ReviewerSession = r.AuthorUser.AdoptedSessionId,
                HostUserId = r.Listing.Host!.UserId!.Value,
                HostName = r.Listing.Host.Name,
                HostSession = r.Listing.Host.User!.AdoptedSessionId,
                HostId = r.Listing.HostId
            })
            .ToListAsync(ct);

        if (reviews.Count == 0) return Ok(Array.Empty<ReviewFraudDto>());

        // For each reviewer: which hosts they have booked, and how many stays.
        var reviewerIds = reviews.Select(r => r.ReviewerId).Distinct().ToList();
        var bookings = await db.Bookings
            .Where(b => b.GuestUserId != null && reviewerIds.Contains(b.GuestUserId.Value))
            .Select(b => new { GuestId = b.GuestUserId!.Value, b.Listing!.HostId })
            .ToListAsync(ct);
        var byReviewer = bookings
            .GroupBy(b => b.GuestId)
            .ToDictionary(g => g.Key, g => new { Hosts = g.Select(x => x.HostId).Distinct().ToList(), Count = g.Count() });

        var flagged = new List<ReviewFraudDto>();
        foreach (var r in reviews)
        {
            byReviewer.TryGetValue(r.ReviewerId, out var b);
            var onlyThisHost = b is not null && b.Hosts.Count == 1 && b.Hosts[0] == r.HostId;

            var signals = new ReviewFraud.Signals(
                SameAccountAsHost: r.ReviewerId == r.HostUserId,
                SharedSessionWithHost: !string.IsNullOrEmpty(r.ReviewerSession)
                    && r.ReviewerSession == r.HostSession,
                ReviewerAccountAgeDays: Math.Max(0, (int)(r.CreatedAt - r.ReviewerCreatedAt).TotalDays),
                ReviewerOnlyBookedThisHost: onlyThisHost,
                ReviewerStayCount: b?.Count ?? 0,
                Rating: r.Rating);

            var assessment = ReviewFraud.Assess(signals);
            if (!assessment.Flagged) continue;

            flagged.Add(new ReviewFraudDto(
                r.Id, r.ListingId, r.ListingTitle, r.HostName, r.ReviewerName, r.Rating,
                ReviewFraud.RiskLabel(assessment.Level), assessment.Reasons, r.CreatedAt));
        }

        return Ok(flagged
            .OrderByDescending(f => f.Risk == ReviewFraud.RiskLabel(ReviewFraud.Risk.High))
            .ThenByDescending(f => f.CreatedAt)
            .ToList());
    }

    /* ------------------------------------------ docs/01 AT-03, neighbour reports */

    /// <summary>docs/01 AT-03 — neighbour reports waiting to be looked at.</summary>
    [HttpGet("admin/neighbor-reports")]
    public async Task<ActionResult<IReadOnlyList<NeighborReportDto>>> NeighborReportsList(CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Moderation, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền kiểm duyệt." });

        var rows = await db.NeighborReports
            .OrderBy(r => r.Status).ThenByDescending(r => r.CreatedAt)
            .Take(200)
            .Select(r => new NeighborReportDto(
                r.Id, r.Location, StayHost.Domain.NeighborReports.ConcernLabel(r.Category),
                r.Detail, r.Contact, r.Status.ToString(), r.Resolution, r.CreatedAt))
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>docs/01 AT-03 — mark a neighbour report handled.</summary>
    [HttpPost("admin/neighbor-reports/{id:int}/resolve")]
    public async Task<IActionResult> ResolveNeighborReport(
        int id, [FromBody] ResolveReportRequest req, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Moderation, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền kiểm duyệt." });

        var report = await db.NeighborReports.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report is null) return NotFound();

        audit.Record(admin, "neighbor.resolve", $"neighbor:{report.Id}",
            report.Status.ToString(), req.Status, req.Resolution);

        report.Status = Enum.TryParse<ReportStatus>(req.Status, true, out var s) ? s : ReportStatus.Resolved;
        report.Resolution = req.Resolution?.Trim();
        report.ResolvedAt = report.Status is ReportStatus.Resolved or ReportStatus.Dismissed ? DateTime.UtcNow : null;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /* ---------------------------------- docs/01 AT-12, discrimination monitor */

    /// <summary>
    /// docs/01 AT-12 — hosts' decline records, so a pattern of turning guests away
    /// can be seen. Declines whose stated reason leans on a protected characteristic
    /// are flagged for a human to read; the decline itself is never blocked.
    /// Sorted so the hosts with flags, then the highest decline rates, come first.
    /// </summary>
    [HttpGet("admin/decline-monitor")]
    public async Task<ActionResult<IReadOnlyList<DeclineMonitorDto>>> DeclineMonitor(CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Moderation, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền kiểm duyệt." });

        // Everything a host actually responded to, with the host attached.
        var responded = await db.Bookings
            .Where(b => b.RespondedAt != null && b.Listing!.Host!.UserId != null)
            .Select(b => new
            {
                HostId = b.Listing!.HostId,
                HostName = b.Listing.Host!.Name,
                b.Reference,
                b.Status,
                b.CancelledBy,
                b.CancellationReason
            })
            .ToListAsync(ct);

        var rows = responded
            .GroupBy(b => new { b.HostId, b.HostName })
            .Select(g =>
            {
                var declines = g.Where(x =>
                    x.Status == BookingStatus.Declined && x.CancelledBy == CancelledBy.Host).ToList();
                var flagged = declines
                    .Select(d => new { d.Reference, d.CancellationReason, Cat = AntiDiscrimination.Screen(d.CancellationReason) })
                    .Where(x => x.Cat != AntiDiscrimination.Category.None)
                    .Select(x => new FlaggedDeclineDto(
                        x.Reference, x.CancellationReason ?? "", AntiDiscrimination.CategoryLabel(x.Cat)))
                    .ToList();

                var total = g.Count();
                return new DeclineMonitorDto(
                    g.Key.HostId, g.Key.HostName, total, declines.Count,
                    total == 0 ? 0 : (int)Math.Round(100.0 * declines.Count / total),
                    flagged.Count, flagged);
            })
            .Where(r => r.Declined > 0)
            .OrderByDescending(r => r.Flagged)
            .ThenByDescending(r => r.DeclineRatePercent)
            .ToList();

        return Ok(rows);
    }

    /* --------------------------------------- docs/01 QT-07, help articles */

    /// <summary>docs/01 QT-07 — every help article, for the admin editor.</summary>
    [HttpGet("admin/help-articles")]
    public async Task<ActionResult<IReadOnlyList<HelpAdminDto>>> HelpArticles(CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Support, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền quản lý nội dung trợ giúp." });

        var rows = await db.HelpArticles
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Id)
            .Select(a => new HelpAdminDto(
                a.Id, a.Slug, a.Title, a.Category, a.Audience.ToString(),
                a.Summary, a.Body, a.SortOrder, a.UpdatedAt))
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>docs/01 QT-07 — create a help article or update an existing one.</summary>
    [HttpPost("admin/help-articles")]
    public async Task<ActionResult<HelpAdminDto>> SaveHelpArticle(
        [FromBody] HelpArticleSaveRequest req, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Support, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền quản lý nội dung trợ giúp." });

        var title = (req.Title ?? "").Trim();
        var body = (req.Body ?? "").Trim();
        if (title.Length < 4) return BadRequest(new { message = "Tiêu đề cần tối thiểu 4 ký tự." });
        if (body.Length < 20) return BadRequest(new { message = "Nội dung cần tối thiểu 20 ký tự." });

        var audience = Enum.TryParse<HelpAudience>(req.Audience, true, out var a) ? a : HelpAudience.Everyone;

        HelpArticle? article = req.Id is int id
            ? await db.HelpArticles.FirstOrDefaultAsync(x => x.Id == id, ct)
            : null;

        var slug = string.IsNullOrWhiteSpace(req.Slug)
            ? SearchText.Normalize(title).Replace(' ', '-')
            : req.Slug.Trim();

        // A new article, or one whose slug changed, must not collide with another.
        var clash = await db.HelpArticles.AnyAsync(
            x => x.Slug == slug && (article == null || x.Id != article.Id), ct);
        if (clash) return BadRequest(new { message = "Đường dẫn (slug) này đã có bài khác dùng." });

        var before = article is null ? "chưa có" : article.Title;
        if (article is null)
        {
            article = new HelpArticle();
            db.HelpArticles.Add(article);
        }

        article.Slug = slug;
        article.Title = title;
        article.Category = (req.Category ?? "").Trim();
        article.Audience = audience;
        article.Summary = (req.Summary ?? "").Trim();
        article.Body = body;
        article.SortOrder = req.SortOrder;
        article.UpdatedAt = DateTime.UtcNow;
        article.RefreshSearchText();

        audit.Record(admin, "help.save", $"help:{slug}", before, title, null);

        await db.SaveChangesAsync(ct);
        return Ok(new HelpAdminDto(
            article.Id, article.Slug, article.Title, article.Category, article.Audience.ToString(),
            article.Summary, article.Body, article.SortOrder, article.UpdatedAt));
    }

    /// <summary>docs/01 QT-07 — remove a help article.</summary>
    [HttpDelete("admin/help-articles/{id:int}")]
    public async Task<IActionResult> DeleteHelpArticle(int id, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Support, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền quản lý nội dung trợ giúp." });

        var article = await db.HelpArticles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (article is null) return NoContent();

        audit.Record(admin, "help.delete", $"help:{article.Slug}", article.Title, "đã xoá", null);
        db.HelpArticles.Remove(article);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /* ------------------------------------------ docs/01 QT-08, feature flags */

    /// <summary>docs/01 QT-08 — the feature flags and their rollout percentages.</summary>
    [HttpGet("admin/feature-flags")]
    public async Task<ActionResult<IReadOnlyList<FeatureFlagDto>>> FeatureFlags(CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Super, ct);
        if (admin is null) return StatusCode(403, new { message = "Chỉ quản trị tối cao mới quản lý được tính năng." });

        var rows = await db.FeatureFlags
            .OrderBy(f => f.Key)
            .Select(f => new FeatureFlagDto(f.Key, f.Description, f.Enabled, f.RolloutPercent, f.UpdatedAt))
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>
    /// docs/01 QT-08 — turn a feature on or off and set the share of users who see
    /// it. Creates the flag if it does not exist yet.
    /// </summary>
    [HttpPost("admin/feature-flags")]
    public async Task<ActionResult<FeatureFlagDto>> SaveFeatureFlag(
        [FromBody] FeatureFlagRequest req, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Super, ct);
        if (admin is null) return StatusCode(403, new { message = "Chỉ quản trị tối cao mới quản lý được tính năng." });

        var key = (req.Key ?? "").Trim();
        if (key.Length == 0) return BadRequest(new { message = "Thiếu mã tính năng." });

        var flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key, ct);
        var percent = FeatureRollout.ClampPercent(req.RolloutPercent);
        var before = flag is null ? "chưa có" : $"{(flag.Enabled ? "bật" : "tắt")} {flag.RolloutPercent}%";

        if (flag is null)
        {
            flag = new FeatureFlag { Key = key };
            db.FeatureFlags.Add(flag);
        }
        flag.Description = (req.Description ?? "").Trim();
        flag.Enabled = req.Enabled;
        flag.RolloutPercent = percent;
        flag.UpdatedAt = DateTime.UtcNow;

        audit.Record(admin, "feature.rollout", $"feature:{key}",
            before, $"{(req.Enabled ? "bật" : "tắt")} {percent}%", null);

        await db.SaveChangesAsync(ct);
        return Ok(new FeatureFlagDto(flag.Key, flag.Description, flag.Enabled, flag.RolloutPercent, flag.UpdatedAt));
    }

    [HttpPost("admin/reports/{id:int}/resolve")]
    public async Task<IActionResult> ResolveReport(int id, [FromBody] ResolveReportRequest req, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Moderation, ct);
        if (admin is null)
            return StatusCode(403, new { message = "Bạn không có quyền kiểm duyệt." });

        var report = await db.AbuseReports.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report is null) return NotFound();

        audit.Record(admin, "report.resolve", $"report:{report.Id}",
            report.Status.ToString(), req.Status, req.Resolution);

        report.Status = Enum.TryParse<ReportStatus>(req.Status, true, out var status) ? status : ReportStatus.Resolved;
        report.Resolution = req.Resolution?.Trim();
        report.ResolvedAt = report.Status is ReportStatus.Resolved or ReportStatus.Dismissed
            ? DateTime.UtcNow
            : null;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<List<ReportDto>> LoadReportsAsync(CancellationToken ct)
    {
        var reports = await db.AbuseReports
            .Include(r => r.Listing)
            .Include(r => r.ReportedUser)
            .Include(r => r.Message)
            .Include(r => r.Review)
            .Include(r => r.ReporterUser)
            .OrderBy(r => r.Status)
            .ThenByDescending(r => r.CreatedAt)
            .Take(40)
            .ToListAsync(ct);

        return reports.Select(r => new ReportDto(
            r.Id, r.Target.ToString(), Reports.TargetLabel(r.Target), r.SubjectId,
            SubjectTitle(r), r.Reason, r.Detail,
            r.Status.ToString(), r.Resolution,
            r.ReporterUser?.FullName ?? "Khách ẩn danh", r.CreatedAt)).ToList();
    }

    /// <summary>
    /// One line naming what a moderator is looking at. A message and a review are
    /// quoted rather than linked because both can be long and both can be deleted
    /// out from under the queue; the quote is what the report was actually about.
    /// </summary>
    private static string SubjectTitle(AbuseReport r) => r.Target switch
    {
        ReportTarget.Listing => r.Listing?.Title ?? "(tin đăng đã xoá)",
        ReportTarget.User => r.ReportedUser?.FullName ?? "(tài khoản đã xoá)",
        ReportTarget.Message => Excerpt(r.Message?.Body) ?? "(tin nhắn đã xoá)",
        ReportTarget.Review => Excerpt(r.Review?.Text) ?? "(đánh giá đã xoá)",
        _ => ""
    };

    private static string? Excerpt(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var one = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return one.Length <= 140 ? one : one[..140] + "…";
    }
}
