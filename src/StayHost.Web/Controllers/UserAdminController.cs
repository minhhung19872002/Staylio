using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/08 — the user-administration console: finding somebody, seeing what the
/// platform knows about them, and the ladder of things that can be done about
/// it.
///
/// Every endpoint goes through <see cref="AdminGate"/>, which is where the four
/// principles of §1 live. Nothing here re-decides who is allowed to do what.
/// </summary>
[ApiController]
[Route("api/admin/users")]
public class UserAdminController(
    StayHostDbContext db, AdminGate gate, AdminAudit audit,
    NotificationService notifications, AuthService auth, RefundGateway refunds) : ControllerBase
{
    private ActionResult Refuse(AdminGate.Verdict v) =>
        StatusCode(v.Status ?? 403, new { message = v.Refusal });

    /* ------------------------------------------------------------ QT-U-01 */

    /// <summary>
    /// docs/08 §4 — found by email, phone, name, booking reference, listing id
    /// or payment reference. All six, because support is handed whichever one
    /// the caller happens to have.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminUserRowDto>>> Search(
        [FromQuery] string? q, CancellationToken ct)
    {
        var v = await gate.AllowAsync(AdminAction.FindUser, null, ct);
        if (!v.Ok) return Refuse(v);

        var term = (q ?? "").Trim();

        // No term is not "no answer": an operator opening the console wants to
        // see who is on the platform, and guessing a search word to get a first
        // row is busywork. The newest accounts are what that question usually
        // means, and they are also where anything suspicious shows up first.
        if (term.Length < 2)
        {
            return Ok(await db.Users
                .OrderByDescending(u => u.Id)
                .Take(50)
                .Select(u => new AdminUserRowDto(
                    u.Id, u.FullName, u.Email, u.Phone, u.Role.ToString(),
                    u.IsBanned ? "Khoá vĩnh viễn" : u.IsSuspended ? "Tạm khoá" : "Bình thường",
                    u.IsIdentityVerified, u.CreatedAt))
                .ToListAsync(ct));
        }

        var lower = term.ToLowerInvariant();

        // A booking code has two people on it. Support is usually handed the code
        // by one of them and needs to reach the other, so both come back.
        var byBooking = (await db.Bookings
                .Where(b => b.Reference.ToLower() == lower
                            || (b.Payment != null && b.Payment.Reference.ToLower() == lower))
                .Select(b => new { b.GuestUserId, HostUserId = (int?)b.Listing!.Host!.UserId })
                .ToListAsync(ct))
            .SelectMany(b => new[] { b.GuestUserId, b.HostUserId })
            .ToList();

        var listingId = int.TryParse(term, out var parsed) ? parsed : 0;

        var byListing = await db.Listings
            .Where(l => l.Id == listingId || l.Slug == lower)
            .Select(l => l.Host!.UserId)
            .ToListAsync(ct);

        var ids = byBooking.Concat(byListing).Where(id => id.HasValue).Select(id => id!.Value).ToList();

        var users = await db.Users
            .Where(u => ids.Contains(u.Id)
                        || u.Email.ToLower().Contains(lower)
                        || (u.Phone != null && u.Phone.Contains(term))
                        || u.FullName.ToLower().Contains(lower))
            .OrderBy(u => u.Id)
            .Take(30)
            .Select(u => new AdminUserRowDto(
                u.Id, u.FullName, u.Email, u.Phone, u.Role.ToString(),
                u.IsBanned ? "Khoá vĩnh viễn" : u.IsSuspended ? "Tạm khoá" : "Bình thường",
                u.IsIdentityVerified, u.CreatedAt))
            .ToListAsync(ct);

        await db.SaveChangesAsync(ct);
        return Ok(users);
    }

    /* ------------------------------------------------- QT-U-02 and QT-U-03 */

    /// <summary>docs/08 §4 — everything the console may show about one person.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AdminUserDto>> Profile(
        int id, [FromQuery] int? ticketId, CancellationToken ct)
    {
        var v = await gate.AllowAsync(AdminAction.FindUser, null, ct);
        if (!v.Ok) return Refuse(v);

        var user = await db.Users
            .Include(u => u.HostProfile)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null) return NotFound(new { message = "Không tìm thấy người dùng." });

        // docs/08 §10 — the look itself is recorded, ticket or no ticket.
        gate.RecordProfileView(v.Admin!, id, ticketId);

        var listings = user.HostProfile is { } host
            ? await db.Listings.Where(l => l.HostId == host.Id)
                .Select(l => new AdminListingRowDto(l.Id, l.Title, l.City, l.IsPublished, l.Rating, l.ReviewCount))
                .ToListAsync(ct)
            : [];

        var asGuest = await db.Bookings.Where(b => b.GuestUserId == id).ToListAsync(ct);

        var asHost = user.HostProfile is { } h
            ? await db.Bookings.Where(b => b.Listing!.HostId == h.Id).ToListAsync(ct)
            : [];

        // Only cancellations this person actually made. A stay the platform
        // called off — because the other side was locked — is not their doing
        // and must not show up as their cancellation rate.
        var cancelledByThem =
            asGuest.Count(b => b.Status == BookingStatus.CancelledByGuest && b.CancelledBy == CancelledBy.Guest)
            + asHost.Count(b => b.Status == BookingStatus.CancelledByHost && b.CancelledBy == CancelledBy.Host);
        var allBookings = asGuest.Count + asHost.Count;

        var sanctions = await db.Sanctions
            .Where(s => s.UserId == id)
            .Include(s => s.DecidedByUser)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SanctionRowDto(
                s.Id, s.Level.ToString(), Sanctions.Label(s.Level),
                s.Restriction == null ? null : Sanctions.RestrictionLabel(s.Restriction.Value),
                s.Policy, s.Reason, s.LiftedWhen,
                s.DecidedByUser!.FullName, s.CreatedAt, s.ExpiresAt,
                s.LiftedAt, s.LiftedReason, s.OverturnedOnAppeal, s.Severe))
            .ToListAsync(ct);

        var hostId = user.HostProfile?.Id;

        // docs/08 §4 — "báo cáo bị nhận". A host collects listing reports; a
        // guest collects cases the other side opened about them, and counting
        // only the first left every guest reading a blameless zero.
        // docs/01 AT-02 added reports filed against a person directly; those are
        // the plainest form of "báo cáo bị nhận" there is, so they count here too.
        var reportsAgainst =
            (hostId is { } reportedHost
                ? await db.AbuseReports.CountAsync(
                    r => r.Target == ReportTarget.Listing && r.Listing!.HostId == reportedHost, ct)
                : 0)
            + await db.AbuseReports.CountAsync(
                r => r.Target == ReportTarget.User && r.ReportedUserId == id, ct)
            + await db.ResolutionCases.CountAsync(
                c => (c.Booking!.GuestUserId == id && c.OpenedByHost)
                     || (hostId != null && c.Booking!.Listing!.HostId == hostId && !c.OpenedByHost), ct);

        // §4 — "hồ sơ tranh chấp" in its own right, open ones called out.
        var disputes = await db.ResolutionCases
            .Where(c => c.OpenedByUserId == id
                        || c.Booking!.GuestUserId == id
                        || (hostId != null && c.Booking!.Listing!.HostId == hostId))
            .Select(c => c.Status)
            .ToListAsync(ct);

        // docs/01 TC-07 — spendable balance, not the raw sum of the rows.
        var balance = CreditLedger.Available(
            await db.CreditEntries.Where(c => c.UserId == id).ToListAsync(ct), DateTime.UtcNow);

        // §4 lists gift cards next to the balance: they are money this person
        // holds that never passes through the credit ledger until redeemed.
        var giftCards = await db.GiftCards
            .Where(g => g.PurchasedByUserId == id || g.RedeemedByUserId == id)
            .Select(g => new { g.Remaining, g.Status })
            .ToListAsync(ct);

        var cards = await db.SavedCards.Where(c => c.UserId == id)
            .Select(c => SavedCards.BrandLabel(c.Brand) + " ••••" + c.Last4)
            .ToListAsync(ct);

        var sessions = await db.AuthSessions
            .Where(s => s.UserId == id)
            .OrderByDescending(s => s.CreatedAt)
            .Take(10)
            .Select(s => new AdminSessionRowDto(s.UserAgent ?? "", s.CreatedAt, s.RevokedAt == null, s.IpAddress))
            .ToListAsync(ct);

        // §4 — "Vai trò: khách, chủ nhà, co-host, danh hiệu."
        var coHostOf = (await db.CoHosts
                .Where(c => c.CoHostUserId == id && c.Status == CoHostStatus.Active)
                .Select(c => new { Owner = c.OwnerUser!.FullName, c.Scope, Listing = c.Listing!.Title })
                .ToListAsync(ct))
            .Select(c => $"{c.Owner}{(c.Listing == null ? "" : $" · {c.Listing}")}: {CoHostScopes.Describe(c.Scope)}")
            .ToList();

        var guestFavourite = hostId is { } favouriteHost
            && await db.Listings.AnyAsync(l => l.HostId == favouriteHost && l.IsGuestFavorite, ct);

        // §4 — reviews written AND received. A host is reviewed on their
        // listings; a guest is reviewed by their hosts.
        var reviewsReceived =
            (hostId is { } reviewedHost
                ? await db.Reviews.CountAsync(r => r.Listing!.HostId == reviewedHost, ct)
                : 0)
            + await db.GuestReviews.CountAsync(r => r.GuestUserId == id, ct);

        // §4 — the two-sided history itself, not just its count.
        var recent = await db.Bookings
            .Where(b => b.GuestUserId == id || (hostId != null && b.Listing!.HostId == hostId))
            .OrderByDescending(b => b.CreatedAt)
            .Take(20)
            .Select(b => new
            {
                b.Id, b.Reference, b.Status, b.CheckIn, b.CheckOut, b.Total,
                Listing = b.Listing!.Title,
                AsGuest = b.GuestUserId == id
            })
            .ToListAsync(ct);

        var recentBookings = recent
            .Select(b => new AdminBookingRowDto(
                b.Id, b.Reference, b.AsGuest ? "Khách" : "Chủ nhà", b.Listing,
                b.Status.ToString(), BookingLifecycle.Label(b.Status),
                b.CheckIn, b.CheckOut, b.Total))
            .ToList();

        // docs/08 §4 — payout account is Finance's to see, and only the tail.
        var maySeePayout = AdminActions.Allows(v.Admin!.AdminScope, AdminAction.ViewPayoutAccount);

        await db.SaveChangesAsync(ct);

        return Ok(new AdminUserDto(
            user.Id, user.FullName, user.DisplayName, user.Email, user.Phone,
            user.Role.ToString(),
            user.IsBanned ? "Khoá vĩnh viễn" : user.IsSuspended ? "Tạm khoá" : "Bình thường",
            user.IsSuspended || user.IsBanned,
            user.SuspendedUntil,
            user.EmailConfirmed, user.PhoneConfirmed, user.IsIdentityVerified,
            user.CreatedAt,
            sessions.FirstOrDefault()?.At,
            user.HostProfile is not null,
            user.HostProfile?.IsSuperhost ?? false,
            listings,
            allBookings, cancelledByThem,
            allBookings == 0 ? 0 : Math.Round(100.0 * cancelledByThem / allBookings, 1),
            await db.Reviews.CountAsync(r => r.AuthorUserId == id, ct),
            reportsAgainst,
            sanctions,
            balance,
            maySeePayout ? user.HostProfile?.PayoutAccountLast4 : null,
            maySeePayout ? user.HostProfile?.PayoutBankName : null,
            cards,
            sessions,
            await RelatedAsync(user, ct),
            AdminActions.All
                .Where(r => AdminActions.Allows(v.Admin.AdminScope, r.Action))
                .Select(r => r.Action.ToString())
                .ToList(),
            guestFavourite,
            coHostOf,
            reviewsReceived,
            disputes.Count(s => s is ResolutionStatus.AwaitingResponse or ResolutionStatus.Disputed),
            disputes.Count,
            giftCards.Count,
            giftCards.Where(g => g.Status == GiftCardStatus.Active).Sum(g => g.Remaining),
            recentBookings));
    }

    /// <summary>
    /// docs/08 §4 and QT-U-03 — accounts that share a phone, a device or the
    /// account the money goes to. The same signal that catches fraud is what
    /// tells an admin they should not be handling this one (§1.3).
    /// </summary>
    private async Task<IReadOnlyList<AdminUserRowDto>> RelatedAsync(User user, CancellationToken ct)
    {
        var devices = await db.AuthSessions
            .Where(s => s.UserId == user.Id && s.UserAgent != null)
            .Select(s => s.UserAgent!)
            .Distinct()
            .ToListAsync(ct);

        var payoutTail = user.HostProfile?.PayoutAccountLast4;

        return await db.Users
            .Where(u => u.Id != user.Id
                        && ((user.Phone != null && u.Phone == user.Phone)
                            || (payoutTail != null && u.HostProfile!.PayoutAccountLast4 == payoutTail)
                            || u.Sessions.Any(s => s.UserAgent != null && devices.Contains(s.UserAgent))))
            .Take(20)
            .Select(u => new AdminUserRowDto(
                u.Id, u.FullName, u.Email, u.Phone, u.Role.ToString(),
                u.IsBanned ? "Khoá vĩnh viễn" : u.IsSuspended ? "Tạm khoá" : "Bình thường",
                u.IsIdentityVerified, u.CreatedAt))
            .ToListAsync(ct);
    }

    /* ------------------------------------------------------------ QT-U-07 */

    /// <summary>
    /// docs/08 §6 and QT-U-07 — what locking this account would cost, shown
    /// before anything is done. "Hệ thống phải hiện cảnh báo trước khi admin bấm
    /// xác nhận."
    /// </summary>
    [HttpGet("{id:int}/lock-preview")]
    public async Task<ActionResult<LockPreviewDto>> LockPreview(
        int id, [FromQuery] bool refundInFull, CancellationToken ct)
    {
        var v = await gate.AllowAsync(AdminAction.FindUser, null, ct);
        if (!v.Ok) return Refuse(v);

        var user = await db.Users.Include(u => u.HostProfile).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        var preview = await BuildPreviewAsync(user, refundInFull, ct);

        return Ok(new LockPreviewDto(
            preview.Lines.Select(l => new LockLineDto(
                l.BookingId, l.Reference, l.Action.ToString(), l.Money, l.CounterpartyName, l.Note)).ToList(),
            preview.GuestsStaying, preview.BookingsCancelled,
            preview.MoneyRefunded, preview.PayoutHeld,
            preview.Warning,
            SuspensionImpact.MustKeepAbleToRespond(preview) ? SuspensionImpact.OpenDisputeNotice() : null,
            SuspensionImpact.SafetyRelocationNotice(preview.GuestsStaying),
            user.HostProfile is not null,
            SuspensionImpact.BalanceNotice()));
    }

    private async Task<SuspensionImpact.Preview> BuildPreviewAsync(
        User user, bool refundInFull, CancellationToken ct)
    {
        var hostId = user.HostProfile?.Id;

        var rows = await db.Bookings
            .Where(b => b.GuestUserId == user.Id || (hostId != null && b.Listing!.HostId == hostId))
            .Include(b => b.Payment)
            .Include(b => b.GuestUser)
            .Include(b => b.Listing!).ThenInclude(l => l.Host)
            .Where(b => b.Status != BookingStatus.CancelledByGuest
                        && b.Status != BookingStatus.CancelledByHost
                        && b.Status != BookingStatus.Declined
                        && b.Status != BookingStatus.Expired)
            .ToListAsync(ct);

        var disputed = await db.ResolutionCases
            .Where(c => c.Status == ResolutionStatus.AwaitingResponse || c.Status == ResolutionStatus.Disputed)
            .Select(c => c.BookingId)
            .ToListAsync(ct);

        SuspensionImpact.Booking Row(Booking b, bool hosted) => new(
            b.Id, b.Reference, b.Status, b.CheckIn, b.CheckOut,
            b.DepositPaid > 0 ? b.DepositPaid : b.Total,
            b.Payment?.HostPayout ?? b.HostPayout,
            hosted ? b.GuestName ?? "Khách" : b.Listing?.Host?.Name ?? "Chủ nhà",
            b.Payment?.PayoutStatus == PayoutStatus.Paid,
            disputed.Contains(b.Id));

        var hosted = rows.Where(b => hostId != null && b.Listing?.HostId == hostId).ToList();
        var travelled = rows.Where(b => b.GuestUserId == user.Id && !hosted.Contains(b)).ToList();

        return SuspensionImpact.Combine(
            SuspensionImpact.ForHost(hosted.Select(b => Row(b, true))),
            SuspensionImpact.ForGuest(travelled.Select(b => Row(b, false)), refundInFull));
    }

    /* -------------------------------------- QT-U-04, QT-U-05 and QT-U-06 */

    /// <summary>
    /// docs/08 §5 — one endpoint for the whole ladder, because the ladder is the
    /// rule: which step is allowed depends on the steps already taken, and
    /// splitting it across four endpoints is how that gets forgotten.
    /// </summary>
    [HttpPost("{id:int}/sanction")]
    public async Task<ActionResult<AdminUserDto>> Sanction(
        int id, [FromBody] SanctionRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<SanctionLevel>(req.Level, true, out var level))
            return BadRequest(new { message = "Mức xử lý không hợp lệ." });

        var action = level switch
        {
            SanctionLevel.Warning => AdminAction.Warn,
            SanctionLevel.Restriction => AdminAction.Restrict,
            SanctionLevel.Suspension => AdminAction.Suspend,
            _ => AdminAction.Ban
        };

        var v = await gate.AllowAsync(action, req.Reason, ct, id);
        if (!v.Ok) return Refuse(v);

        var user = await db.Users.Include(u => u.HostProfile).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        var severe = level >= SanctionLevel.Suspension && Sanctions.IsSevereGround(req.SevereGround);

        // docs/08 §5 — the ladder, unless §5.6 applies.
        var highest = await db.Sanctions
            .Where(s => s.UserId == id && !s.OverturnedOnAppeal)
            .Select(s => (SanctionLevel?)s.Level)
            .OrderByDescending(l => l)
            .FirstOrDefaultAsync(ct);

        if (!Sanctions.IsInOrder(highest, level, severe))
            return BadRequest(new { message = Sanctions.OutOfOrderMessage(highest, level) });

        if (level == SanctionLevel.Ban && !Sanctions.BanGrounds.Contains(req.SevereGround ?? ""))
        {
            return BadRequest(new
            {
                message = "Khoá vĩnh viễn chỉ dùng cho: " + string.Join(", ", Sanctions.BanGrounds).ToLowerInvariant() + ".",
                grounds = Sanctions.BanGrounds
            });
        }

        RestrictionKind? restriction = null;
        if (level == SanctionLevel.Restriction)
        {
            if (!Enum.TryParse<RestrictionKind>(req.Restriction, true, out var kind))
                return BadRequest(new { message = "Chưa chọn loại hạn chế." });
            restriction = kind;
        }

        var now = DateTime.UtcNow;

        var sanction = new Sanction
        {
            UserId = id,
            Level = level,
            Restriction = restriction,
            Policy = (req.Policy ?? "").Trim(),
            Reason = req.Reason!.Trim(),
            LiftedWhen = string.IsNullOrWhiteSpace(req.LiftedWhen) ? null : req.LiftedWhen.Trim(),
            DecidedByUserId = v.Admin!.Id,
            ExpiresAt = req.Days is > 0 ? now.AddDays(req.Days.Value) : null,
            Severe = severe
        };

        db.Sanctions.Add(sanction);

        await ApplyAsync(user, sanction, req.RefundInFull, ct);

        audit.Record(v.Admin, $"user.{level}".ToLowerInvariant(), $"user:{id}",
            user.IsSuspended ? "đã bị khoá" : "bình thường",
            Sanctions.Label(level), req.Reason);

        // docs/08 §5.6 — a severe jump goes straight onto a Super's desk, with
        // the 24-hour clock already running.
        if (severe)
        {
            var super = await db.Users
                .Where(u => u.Id != v.Admin.Id && u.Role == UserRole.Admin
                            && u.AdminScope.HasFlag(AdminScope.Super))
                .OrderBy(u => u.Id)
                .FirstOrDefaultAsync(ct);

            if (super is not null)
            {
                await notifications.QueueWithEmailAsync(super, NotificationKind.System,
                    "Hồ sơ vi phạm nghiêm trọng cần xem lại trong 24 giờ",
                    $"{v.Admin.FullName} vừa {Sanctions.Label(level).ToLowerInvariant()} người dùng #{id} " +
                    $"theo §5.6 ({req.SevereGround}). Hạn xem lại: " +
                    $"{Sanctions.SevereReviewDueBy(now):HH:mm dd/MM/yyyy}.",
                    "/admin", ct);
            }
        }

        // docs/08 §8 — a suspension takes the sign-in away, so the appeal right
        // travels in the email as a token instead of a page they can no longer open.
        string appealLink = "/account/sanctions";
        if (level >= SanctionLevel.Suspension)
        {
            var token = SanctionsController.IssueAppealToken(user.Id);
            db.UserTokens.Add(token);
            appealLink = $"/appeal?token={token.Token}";
        }

        await db.SaveChangesAsync(ct);

        await notifications.QueueWithEmailAsync(user, NotificationKind.System,
            $"Staylio: {Sanctions.Label(level).ToLowerInvariant()}",
            Sanctions.Notice(sanction) +
            $" Bạn có thể khiếu nại một lần trong {Appeals.WindowDays} ngày theo liên kết trong thư này.",
            appealLink, ct);

        return await Profile(id, null, ct);
    }

    /// <summary>docs/08 §5 and §6 — what the sanction actually does to the account.</summary>
    private async Task ApplyAsync(User user, Sanction sanction, bool refundInFull, CancellationToken ct)
    {
        switch (sanction.Level)
        {
            case SanctionLevel.Warning:
                // Nothing is blocked. That is what makes it a warning.
                break;

            case SanctionLevel.Restriction when sanction.Restriction is { } kind:
                user.RestrictionMask |= 1 << (int)kind;
                if (kind == RestrictionKind.ListingsHiddenFromSearch) await HideListingsAsync(user, ct);
                if (kind == RestrictionKind.PayoutsHeld) await HoldPayoutsAsync(user, ct);
                break;

            case SanctionLevel.Suspension:
            case SanctionLevel.Ban:
                user.IsSuspended = true;
                user.SuspendedUntil = sanction.ExpiresAt;
                user.IsBanned = sanction.Level == SanctionLevel.Ban;

                // docs/08 §6 — an open case keeps the door open enough to answer it.
                var preview = await BuildPreviewAsync(user, refundInFull, ct);
                user.MayStillRespondToDisputes = SuspensionImpact.MustKeepAbleToRespond(preview);

                // The preview the admin confirmed is a promise; this keeps it.
                await ExecuteFalloutAsync(user, preview, ct);

                await HideListingsAsync(user, ct);
                await HoldPayoutsAsync(user, ct);

                // Every session ends now; a lock that leaves a live cookie is not a lock.
                foreach (var s in await db.AuthSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null).ToListAsync(ct))
                    s.RevokedAt = DateTime.UtcNow;
                break;
        }
    }

    /// <summary>
    /// docs/08 §6 — what the lock-preview said would happen, actually happening.
    /// Cancellations go through <see cref="BookingsController.PostCancellation"/>
    /// — the same path as any other cancellation — so the ledger, the booking
    /// history and the refund maths come out identical to a normal cancel.
    /// </summary>
    private async Task ExecuteFalloutAsync(User user, SuspensionImpact.Preview preview, CancellationToken ct)
    {
        foreach (var line in preview.Lines)
        {
            var booking = await db.Bookings
                .Include(b => b.Payment)
                .Include(b => b.GuestUser)
                .Include(b => b.Listing!).ThenInclude(l => l.Host!).ThenInclude(h => h.User)
                .FirstOrDefaultAsync(b => b.Id == line.BookingId, ct);
            if (booking is null) continue;

            // Which side of this booking the locked person is on.
            var isHost = user.HostProfile is { } hp && booking.Listing?.HostId == hp.Id;

            switch (line.Action)
            {
                case BookingFallout.CancelRefundFull:
                case BookingFallout.CancelPerPolicy:
                {
                    // §6 — the platform cancelled, not the host, so no host
                    // penalty; a guest lock may instead apply the booking's own
                    // policy ("hoặc hoàn 100% tuỳ mức độ vi phạm — admin chọn").
                    var by = line.Action == BookingFallout.CancelRefundFull
                        ? CancelledBy.Platform
                        : CancelledBy.Guest;

                    var outcome = Cancellation.Refund(new Cancellation.Context
                    {
                        Booking = booking,
                        Now = DateTime.UtcNow,
                        By = by
                    });

                    // docs/07 §10 — the money goes back through the gateway that
                    // took it, or becomes balance. Not assumed either way.
                    var sentBack = await refunds.SendAsync(
                        booking, outcome.Amount, "system", "Tai khoan bi khoa", ct);

                    BookingsController.PostCancellation(db, booking, outcome, by,
                        isHost ? "Sàn huỷ: tài khoản chủ nhà bị khoá." : "Sàn huỷ: tài khoản khách bị khoá.",
                        sentBack);

                    if (isHost && booking.GuestUser is not null)
                    {
                        // §6 — "hoàn 100% cho khách, gửi khách danh sách chỗ thay thế."
                        var city = booking.Listing?.City ?? "";
                        await notifications.QueueWithEmailAsync(booking.GuestUser, NotificationKind.BookingCancelled,
                            "Đơn của bạn đã được hoàn tiền đầy đủ",
                            $"Đơn {booking.Reference} bị huỷ vì chỗ nghỉ ngừng hoạt động trên Staylio. " +
                            $"Bạn được hoàn {outcome.Amount:#,##0}₫ — không phải do bạn. " +
                            $"Chúng tôi đã chọn sẵn các chỗ tương tự ở {city} cho bạn.",
                            $"/search?city={Uri.EscapeDataString(city)}", ct);
                    }
                    else if (!isHost)
                    {
                        await notifications.QueueWithEmailAsync(booking.Listing?.Host?.User, NotificationKind.BookingCancelled,
                            "Một đơn đã bị sàn huỷ",
                            $"Đơn {booking.Reference} đã bị huỷ vì tài khoản khách bị khoá. " +
                            "Lịch của bạn đã mở lại cho những ngày đó.",
                            "/hosting", ct);
                    }
                    break;
                }

                case BookingFallout.CancelRequest:
                {
                    // §6 — "Tự động huỷ yêu cầu, báo khách." Nothing was captured,
                    // so there is no money to move — only a request to close.
                    var to = booking.Status == BookingStatus.PendingHostApproval
                        ? BookingStatus.Expired
                        : BookingStatus.CancelledByGuest;

                    db.BookingEvents.Add(BookingLifecycle.Transition(booking, to, "system",
                        isHost ? "Tự huỷ: tài khoản chủ nhà bị khoá." : "Tự huỷ: tài khoản khách bị khoá."));

                    if (isHost && booking.GuestUser is not null)
                    {
                        await notifications.QueueWithEmailAsync(booking.GuestUser, NotificationKind.BookingDeclined,
                            "Yêu cầu đặt chỗ đã đóng",
                            $"Yêu cầu {booking.Reference} không thể tiếp tục vì chỗ nghỉ ngừng hoạt động. " +
                            "Bạn chưa bị trừ tiền.",
                            "/search", ct);
                    }
                    break;
                }

                // LeaveAlone: somebody is in that house tonight — §6 says not to
                // touch it. HoldPayout: applied wholesale by HoldPayoutsAsync.
            }
        }
    }

    private async Task HideListingsAsync(User user, CancellationToken ct)
    {
        if (user.HostProfile is not { } host) return;

        var now = DateTime.UtcNow;

        foreach (var l in await db.Listings.Where(l => l.HostId == host.Id && l.IsPublished).ToListAsync(ct))
        {
            l.IsPublished = false;
            // Remembered so §5.5 restore republishes exactly these — and so the
            // host cannot simply flip them back on while the sanction stands.
            l.HiddenBySanctionAt = now;
        }
    }

    private async Task HoldPayoutsAsync(User user, CancellationToken ct)
    {
        if (user.HostProfile is not { } host) return;

        var payments = await db.Payments
            // Not Sent: those rows are already in a file at the bank. Holding
            // them here and releasing them on restore put them in a second file.
            // Stopping a transfer already sent is done by refusing its batch.
            .Where(p => p.PayoutStatus != PayoutStatus.Paid && p.PayoutStatus != PayoutStatus.Sent
                        && p.Booking!.Listing!.HostId == host.Id)
            .ToListAsync(ct);

        foreach (var p in payments)
        {
            p.PayoutStatus = PayoutStatus.OnHold;
            p.PayoutHoldReason = PayoutHoldReason.AccountUnderReview;
        }
    }

    /* ------------------------------------------------------------ QT-U-06 */

    /// <summary>docs/08 §5.5 — lifting it, with a reason of its own.</summary>
    [HttpPost("{id:int}/restore")]
    public async Task<ActionResult<AdminUserDto>> Restore(
        int id, [FromBody] RestoreRequest req, CancellationToken ct)
    {
        var v = await gate.AllowAsync(AdminAction.Restore, req.Reason, ct, id);
        if (!v.Ok) return Refuse(v);

        var user = await db.Users.Include(u => u.HostProfile).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        // docs/08 §5.5 — only the top role reopens a permanent ban.
        if (user.IsBanned && !v.Admin!.AdminScope.HasFlag(AdminScope.Super))
            return StatusCode(403, new { message = "Tài khoản bị khoá vĩnh viễn chỉ Quản trị tối cao mở được." });

        var now = DateTime.UtcNow;

        foreach (var s in await db.Sanctions.Where(s => s.UserId == id && s.LiftedAt == null).ToListAsync(ct))
        {
            s.LiftedAt = now;
            s.LiftedByUserId = v.Admin!.Id;
            s.LiftedReason = req.Reason!.Trim();
        }

        user.IsSuspended = false;
        user.IsBanned = false;
        user.SuspendedUntil = null;
        user.RestrictionMask = 0;
        user.MayStillRespondToDisputes = false;

        // §5.5 — what the sanction hid comes back; what the host had chosen to
        // keep offline stays offline.
        if (user.HostProfile is { } hostProfile)
        {
            foreach (var l in await db.Listings
                         .Where(l => l.HostId == hostProfile.Id && l.HiddenBySanctionAt != null)
                         .ToListAsync(ct))
            {
                l.IsPublished = true;
                l.HiddenBySanctionAt = null;
            }

            // Money held only because of the sanction starts moving again; holds
            // with a life of their own (disputes, chargebacks) are recomputed by
            // the payout sweep and stay put.
            foreach (var p in await db.Payments
                         .Where(p => p.PayoutStatus == PayoutStatus.OnHold
                                     && p.PayoutHoldReason == PayoutHoldReason.AccountUnderReview
                                     && p.Booking!.Listing!.HostId == hostProfile.Id)
                         .ToListAsync(ct))
            {
                p.PayoutStatus = PayoutStatus.Scheduled;
                p.PayoutHoldReason = PayoutHoldReason.None;
            }
        }

        audit.Record(v.Admin!, "user.restore", $"user:{id}", "đang bị xử lý", "bình thường", req.Reason);
        await db.SaveChangesAsync(ct);

        await notifications.QueueWithEmailAsync(user, NotificationKind.System,
            "Tài khoản Staylio đã được khôi phục",
            $"Các hạn chế trên tài khoản của bạn đã được gỡ. Lý do: {req.Reason!.Trim()}",
            "/", ct);

        return await Profile(id, null, ct);
    }

    /* -------------------------------------------- QT-U-09 and QT-U-10 */

    /// <summary>docs/08 §2 — force a password reset and end every session.</summary>
    [HttpPost("{id:int}/force-password-reset")]
    public async Task<ActionResult> ForcePasswordReset(
        int id, [FromBody] RestoreRequest req, CancellationToken ct)
    {
        var v = await gate.AllowAsync(AdminAction.ForcePasswordReset, req.Reason, ct, id);
        if (!v.Ok) return Refuse(v);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        foreach (var s in await db.AuthSessions.Where(s => s.UserId == id && s.RevokedAt == null).ToListAsync(ct))
            s.RevokedAt = DateTime.UtcNow;

        var token = await auth.BeginPasswordResetAsync(user.Email, ct);

        audit.Record(v.Admin!, "user.force-password-reset", $"user:{id}", null, "đã huỷ mọi phiên", req.Reason);
        await db.SaveChangesAsync(ct);

        await notifications.QueueWithEmailAsync(user, NotificationKind.System,
            "Cần đặt lại mật khẩu Staylio",
            $"Vì lý do an toàn, mọi phiên đăng nhập của bạn đã bị huỷ và bạn cần đặt lại mật khẩu. " +
            $"Lý do: {req.Reason!.Trim()}",
            $"/reset-password?token={token}", ct);

        // The link comes back to the admin as well, because on a server with no
        // mail configured the email above goes nowhere and the person is simply
        // locked out. This is the controlled route the public forgot-password
        // endpoint must not be: a named admin, a permission, a written reason
        // and an audit line. The admin still never learns the password — the
        // person sets their own at the other end of the link.
        return Ok(new
        {
            message = "Đã huỷ mọi phiên và tạo liên kết đặt lại mật khẩu.",
            resetLink = $"/reset-password?token={token}",
            note = "Gửi liên kết này cho người dùng. Liên kết sống 2 giờ và chỉ dùng được một lần."
        });
    }

    /* ------------------------------------------------------------ QT-U-11 */

    /// <summary>
    /// docs/08 §4 — "chỉ vai Kiểm duyệt và Tối cao, mỗi lần xem phải nhập lý do,
    /// mỗi lần xem ghi một dòng nhật ký riêng, ảnh có đóng dấu mờ tên admin đang
    /// xem."
    ///
    /// The watermark is the part that changes behaviour: a screenshot of somebody
    /// else's identity card that leaves this building carries the name of whoever
    /// was looking at it.
    /// </summary>
    [HttpPost("{id:int}/identity")]
    public async Task<ActionResult<IdentityViewDto>> ViewIdentity(
        int id, [FromBody] RestoreRequest req, CancellationToken ct)
    {
        var v = await gate.AllowAsync(AdminAction.ViewIdentityDocuments, req.Reason, ct);
        if (!v.Ok) return Refuse(v);

        var check = await db.IdentityChecks
            .Where(c => c.UserId == id)
            .OrderByDescending(c => c.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        if (check is null) return NotFound(new { message = "Người dùng chưa gửi giấy tờ nào." });

        // docs/08 §4 — its own line, on top of the ordinary audit row.
        gate.RecordSensitiveRead(v.Admin!, AdminAction.ViewIdentityDocuments, $"user:{id}", req.Reason!);
        await db.SaveChangesAsync(ct);

        var stamp = $"{v.Admin!.FullName} · {DateTime.UtcNow:HH:mm dd/MM/yyyy}";

        return Ok(new IdentityViewDto(
            IdentityChecks.DocumentLabel(check.Document),
            check.DocumentLast4,
            check.FrontImageUrl, check.BackImageUrl, check.SelfieImageUrl,
            stamp,
            check.Status.ToString(), check.SubmittedAt));
    }

    /* ------------------------------------------------ §2, the two ✓* reads */

    /// <summary>
    /// docs/08 §2 — "Xem nội dung tin nhắn của một đơn cụ thể." One booking, not
    /// an inbox: the row is scoped to the booking the admin names, so "xem tin
    /// nhắn" can never quietly become "đọc mọi cuộc trò chuyện của người này".
    /// POST, because a reason has to travel with it and each read is recorded.
    /// </summary>
    [HttpPost("bookings/{bookingId:int}/thread")]
    public async Task<ActionResult<AdminThreadDto>> ViewThread(
        int bookingId, [FromBody] RestoreRequest req, CancellationToken ct)
    {
        var booking = await db.Bookings
            .Include(b => b.Listing!).ThenInclude(l => l.Host)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null) return NotFound(new { message = "Không tìm thấy đơn này." });

        var v = await gate.AllowAsync(AdminAction.ViewBookingThread, req.Reason, ct, booking.GuestUserId);
        if (!v.Ok) return Refuse(v);

        var thread = await db.MessageThreads
            .Include(t => t.Messages).ThenInclude(m => m.SenderUser)
            .Include(t => t.GuestUser).Include(t => t.HostUser)
            .FirstOrDefaultAsync(t => t.BookingId == bookingId, ct);

        gate.RecordSensitiveRead(v.Admin!, AdminAction.ViewBookingThread, $"booking:{booking.Reference}", req.Reason!);
        await db.SaveChangesAsync(ct);

        return Ok(new AdminThreadDto(
            booking.Id, booking.Reference,
            booking.Listing?.Title ?? "",
            thread?.GuestUser?.FullName ?? booking.GuestName ?? "Khách",
            thread?.HostUser?.FullName ?? booking.Listing?.Host?.Name ?? "Chủ nhà",
            thread is null
                ? []
                : thread.Messages
                    .OrderBy(m => m.SentAt)
                    .Select(m => new AdminThreadMessageDto(
                        m.SenderUser?.FullName ?? "—", m.Body, m.SentAt, m.IsSystem))
                    .ToList()));
    }

    /// <summary>
    /// docs/08 §2 and §9 — "Sửa thông tin sai: người dùng tự sửa được phần lớn;
    /// phần đã xác minh cần admin duyệt." Only the fields a person might be stuck
    /// with; the email and the payout account are deliberately not here, because
    /// those decide who controls the account and where the money goes.
    /// </summary>
    [HttpPost("{id:int}/profile")]
    public async Task<ActionResult<AdminUserDto>> EditProfile(
        int id, [FromBody] AdminEditProfileRequest req, CancellationToken ct)
    {
        var v = await gate.AllowAsync(AdminAction.EditProfile, req.Reason, ct, id);
        if (!v.Ok) return Refuse(v);

        var user = await db.Users.Include(u => u.HostProfile).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        var before = $"{user.FullName} · {user.Phone ?? "—"}";

        if (!string.IsNullOrWhiteSpace(req.FullName)) user.FullName = req.FullName.Trim();
        if (req.DisplayName is not null) user.DisplayName = Profiles.Tidy(req.DisplayName, Profiles.LineMax);
        if (req.Location is not null) user.Location = Profiles.Tidy(req.Location, Profiles.LineMax);
        if (req.Occupation is not null) user.Occupation = Profiles.Tidy(req.Occupation, Profiles.LineMax);

        if (req.Phone is not null)
        {
            var phone = Identity.NormalisePhone(req.Phone);
            if (req.Phone.Trim().Length > 0 && phone is null)
                return BadRequest(new { message = "Số điện thoại không hợp lệ." });

            if (phone is not null && await db.Users.AnyAsync(u => u.Id != id && u.Phone == phone, ct))
                return BadRequest(new { message = "Số điện thoại này đã thuộc về tài khoản khác." });

            // A number an admin typed in has not been proved by anybody, so the
            // confirmed flag goes with it.
            user.Phone = phone;
            user.PhoneConfirmed = false;
        }

        var shown = Profiles.DisplayNameOf(user.DisplayName, user.FullName);
        user.Initials = Profiles.InitialsOf(shown);

        if (user.HostProfile is { } host)
        {
            host.Name = shown;
            host.Initials = user.Initials;
        }

        audit.Record(v.Admin!, "user.edit-profile", $"user:{id}",
            before, $"{user.FullName} · {user.Phone ?? "—"}", req.Reason);

        await db.SaveChangesAsync(ct);

        await notifications.QueueWithEmailAsync(user, NotificationKind.System,
            "Hồ sơ của bạn vừa được Staylio chỉnh sửa",
            $"Nhân viên hỗ trợ đã sửa thông tin hồ sơ của bạn. Lý do: {req.Reason!.Trim()}. " +
            "Nếu bạn thấy bất thường, hãy liên hệ ngay.",
            "/account", ct);

        return await Profile(id, null, ct);
    }

    /// <summary>docs/08 §2 — send them round the identity check again.</summary>
    [HttpPost("{id:int}/force-identity-recheck")]
    public async Task<ActionResult> ForceIdentityRecheck(
        int id, [FromBody] RestoreRequest req, CancellationToken ct)
    {
        var v = await gate.AllowAsync(AdminAction.ForceIdentityRecheck, req.Reason, ct, id);
        if (!v.Ok) return Refuse(v);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        user.IsIdentityVerified = false;

        audit.Record(v.Admin!, "user.force-identity-recheck", $"user:{id}", "đã xác minh", "cần xác minh lại", req.Reason);
        await db.SaveChangesAsync(ct);

        await notifications.QueueWithEmailAsync(user, NotificationKind.System,
            "Cần xác minh lại danh tính",
            $"Vui lòng gửi lại giấy tờ tuỳ thân để xác minh. Lý do: {req.Reason!.Trim()}",
            "/account/identity", ct);

        return Ok(new { message = "Đã yêu cầu xác minh lại danh tính." });
    }
}
