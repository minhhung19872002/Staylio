using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/01 ĐP-07 — one booking paid by up to sixteen people. The organiser sets
/// it up from a held booking; everyone else follows a link, sees what they owe
/// and pays only that. The booking is confirmed when the last share lands, and
/// let go — with everything paid so far sent back — if the day runs out first.
/// </summary>
[ApiController]
public class SplitBillController(
    StayHostDbContext db,
    AuthService auth,
    PaymentGateway gateway,
    SplitBillService splits,
    BankTransferSettings bank,
    Services.Gateways.PspRouter psp,
    Services.Gateways.PspCheckout pspCheckout) : ControllerBase
{
    /* ------------------------------------------------------ the organiser */

    [HttpPost("api/bookings/{id:int}/split")]
    public async Task<ActionResult<BillSplitDto>> Open(
        int id, [FromBody] OpenSplitRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var booking = await db.Bookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == id && b.GuestUserId == user.Id, ct);
        if (booking is null) return NotFound();

        if (booking.Status != BookingStatus.PendingPayment)
            return BadRequest(new { message = "Chỉ chia hoá đơn khi đơn còn đang chờ thanh toán." });

        if (await db.BillSplits.AnyAsync(s => s.BookingId == id, ct))
            return BadRequest(new { message = "Đơn này đã có một lượt chia hoá đơn." });

        var emails = (req.Emails ?? [])
            .Select(e => (e ?? "").Trim().ToLowerInvariant())
            .Where(e => e.Length > 0 && e.Contains('@'))
            .Where(e => !string.Equals(e, user.Email, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        if (emails.Count == 0)
            return BadRequest(new { message = "Thêm ít nhất một người cùng trả." });
        if (emails.Count + 1 > BillSplitRules.MaxPeople)
            return BadRequest(new { message = $"Tối đa {BillSplitRules.MaxPeople} người cho một hoá đơn." });

        // The booking would otherwise lapse in fifteen minutes; a split needs a
        // day, so the hold is extended for exactly as long as the split lives.
        var split = new BillSplit
        {
            BookingId = booking.Id,
            OrganiserUserId = user.Id,
            Total = booking.Total,
            ExpiresAt = DateTime.UtcNow + BillSplitRules.Window
        };

        var amounts = BillSplitRules.Divide(booking.Total, emails.Count + 1);

        split.Shares.Add(new BillShare { Email = user.Email, Name = user.FullName, Amount = amounts[0] });
        for (var i = 0; i < emails.Count; i++)
            split.Shares.Add(new BillShare { Email = emails[i], Amount = amounts[i + 1] });

        db.BillSplits.Add(split);
        booking.HoldExpiresAt = split.ExpiresAt;
        await db.SaveChangesAsync(ct);

        await splits.InviteAsync(split, booking, ct);

        return Ok(await DtoAsync(split.Id, ct));
    }

    [HttpGet("api/bookings/{id:int}/split")]
    public async Task<ActionResult<BillSplitDto>> Get(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var split = await db.BillSplits
            .FirstOrDefaultAsync(s => s.BookingId == id && s.OrganiserUserId == user.Id, ct);

        return split is null ? NotFound() : Ok(await DtoAsync(split.Id, ct));
    }

    [HttpDelete("api/bookings/{id:int}/split")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var split = await db.BillSplits
            .Include(s => s.Shares)
            .Include(s => s.Booking)
            .FirstOrDefaultAsync(s => s.BookingId == id && s.OrganiserUserId == user.Id, ct);
        if (split is null) return NotFound();
        if (!BillSplitRules.IsOpen(split.Status)) return NoContent();

        await splits.UnwindAsync(split, BillSplitStatus.Cancelled, "Người tổ chức đã huỷ chia hoá đơn.", ct);
        return NoContent();
    }

    /* ------------------------------------------------- everybody else */

    /// <summary>
    /// What the link opens. No account needed: the token is the credential, and
    /// it only ever exposes one share of one booking.
    /// </summary>
    [HttpGet("api/split/{token}")]
    public async Task<ActionResult<ShareInviteDto>> Invite(string token, CancellationToken ct)
    {
        var dto = await InviteDtoAsync(token, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    private async Task<ShareInviteDto?> InviteDtoAsync(string token, CancellationToken ct)
    {
        var share = await db.BillShares
            .Include(s => s.Split!).ThenInclude(x => x.Booking!).ThenInclude(b => b!.Listing)
            .FirstOrDefaultAsync(s => s.Token == token, ct);
        if (share is null) return null;

        var split = share.Split!;
        var booking = split.Booking!;

        var paid = await db.BillShares.CountAsync(s => s.SplitId == split.Id && s.Status == BillShareStatus.Paid, ct);
        var people = await db.BillShares.CountAsync(s => s.SplitId == split.Id, ct);

        return new ShareInviteDto(
            share.Token,
            booking.Reference,
            booking.Listing?.Title ?? "",
            booking.Listing?.City ?? "",
            booking.CheckIn,
            booking.CheckOut,
            booking.Nights,
            booking.Guests,
            split.Total,
            share.Amount,
            share.Status.ToString(),
            BillSplitRules.ShareLabel(share.Status),
            split.Status.ToString(),
            BillSplitRules.Label(split.Status),
            paid, people,
            split.ExpiresAt);
    }

    [HttpPost("api/split/{token}/pay")]
    public async Task<ActionResult<ShareInviteDto>> Pay(
        string token, [FromBody] PayShareRequest? req, CancellationToken ct)
    {
        var share = await db.BillShares
            .Include(s => s.Split!).ThenInclude(x => x.Shares)
            .FirstOrDefaultAsync(s => s.Token == token, ct);
        if (share is null) return NotFound();

        var split = share.Split!;
        if (!BillSplitRules.IsOpen(split.Status))
            return BadRequest(new { message = $"Lượt chia hoá đơn này {BillSplitRules.Label(split.Status).ToLower()}." });
        if (share.Status == BillShareStatus.Paid)
            return BadRequest(new { message = "Phần này đã được trả rồi." });
        if (BillSplitRules.Expired(split.ExpiresAt, DateTime.UtcNow))
            return BadRequest(new { message = "Đã quá 24 giờ, lượt chia hoá đơn này hết hạn." });

        // docs/07 §2 — a share is paid by a card or a wallet, like the stay it is
        // part of. It used to be charged as "card" by the stand-in whatever the
        // site had switched on, so a whole stay could be confirmed by typing
        // 4242 once per share.
        var (road, refusal) = ProductCheckout.Choose(psp, bank, req?.PaymentMethod);
        if (refusal is not null || road == ProductCheckout.Road.Transfer)
            return BadRequest(new { message = refusal ?? Payments.Message(DeclineReason.MethodUnavailable) });
        var method = ProductCheckout.Normalise(req?.PaymentMethod);

        if (road == ProductCheckout.Road.Gateway)
        {
            var booking = await db.Bookings.FirstAsync(b => b.Id == split.BookingId, ct);
            if (!string.IsNullOrWhiteSpace(req?.Name)) share.Name = req.Name.Trim();

            var started = await pspCheckout.StartForAsync(
                new PaymentSession
                {
                    BookingId = booking.Id, BillShareId = share.Id, Method = method,
                    Amount = share.Amount, AttemptKey = $"share-{share.Id}"
                },
                share.Id, $"Staylio {booking.Reference}", null,
                Psp.ClientIp(HttpContext.Connection.RemoteIpAddress?.ToString()), ct);

            if (!started.Ok || started.PayUrl is null)
                return BadRequest(new { message = started.Error, retryable = true });

            var pending = await InviteDtoAsync(token, ct);
            return pending is null ? NotFound() : Ok(pending with { GatewayRedirectUrl = started.PayUrl, GatewayOrderRef = started.OrderRef });
        }

        var attempt = gateway.Charge(share.Amount, method, req?.CardLast4);
        if (!attempt.Ok) return BadRequest(new { message = attempt.Reason });

        await splits.SharePaidAsync(share.Id, share.Amount, ct, req?.Name, req?.CardLast4);

        var dto = await InviteDtoAsync(token, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    private async Task<BillSplitDto> DtoAsync(int splitId, CancellationToken ct)
    {
        var split = await db.BillSplits
            .Include(s => s.Shares)
            .Include(s => s.Booking)
            .FirstAsync(s => s.Id == splitId, ct);

        return new BillSplitDto(
            split.Id,
            split.BookingId,
            split.Booking!.Reference,
            split.Total,
            split.Status.ToString(),
            BillSplitRules.Label(split.Status),
            split.ExpiresAt,
            split.Shares.OrderBy(s => s.Id).Select(s => new BillShareDto(
                s.Id, s.Email, s.Name, s.Amount,
                s.Status.ToString(), BillSplitRules.ShareLabel(s.Status),
                $"{Request.Scheme}://{Request.Host}/split/{s.Token}",
                s.PaidAt)).ToList());
    }
}
