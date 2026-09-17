using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/07 §2 and §4 — what a guest may pay with, and the cards they have kept.
///
/// The full number never reaches this class: the client sends it once so the
/// brand and last four can be read off it, and what is stored is what §4 allows
/// on a screen. There is no endpoint that returns a card number because there is
/// no card number to return.
/// </summary>
[ApiController]
[Route("api/payment-methods")]
public class PaymentMethodsController(
    StayHostDbContext db, AuthService auth, NotificationService notifications,
    BankTransferSettings bank, Services.Gateways.PspRouter psp,
    DataSecrets secrets, ILogger<PaymentMethodsController> log) : ControllerBase
{
    /// <summary>docs/07 §2 — the catalogue, so the payment page and this screen agree.</summary>
    /// <param name="listingId">
    /// docs/07 §2.5 — pay-at-property belongs to a listing, not to the platform:
    /// it is offered only where that host turned it on. Left out, the catalogue
    /// answers for the platform in general and simply does not include it.
    /// </param>
    [HttpGet("catalogue")]
    public async Task<ActionResult<PaymentCatalogueDto>> Catalogue(
        [FromQuery] int? listingId, CancellationToken ct)
    {
        // A row nobody can take money through is not offered: in production a
        // method whose gateway keys are missing disappears instead of failing at
        // the last step. The balance row is not a way to pay on its own and the
        // checkout already reads it separately.
        var offered = PaymentMethods.Available()
            .Where(m => m.Key == "balance" || psp.CanTake(m.Key))
            .Select(m => new PaymentMethodDto(m.Key, m.Group, m.Label, m.Hint, m.Savable,
                psp.IsLive(m.Key), psp.KeepsCards(m.Key), psp.ProviderOf(m.Key)))
            .ToList();

        // docs/07 §2.3 — VietQR is a "later" method in the catalogue, so it is not
        // in the required set above. It joins the list once there is an account
        // for it to credit, and never before: an option that leads to a QR code
        // crediting nobody is worse than one that is missing.
        if (bank.Enabled && PaymentMethods.Find("vietqr") is { } qr)
            offered.Add(new PaymentMethodDto(qr.Key, qr.Group, qr.Label, qr.Hint, qr.Savable));

        // docs/07 §2.5 — the same shape as VietQR above: in the catalogue, but
        // only offered where it can actually be used. Asked of the listing rather
        // than assumed, so a host who never turned it on never sees it appear on
        // their own place.
        if (listingId is { } id
            && await db.Listings.AnyAsync(l => l.Id == id && l.AcceptsPayAtProperty, ct)
            && PaymentMethods.Find(PayAtProperty.Key) is { } atProperty)
        {
            offered.Add(new PaymentMethodDto(
                atProperty.Key, atProperty.Group, atProperty.Label, atProperty.Hint, atProperty.Savable));
        }

        return Ok(new PaymentCatalogueDto(offered, PaymentMethods.RefusedLabels, PaymentMethods.RefusalReason()));
    }

    /// <summary>
    /// docs/07 §2.3 — the QR for one amount and one reference.
    ///
    /// The reference is the whole mechanism: it is what turns an anonymous credit
    /// on a bank statement back into the booking that was waiting for it, and
    /// putting it inside the QR is what stops the guest mistyping it. Anything
    /// the guest could have chosen — how much, what memo — is rejected here and
    /// taken from the booking instead; a QR built from what the browser asked for
    /// is a QR the guest wrote themselves.
    /// </summary>
    [HttpGet("vietqr/{reference}")]
    public async Task<ActionResult<BankTransferQrDto>> Qr(string reference, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        if (!bank.Enabled)
            return BadRequest(new { message = "Chưa cấu hình tài khoản nhận chuyển khoản." });

        var owed = await OwedOnAsync(user.Id, reference, ct);
        if (owed is null) return NotFound();
        if (owed.Value.Amount <= 0) return BadRequest(new { message = "Đơn này không còn khoản nào phải trả." });

        return Ok(new BankTransferQrDto(
            VietQr.Payload(bank.Beneficiary, owed.Value.Amount, reference),
            bank.BankName,
            bank.AccountNumber,
            bank.AccountName,
            VietQr.Sanitise(reference),
            owed.Value.Amount,
            owed.Value.ExpiresAt));
    }

    /// <summary>
    /// docs/07 §2.3 — whether the money has been found yet.
    ///
    /// The guest is on a page with a QR code and no way of knowing what happened
    /// after they pressed send in their banking app, so the page asks. It reads
    /// the booking's own status rather than anything about the transfer: what a
    /// guest wants to know is whether they have a booking.
    /// </summary>
    [HttpGet("vietqr/{reference}/status")]
    public async Task<ActionResult<BankTransferStatusDto>> QrStatus(string reference, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        if (await db.Bookings.FirstOrDefaultAsync(
                b => b.Reference == reference && b.GuestUserId == user.Id, ct) is { } stay)
            return Ok(new BankTransferStatusDto(
                stay.Status.ToString(), BookingLifecycle.Label(stay.Status),
                stay.Status == BookingStatus.Confirmed,
                stay.Status == BookingStatus.PendingPayment,
                stay.HoldExpiresAt, $"/trips/{stay.Id}"));

        if (await db.ExperienceBookings.FirstOrDefaultAsync(
                b => b.Reference == reference && b.GuestUserId == user.Id, ct) is { } ticket)
            return Ok(new BankTransferStatusDto(
                ticket.Status.ToString(), ExperienceRules.StatusLabel(ticket.Status),
                ticket.Status == ExperienceBookingStatus.Confirmed,
                ticket.Status == ExperienceBookingStatus.AwaitingPayment,
                ticket.CreatedAt + BankTransfers.Window, "/experiences/bookings"));

        if (await db.ServiceBookings.FirstOrDefaultAsync(
                b => b.Reference == reference && b.GuestUserId == user.Id, ct) is { } job)
            return Ok(new BankTransferStatusDto(
                job.Status.ToString(), ServiceRules.StatusLabel(job.Status),
                job.Status == ServiceBookingStatus.Confirmed,
                job.Status == ServiceBookingStatus.AwaitingPayment,
                job.CreatedAt + BankTransfers.Window, "/services/bookings"));

        return NotFound();
    }

    /// <summary>
    /// What this guest still owes on that reference, across the three things they
    /// can book. Null when the reference is not theirs — which reads the same as
    /// "no such booking", so nobody can probe for other people's references.
    /// </summary>
    private async Task<(decimal Amount, DateTime? ExpiresAt)?> OwedOnAsync(
        int userId, string reference, CancellationToken ct)
    {
        if (await db.Bookings.FirstOrDefaultAsync(
                b => b.Reference == reference && b.GuestUserId == userId, ct) is { } stay)
        {
            // A stay that has been paid owes nothing, and must not be handed a QR
            // for its own total a second time. Only two things are outstanding: a
            // booking still waiting to be paid at all, and the balance of one paid
            // by deposit.
            if (stay.BalanceDue > 0) return (stay.BalanceDue, null);
            return stay.Status == BookingStatus.PendingPayment
                ? (stay.Total, stay.HoldExpiresAt)
                : (0m, null);
        }

        if (await db.ServiceBookings.FirstOrDefaultAsync(
                b => b.Reference == reference && b.GuestUserId == userId, ct) is { } job)
            return job.Status == ServiceBookingStatus.AwaitingPayment
                ? (job.Total, job.CreatedAt + BankTransfers.Window)
                : (0m, null);

        if (await db.ExperienceBookings.FirstOrDefaultAsync(
                b => b.Reference == reference && b.GuestUserId == userId, ct) is { } ticket)
            return ticket.Status == ExperienceBookingStatus.AwaitingPayment
                ? (ticket.Total, ticket.CreatedAt + BankTransfers.Window)
                : (0m, null);

        return null;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavedCardDto>>> Mine(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        return Ok(await CardsOf(user.Id, ct));
    }

    [HttpPost]
    public async Task<ActionResult<IReadOnlyList<SavedCardDto>>> Add(
        [FromBody] SaveCardRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        if (!SavedCards.IsPlausibleNumber(req.Number))
            return BadRequest(new { message = "Số thẻ không đúng. Hãy kiểm tra lại." });

        var brand = SavedCards.BrandOf(req.Number);
        if (brand == CardBrand.Unknown)
            return BadRequest(new { message = "Staylio chưa nhận loại thẻ này." });

        if (req.ExpiryMonth is < 1 or > 12 || req.ExpiryYear < 2000)
            return BadRequest(new { message = "Tháng/năm hết hạn không hợp lệ." });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (SavedCards.ExpiresAfter(req.ExpiryMonth, req.ExpiryYear) < today)
            return BadRequest(new { message = "Thẻ này đã hết hạn." });

        var last4 = SavedCards.Last4Of(req.Number);

        var already = await db.SavedCards.AnyAsync(
            c => c.UserId == user.Id && c.Last4 == last4 && c.Brand == brand
                 && c.ExpiryMonth == req.ExpiryMonth && c.ExpiryYear == req.ExpiryYear, ct);

        if (already) return BadRequest(new { message = "Thẻ này đã được lưu rồi." });

        var card = new SavedCard
        {
            UserId = user.Id,
            Brand = brand,
            Last4 = last4,
            ExpiryMonth = req.ExpiryMonth,
            ExpiryYear = req.ExpiryYear,
            Nickname = string.IsNullOrWhiteSpace(req.Nickname) ? null : req.Nickname.Trim()
        };

        db.SavedCards.Add(card);
        await db.SaveChangesAsync(ct);

        // The first card saved is the default; a later one only takes over when
        // the guest asks for it.
        var cards = await db.SavedCards.Where(c => c.UserId == user.Id).ToListAsync(ct);
        SavedCards.Reseat(cards, req.MakeDefault ? card.Id : null);
        await db.SaveChangesAsync(ct);

        return Ok(await CardsOf(user.Id, ct));
    }

    [HttpPut("{id:int}/default")]
    public async Task<ActionResult<IReadOnlyList<SavedCardDto>>> MakeDefault(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var cards = await db.SavedCards.Where(c => c.UserId == user.Id).ToListAsync(ct);
        if (cards.All(c => c.Id != id)) return NotFound(new { message = "Không tìm thấy thẻ." });

        SavedCards.Reseat(cards, id);
        await db.SaveChangesAsync(ct);

        return Ok(await CardsOf(user.Id, ct));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<IReadOnlyList<SavedCardDto>>> Rename(
        int id, [FromBody] RenameCardRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var card = await db.SavedCards.FirstOrDefaultAsync(c => c.Id == id && c.UserId == user.Id, ct);
        if (card is null) return NotFound(new { message = "Không tìm thấy thẻ." });

        card.Nickname = string.IsNullOrWhiteSpace(req.Nickname) ? null : req.Nickname.Trim();
        await db.SaveChangesAsync(ct);

        return Ok(await CardsOf(user.Id, ct));
    }

    /// <summary>
    /// docs/07 §4 — removing a card is blocked while it is doing something. The
    /// guest is told which, and what to do first.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult<IReadOnlyList<SavedCardDto>>> Remove(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var card = await db.SavedCards.FirstOrDefaultAsync(c => c.Id == id && c.UserId == user.Id, ct);
        if (card is null) return NotFound(new { message = "Không tìm thấy thẻ." });

        var block = SavedCards.CanRemove(
            await HasScheduledChargeAsync(user.Id, card.Last4, ct),
            await HasOpenBookingAsync(user.Id, card.Last4, ct));

        if (block != SavedCards.RemovalBlock.None)
            return BadRequest(new { message = SavedCards.RemovalMessage(block) });

        // docs/07 §15.5 — the gateway is holding this card too, and deleting the
        // row here would leave it holding it for ever. Told first, while the
        // sealed token is still readable; the row goes whatever they answer,
        // because a guest who asked to remove a card must not be refused
        // because somebody else's server is down.
        await ForgetAtGatewayAsync(card, user.Id, ct);

        db.SavedCards.Remove(card);
        await db.SaveChangesAsync(ct);

        // Removing the default hands the title on rather than leaving none.
        var rest = await db.SavedCards.Where(c => c.UserId == user.Id).ToListAsync(ct);
        SavedCards.Reseat(rest);
        await db.SaveChangesAsync(ct);

        return Ok(await CardsOf(user.Id, ct));
    }

    /* ------------------------------------------------------------- helpers */

    /// <summary>
    /// docs/07 §15.5 — asks the gateway to stop holding a card the guest just
    /// deleted here. Never throws and never blocks the deletion.
    /// </summary>
    private async Task ForgetAtGatewayAsync(SavedCard card, int userId, CancellationToken ct)
    {
        if (card.GatewayTokenSealed is not { Length: > 0 }) return;

        var provider = psp.ByKey(card.Provider);
        if (provider is null) return;

        var token = secrets.Open(DataSecrets.CardToken, card.GatewayTokenSealed);
        if (token is null)
        {
            // The key changed or was never set, so the token cannot be read back
            // — which means it can never be removed either. Worth saying once
            // rather than deleting the row and quietly forgetting it existed.
            log.LogWarning(
                "Không mở được token của thẻ {Card}, nên {Provider} vẫn giữ thẻ này.",
                card.Id, card.Provider);
            return;
        }

        // The same name the token was created under. Psp.AppUserRef owns the
        // format precisely so these two cannot drift.
        await provider.ForgetCardAsync(token, Psp.AppUserRef(userId), ct);
    }

    private async Task<IReadOnlyList<SavedCardDto>> CardsOf(int userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var cards = await db.SavedCards
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.IsDefault).ThenByDescending(c => c.AddedAt)
            .ToListAsync(ct);

        var result = new List<SavedCardDto>(cards.Count);

        foreach (var card in cards)
        {
            var scheduled = await HasScheduledChargeAsync(userId, card.Last4, ct);

            result.Add(new SavedCardDto(
                card.Id,
                card.Brand.ToString(),
                SavedCards.BrandLabel(card.Brand),
                card.Last4,
                SavedCards.ExpiryLabel(card),
                card.Nickname,
                card.IsDefault,
                SavedCards.IsExpired(card, today),
                SavedCards.ExpiringSoon(card, today),
                scheduled,
                await HasOpenBookingAsync(userId, card.Last4, ct),
                card.IsGatewayHeld,
                card.Provider));
        }

        return result;
    }

    /// <summary>
    /// docs/07 §6 — money still to be taken: a deposit whose balance is due, or
    /// a share of a split bill nobody has paid yet.
    /// </summary>
    private Task<bool> HasScheduledChargeAsync(int userId, string last4, CancellationToken ct) =>
        db.Bookings.AnyAsync(
            b => b.GuestUserId == userId
                 && b.Payment != null && b.Payment.CardLast4 == last4
                 && b.BalanceDueOn != null && b.BalanceDue > 0
                 && BookingLifecycle.BlocksDates.Contains(b.Status), ct);

    private Task<bool> HasOpenBookingAsync(int userId, string last4, CancellationToken ct) =>
        db.Bookings.AnyAsync(
            b => b.GuestUserId == userId
                 && b.Payment != null && b.Payment.CardLast4 == last4
                 && BookingLifecycle.BlocksDates.Contains(b.Status), ct);

    /// <summary>
    /// docs/07 §4 — "Thẻ sắp hết hạn mà còn lịch thu tự động → nhắc khách cập
    /// nhật trước 14 ngày." Called from the nightly sweep, once per card.
    /// </summary>
    public static async Task<int> RemindExpiringAsync(
        StayHostDbContext db, NotificationService notifications, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sent = 0;

        var cards = await db.SavedCards
            .Where(c => c.ExpiryReminderSentAt == null)
            .Include(c => c.User)
            .ToListAsync(ct);

        foreach (var card in cards.Where(c => SavedCards.ExpiringSoon(c, today)))
        {
            var owing = await db.Bookings.AnyAsync(
                b => b.GuestUserId == card.UserId
                     && b.Payment != null && b.Payment.CardLast4 == card.Last4
                     && b.BalanceDueOn != null && b.BalanceDue > 0
                     && BookingLifecycle.BlocksDates.Contains(b.Status), ct);

            // Only a card with money still to come off it is worth a reminder;
            // otherwise this is an email about nothing.
            if (!owing) continue;

            await notifications.QueueWithEmailAsync(card.User, NotificationKind.System,
                "Thẻ sắp hết hạn", SavedCards.ExpiryReminder(card), "/account/payments", ct);

            card.ExpiryReminderSentAt = DateTime.UtcNow;
            sent++;
        }

        if (sent > 0) await db.SaveChangesAsync(ct);
        return sent;
    }
}
