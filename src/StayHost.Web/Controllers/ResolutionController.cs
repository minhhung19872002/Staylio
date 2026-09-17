using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/01 AT-04 — the resolution centre. A guest or host opens a claim about
/// a booking, the other side has 24 hours to answer, and an admin decides if
/// they object. Acceptance scenario 10 of docs/04 runs entirely through here.
/// </summary>
[ApiController]
[Route("api/resolutions")]
public class ResolutionController(
    StayHostDbContext db, AuthService auth, NotificationService notifications, ThreadMessenger messenger)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResolutionCaseDto>>> Mine(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var cases = await VisibleTo(user)
            .Include(c => c.Booking!).ThenInclude(b => b.Listing)
            .Include(c => c.OpenedByUser)
            .Include(c => c.DecidedByUser)
            .Include(c => c.Events)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return Ok(cases.Select(c => ToDto(c, user)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ResolutionCaseDto>> Open(
        [FromBody] OpenResolutionRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var booking = await db.Bookings
            .Include(b => b.Listing!).ThenInclude(l => l.Host)
            .FirstOrDefaultAsync(b => b.Id == req.BookingId, ct);

        if (booking is null) return NotFound();

        var hostUserId = booking.Listing?.Host?.UserId;
        var isGuest = booking.GuestUserId == user.Id;
        var isHost = hostUserId == user.Id;
        if (!isGuest && !isHost) return this.Denied();

        // A claim is about a stay that happened, not one still being argued over.
        if (booking.Status is not (BookingStatus.Completed or BookingStatus.InProgress
            or BookingStatus.CancelledByGuest or BookingStatus.CancelledByHost))
        {
            return BadRequest(new { message = "Chỉ mở hồ sơ cho chuyến đi đang diễn ra hoặc đã kết thúc." });
        }

        if (await db.ResolutionCases.AnyAsync(c => c.BookingId == booking.Id
                && c.Status != ResolutionStatus.Resolved && c.Status != ResolutionStatus.Withdrawn, ct))
        {
            return Conflict(new { message = "Đơn này đã có hồ sơ đang mở." });
        }

        var description = (req.Description ?? "").Trim();
        if (description.Length < 20)
            return BadRequest(new { message = "Vui lòng mô tả sự việc, tối thiểu 20 ký tự." });

        var amount = Resolutions.Clamp(req.AmountClaimed, booking);
        if (amount <= 0)
            return BadRequest(new { message = "Số tiền yêu cầu phải lớn hơn 0." });

        var kase = new ResolutionCase
        {
            Reference = "HS" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            BookingId = booking.Id,
            OpenedByUserId = user.Id,
            OpenedByHost = isHost,
            Kind = Enum.TryParse<ResolutionKind>(req.Kind, true, out var kind) ? kind : ResolutionKind.Other,
            AmountClaimed = amount,
            Description = description,
            EvidenceUrls = string.Join('\n', (req.EvidenceUrls ?? []).Where(u => !string.IsNullOrWhiteSpace(u)).Take(10)),
            ResponseDueAt = DateTime.UtcNow + Resolutions.ResponseWindow
        };

        db.ResolutionCases.Add(kase);
        await db.SaveChangesAsync(ct);

        db.ResolutionEvents.Add(Resolutions.Opened(kase, ActorOf(user, isHost),
            $"Mở hồ sơ {Resolutions.KindLabel(kase.Kind)}, yêu cầu {amount:#,##0}₫."));

        var otherId = isHost ? booking.GuestUserId : hostUserId;
        var other = otherId is int id ? await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) : null;

        await notifications.QueueWithEmailAsync(other, NotificationKind.System,
            "Có yêu cầu bồi thường cần bạn trả lời",
            $"Hồ sơ {kase.Reference} về đơn {booking.Reference}: {Resolutions.KindLabel(kase.Kind)}, " +
            $"{amount:#,##0}₫. Bạn có 24 giờ để đồng ý hoặc phản đối.",
            "/resolutions", ct);

        await messenger.PostAsync(booking,
            $"Đã mở hồ sơ {kase.Reference}: {Resolutions.KindLabel(kase.Kind)}, yêu cầu {amount:#,##0}₫. " +
            "Bên còn lại có 24 giờ để trả lời.", ct);

        await db.SaveChangesAsync(ct);

        var fresh = await LoadAsync(kase.Id, ct);
        return Created($"/api/resolutions/{kase.Id}", ToDto(fresh!, user));
    }

    /// <summary>The other party either agrees or objects. Objecting sends it to an admin.</summary>
    [HttpPost("{id:int}/respond")]
    public async Task<ActionResult<ResolutionCaseDto>> Respond(
        int id, [FromBody] RespondResolutionRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var kase = await LoadAsync(id, ct);
        if (kase is null) return NotFound();

        var hostUserId = kase.Booking?.Listing?.Host?.UserId;
        var isRespondent = kase.OpenedByHost
            ? kase.Booking?.GuestUserId == user.Id
            : hostUserId == user.Id;

        if (!isRespondent) return this.Denied();
        if (kase.Status != ResolutionStatus.AwaitingResponse)
            return BadRequest(new { message = $"Hồ sơ đang ở trạng thái \"{Resolutions.Label(kase.Status)}\"." });

        var to = req.Accept ? ResolutionStatus.Accepted : ResolutionStatus.Disputed;
        kase.Response = (req.Note ?? "").Trim();
        kase.RespondedAt = DateTime.UtcNow;

        db.ResolutionEvents.Add(Resolutions.Transition(kase, to, ActorOf(user, !kase.OpenedByHost),
            req.Accept ? "Đồng ý với yêu cầu." : "Phản đối yêu cầu, chuyển quản trị phân xử."));

        var opener = await db.Users.FirstOrDefaultAsync(u => u.Id == kase.OpenedByUserId, ct);
        await notifications.QueueWithEmailAsync(opener, NotificationKind.System,
            req.Accept ? "Bên kia đã đồng ý" : "Bên kia đã phản đối",
            $"Hồ sơ {kase.Reference}: " + (req.Accept
                ? "Staylio sẽ chuyển tiền sau khi quản trị duyệt."
                : "Quản trị viên Staylio sẽ phân xử."),
            "/resolutions", ct);

        await db.SaveChangesAsync(ct);
        return Ok(ToDto((await LoadAsync(id, ct))!, user));
    }

    [HttpPost("{id:int}/withdraw")]
    public async Task<IActionResult> Withdraw(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var kase = await LoadAsync(id, ct);
        if (kase is null) return NotFound();
        if (kase.OpenedByUserId != user.Id) return this.Denied();
        if (!Resolutions.CanTransition(kase.Status, ResolutionStatus.Withdrawn))
            return BadRequest(new { message = "Hồ sơ này không rút lại được nữa." });

        db.ResolutionEvents.Add(Resolutions.Transition(
            kase, ResolutionStatus.Withdrawn, ActorOf(user, kase.OpenedByHost), "Người mở đã rút hồ sơ."));

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /* --------------------------------------------------------------- admin */

    [HttpGet("admin")]
    public async Task<ActionResult<IReadOnlyList<ResolutionCaseDto>>> AllCases(CancellationToken ct)
    {
        var admin = await RequireScopeAsync(AdminScope.Arbitration, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền phân xử." });

        var cases = await db.ResolutionCases
            .Include(c => c.Booking!).ThenInclude(b => b.Listing)
            .Include(c => c.OpenedByUser)
            .Include(c => c.DecidedByUser)
            .Include(c => c.Events)
            .OrderBy(c => c.Status)
            .ThenBy(c => c.CreatedAt)
            .ToListAsync(ct);

        return Ok(cases.Select(c => ToDto(c, admin)).ToList());
    }

    /// <summary>
    /// docs/01 QT-05 — the admin's ruling. The awarded amount moves between the
    /// two sides of the booking in the ledger, so the books still balance
    /// afterwards (docs/00 §6.1).
    /// </summary>
    [HttpPost("{id:int}/decide")]
    public async Task<ActionResult<ResolutionCaseDto>> Decide(
        int id, [FromBody] DecideResolutionRequest req, [FromServices] AdminGate gate, CancellationToken ct)
    {
        var admin = await RequireScopeAsync(AdminScope.Arbitration, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền phân xử." });

        var kase = await LoadAsync(id, ct);
        if (kase is null) return NotFound();

        if (await gate.PartyConflictAsync(
                admin, kase.Booking?.GuestUserId, kase.Booking?.Listing?.Host?.UserId, ct) is { } refusal)
            return StatusCode(403, new { message = refusal });
        if (!Resolutions.CanTransition(kase.Status, ResolutionStatus.Resolved))
            return BadRequest(new { message = $"Hồ sơ đang ở trạng thái \"{Resolutions.Label(kase.Status)}\"." });

        var booking = kase.Booking!;
        var awarded = Resolutions.Clamp(req.AmountAwarded, booking);
        var decision = (req.Decision ?? "").Trim();
        if (decision.Length < 10)
            return BadRequest(new { message = "Vui lòng ghi rõ lý do phân xử, tối thiểu 10 ký tự." });

        var before = $"{Resolutions.Label(kase.Status)}, yêu cầu {kase.AmountClaimed:#,##0}₫";

        kase.AmountAwarded = awarded;
        kase.Decision = decision;
        kase.DecidedByUserId = admin.Id;
        kase.DecidedAt = DateTime.UtcNow;

        db.ResolutionEvents.Add(Resolutions.Transition(kase, ResolutionStatus.Resolved,
            $"admin:{admin.Id}",
            kase.OpenedByHost
                ? $"Phân xử: khách phải đền chủ nhà {awarded:#,##0}₫. {decision}"
                : $"Phân xử: chuyển {awarded:#,##0}₫. {decision}"));

        /*
         * docs/06 §3.3, chốt 17/08/2026 — a host's damage claim is settled
         * between the two of them, in cash. The platform rules on it and does
         * not move the money.
         *
         * It used to post SettleClaim(toHost:), which debits GuestFunds — the
         * pooled account of money Staylio is holding for guests. By the time a
         * damage case is decided the guest's own money has long since gone to
         * the host as payout, so that debit was against other guests' balances
         * and nothing ever collected it back. A ruling nobody can enforce is
         * still a ruling; a ledger entry against somebody else's money is a
         * mistake.
         *
         * The other direction is unchanged and is not the same case: when a host
         * owes a guest, the platform genuinely holds that host's money.
         */
        if (awarded > 0 && !kase.OpenedByHost)
            db.LedgerEntries.AddRange(
                Ledger.SettleClaim(booking, toGuest: awarded, toHost: 0m, DateTime.UtcNow));

        db.AdminAudit.Add(new AdminAuditEntry
        {
            ActorUserId = admin.Id,
            Action = "case.decide",
            Target = $"case:{kase.Id}",
            Before = before,
            After = $"Đã phân xử, chuyển {awarded:#,##0}₫",
            Note = decision
        });

        await db.SaveChangesAsync(ct);

        // Saying "chuyển" for a host's damage claim would promise a transfer that
        // is not going to happen: the two of them settle it (docs/06 §3.3). What
        // the platform decided is a number, and both sides are told it plainly.
        var ruling = kase.OpenedByHost
            ? $"khách phải đền chủ nhà {awarded:#,##0}₫, hai bên tự thanh toán với nhau"
            : $"chuyển {awarded:#,##0}₫";

        var opener = await db.Users.FirstOrDefaultAsync(u => u.Id == kase.OpenedByUserId, ct);
        await notifications.QueueWithEmailAsync(opener, NotificationKind.System,
            "Hồ sơ đã được phân xử",
            $"Hồ sơ {kase.Reference}: Staylio quyết định {ruling}. {decision}",
            "/resolutions", ct);

        await messenger.PostAsync(booking,
            $"Hồ sơ {kase.Reference} đã được Staylio phân xử: {ruling}. {decision}", ct);

        await db.SaveChangesAsync(ct);
        return Ok(ToDto((await LoadAsync(id, ct))!, admin));
    }

    /* ------------------------------------------------------------- helpers */

    private IQueryable<ResolutionCase> VisibleTo(User user) =>
        db.ResolutionCases.Where(c =>
            c.OpenedByUserId == user.Id ||
            c.Booking!.GuestUserId == user.Id ||
            c.Booking.Listing!.Host!.UserId == user.Id);

    private Task<ResolutionCase?> LoadAsync(int id, CancellationToken ct) =>
        db.ResolutionCases
            .Include(c => c.Booking!).ThenInclude(b => b.Listing!).ThenInclude(l => l.Host)
            .Include(c => c.OpenedByUser)
            .Include(c => c.DecidedByUser)
            .Include(c => c.Events)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    private async Task<User?> RequireScopeAsync(AdminScope scope, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user?.Role != UserRole.Admin) return null;
        return user.AdminScope.HasFlag(scope) || user.AdminScope.HasFlag(AdminScope.Super) ? user : null;
    }

    private static string ActorOf(User user, bool asHost) => $"{(asHost ? "host" : "guest")}:{user.Id}";

    private static ResolutionCaseDto ToDto(ResolutionCase c, User viewer) => new(
        c.Id,
        c.Reference,
        c.BookingId,
        c.Booking?.Reference ?? "",
        c.Booking?.Listing?.Title ?? "",
        c.Kind.ToString(),
        Resolutions.KindLabel(c.Kind),
        c.Status.ToString(),
        Resolutions.Label(c.Status),
        Resolutions.BadgeClass(c.Status),
        c.AmountClaimed,
        c.AmountAwarded,
        c.Description,
        c.EvidenceUrls.Split('\n', StringSplitOptions.RemoveEmptyEntries),
        c.OpenedByUser?.FullName ?? "",
        c.OpenedByHost,
        c.ResponseDueAt,
        c.Response,
        c.RespondedAt,
        c.Decision,
        c.DecidedByUser?.FullName,
        c.DecidedAt,
        // Whoever did not open it is the one who owes an answer.
        c.Status == ResolutionStatus.AwaitingResponse && c.OpenedByUserId != viewer.Id,
        c.OpenedByUserId == viewer.Id && Resolutions.CanTransition(c.Status, ResolutionStatus.Withdrawn),
        c.Events.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new ResolutionEventDto(
                e.FromStatus is null ? "" : Resolutions.Label(e.FromStatus.Value),
                Resolutions.Label(e.ToStatus), e.Actor, e.Note, e.CreatedAt))
            .ToList(),
        c.CreatedAt);
}
