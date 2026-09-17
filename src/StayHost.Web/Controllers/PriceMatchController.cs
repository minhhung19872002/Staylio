using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/01 MR-10 — find the same room cheaper somewhere else within a day of
/// booking and the difference comes back as balance. Balance, not cash: the
/// guarantee is meant to keep the guest, not to refund them out of the stay.
/// </summary>
[ApiController]
[Route("api")]
public class PriceMatchController(
    StayHostDbContext db, AuthService auth, AdminAudit audit,
    NotificationService notifications, WalletService wallet)
    : ControllerBase
{
    [HttpPost("bookings/{id:int}/price-match")]
    public async Task<ActionResult<PriceMatchDto>> Submit(
        int id, [FromBody] SubmitPriceMatchRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var booking = await db.Bookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == id && b.GuestUserId == user.Id, ct);
        if (booking is null) return NotFound();

        if (booking.Listing?.IsHotel != true)
            return BadRequest(new { message = "Cam kết giá tốt chỉ áp dụng cho phòng khách sạn." });

        if (!HotelRules.WithinWindow(booking.CreatedAt, DateTime.UtcNow))
            return BadRequest(new { message = "Chỉ gửi được trong 24 giờ kể từ khi đặt." });

        if (await db.PriceMatchClaims.AnyAsync(c => c.BookingId == id, ct))
            return BadRequest(new { message = "Đơn này đã có một yêu cầu so giá." });

        var url = (req.CompetitorUrl ?? "").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return BadRequest(new { message = "Cần đường dẫn tới nơi bạn thấy giá rẻ hơn." });

        var ourNightly = booking.Nights > 0
            ? Math.Round(booking.RoomBeforeDiscount / booking.Nights, 0, MidpointRounding.AwayFromZero)
            : booking.RoomBeforeDiscount;

        var difference = HotelRules.MatchValue(ourNightly, req.CompetitorNightlyRate, booking.Nights);
        if (difference <= 0)
            return BadRequest(new
            {
                message = $"Giá bên kia ({req.CompetitorNightlyRate:#,##0}₫/đêm) không thấp hơn " +
                          $"giá bạn đã trả ({ourNightly:#,##0}₫/đêm) đủ để bù chênh lệch."
            });

        var claim = new PriceMatchClaim
        {
            BookingId = booking.Id,
            GuestUserId = user.Id,
            CompetitorUrl = url,
            CompetitorNightlyRate = req.CompetitorNightlyRate,
            OurNightlyRate = ourNightly,
            Difference = difference
        };

        db.PriceMatchClaims.Add(claim);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(claim, booking.Reference));
    }

    [HttpGet("bookings/{id:int}/price-match")]
    public async Task<ActionResult<PriceMatchDto>> Mine(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var claim = await db.PriceMatchClaims
            .Include(c => c.Booking)
            .FirstOrDefaultAsync(c => c.BookingId == id && c.GuestUserId == user.Id, ct);

        return claim is null ? NotFound() : Ok(ToDto(claim, claim.Booking!.Reference));
    }

    /* ------------------------------------------------------------ admin */

    [HttpGet("admin/price-matches")]
    public async Task<ActionResult<IReadOnlyList<PriceMatchDto>>> Pending(CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Arbitration, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền phân xử." });

        return Ok(await db.PriceMatchClaims
            .OrderBy(c => c.Status).ThenByDescending(c => c.CreatedAt)
            .Take(100)
            .Select(c => new PriceMatchDto(
                c.Id, c.BookingId, c.Booking!.Reference, c.CompetitorUrl,
                c.CompetitorNightlyRate, c.OurNightlyRate, c.Difference,
                c.Status.ToString(), HotelRules.StatusLabel(c.Status), c.Decision, c.CreatedAt))
            .ToListAsync(ct));
    }

    /// <summary>
    /// Approving one hands the guest promotional balance. It comes out of the
    /// platform's own pocket — the host sold the room at the price they set.
    /// </summary>
    [HttpPost("admin/price-matches/{id:int}/{decision}")]
    public async Task<IActionResult> Decide(
        int id, string decision, [FromBody] ResolveRiskFlagRequest? req, [FromServices] AdminGate gate,
        CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Arbitration, ct);
        if (admin is null) return StatusCode(403, new { message = "Bạn không có quyền phân xử." });

        var claim = await db.PriceMatchClaims
            .Include(c => c.Booking!).ThenInclude(b => b.Listing!).ThenInclude(l => l.Host)
            .Include(c => c.GuestUser)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null) return NotFound();

        if (await gate.PartyConflictAsync(
                admin, claim.GuestUserId, claim.Booking?.Listing?.Host?.UserId, ct) is { } refusal)
            return StatusCode(403, new { message = refusal });
        if (claim.Status != PriceMatchStatus.Submitted)
            return BadRequest(new { message = "Yêu cầu này đã được xử lý." });

        var approve = decision.Equals("approve", StringComparison.OrdinalIgnoreCase);

        audit.Record(admin, "price-match.decide", $"price-match:{claim.Id}",
            claim.Status.ToString(), approve ? "Approved" : "Rejected", req?.Resolution);

        claim.Status = approve ? PriceMatchStatus.Approved : PriceMatchStatus.Rejected;
        claim.Decision = req?.Resolution?.Trim();
        claim.DecidedAt = DateTime.UtcNow;

        if (approve)
        {
            var booking = claim.Booking!;
            booking.GoodwillCredit += claim.Difference;

            // The books said the guest had been given balance while the wallet
            // said nothing: `GoodwillCredit` is a note on the booking and the
            // ledger row moves money between platform accounts, but what a guest
            // can actually spend is the run of CreditEntries. Without this line
            // the notification below promised money that was never there — and
            // docs/07 §16 stamps the twelve-month lifetime here, at the grant.
            wallet.Add(claim.GuestUserId, claim.Difference, CreditReason.Goodwill,
                $"Cam kết giá tốt · đơn {booking.Reference}", booking.Id);

            db.LedgerEntries.AddRange(Ledger.GrantCredit(
                booking, claim.Difference, "Bù chênh lệch cam kết giá tốt", DateTime.UtcNow));
        }

        await notifications.QueueWithEmailAsync(
            claim.GuestUser, NotificationKind.System,
            approve ? "Đã bù chênh lệch giá" : "Yêu cầu so giá không được chấp nhận",
            approve
                ? $"{claim.Difference:#,##0}₫ đã được cộng vào số dư của bạn."
                : claim.Decision ?? "Chúng tôi không xác minh được mức giá bạn gửi.",
            $"/trips/{claim.BookingId}", ct);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static PriceMatchDto ToDto(PriceMatchClaim c, string reference) => new(
        c.Id, c.BookingId, reference, c.CompetitorUrl,
        c.CompetitorNightlyRate, c.OurNightlyRate, c.Difference,
        c.Status.ToString(), HotelRules.StatusLabel(c.Status), c.Decision, c.CreatedAt);
}
