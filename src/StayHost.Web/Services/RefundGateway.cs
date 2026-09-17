using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Services.Gateways;

namespace StayHost.Web.Services;

/// <summary>
/// docs/07 §10 — the one place that actually sends a guest's money back.
///
/// Five paths cancel a booking: the guest, the host, an admin's manual refund, a
/// suspension, and the sweep that gives up on a part-paid stay. Before this, one
/// of them asked the stand-in gateway and the other four passed
/// <c>cardRefundAccepted: true</c> without asking anything at all — a default
/// that was harmless while no real money existed and became a lie the day VNPay
/// was switched on.
///
/// The answer that matters is not yes or no but <em>refused</em> versus
/// <em>don't know</em>. A refusal is §10's own case: the card is closed, so the
/// money becomes balance and the guest is told. Not knowing is different, and
/// the gateways are asked again rather than guessed at.
/// </summary>
public class RefundGateway(
    StayHostDbContext db, PspRouter router, PaymentGateway gateway, ILogger<RefundGateway> log)
{
    /// <summary>What went back to where it came from, out of what was asked.</summary>
    public sealed record Sent(decimal Asked, decimal ToCard)
    {
        public bool All => ToCard >= Asked;

        /// <summary>What has to become balance instead (docs/07 §10).</summary>
        public decimal Bounced => Math.Max(0m, Asked - ToCard);
    }

    /// <summary>
    /// True when the money is on its way back to where it came from; false when
    /// some of it has to become balance instead (docs/07 §10, <c>Refunds.Redirect</c>).
    /// </summary>
    /// <param name="refund">
    /// The outcome's whole refund. Only the part that was paid in money is sent:
    /// balance and coupon come back as what they were, and a part-paid stay
    /// cannot return more than its deposit (docs/01 ĐP-06).
    /// </param>
    public Task<Sent> SendAsync(
        Booking booking, decimal refund, string by, string reason, CancellationToken ct) =>
        SendForAsync(
            s => s.BookingId == booking.Id,
            Controllers.BookingsController.CashBackOf(booking, refund), booking.Reference,
            booking.Payment?.Method ?? "card", booking.Payment?.CardLast4, by, ct);

    /// <summary>
    /// Sends <paramref name="amount"/> back across every paid gateway visit of one
    /// subject — a stay, a ticket, a service — newest first, never asking any
    /// visit for more than it still holds.
    ///
    /// A stay can be paid in more than one visit: the deposit and the balance of
    /// docs/01 ĐP-06, or one visit per person of a split bill. Refunding only the
    /// newest one asked VNPay to return more than that transaction ever took,
    /// which it refuses — and a refusal is read as "card closed", so the guest
    /// was moved to balance for money their card could have had back.
    /// </summary>
    public async Task<Sent> SendForAsync(
        Expression<Func<PaymentSession, bool>> subject, decimal amount, string reference,
        string standInMethod, string? standInCard, string by, CancellationToken ct)
    {
        if (amount <= 0) return new Sent(0, 0);

        var sessions = await db.PaymentSessions
            .Where(subject)
            .Where(s => s.Status == PaymentSessionStatus.Paid && s.GiftCardId == null)
            .OrderByDescending(s => s.Id)
            .ToListAsync(ct);

        // Paid through the stand-in: no visit to a gateway was ever made. A split
        // bill paid that way was charged share by share as cards.
        if (standInMethod == "split") standInMethod = "card";
        if (sessions.Count == 0)
            return new Sent(amount, gateway.Refund(amount, standInMethod, standInCard) ? amount : 0);

        var left = amount;
        var toCard = 0m;

        foreach (var session in sessions)
        {
            if (left <= 0) break;

            var headroom = session.Amount - session.RefundedAmount;
            if (headroom <= 0) continue;

            var portion = Math.Min(left, headroom);
            left -= portion;

            if (router.ByKey(session.Provider) is not { } provider)
            {
                log.LogError("Cổng {Provider} đã tắt nên không hoàn được {Amount} cho {Reference}.",
                    session.Provider, portion, reference);
                continue;
            }

            if (await SendOneAsync(provider, session, portion, reference, by, ct))
                toCard += portion;
        }

        // More asked than the gateways ever took is not a card refund at all; the
        // caller already caps cash at what was paid, so this is only logged.
        if (left > 0)
            log.LogWarning("Yêu cầu hoàn {Amount} cho {Reference} vượt số đã thu qua cổng {Left}.",
                amount, reference, left);

        return new Sent(amount - left, toCard);
    }

    private async Task<bool> SendOneAsync(
        IPspProvider provider, PaymentSession session, decimal amount, string reference, string by,
        CancellationToken ct)
    {
        var result = await provider.RefundAsync(new PspRefund(
            session.OrderRef, amount, session.Amount, session.ProviderTxnId,
            session.ProviderPaidAt, session.CreatedAt,
            // VNPay puts this on the guest's statement, so it is plain and short
            // rather than an internal reason code.
            $"Hoan tien don {reference}", by), ct);

        // docs/07 §7 — a refund the platform cannot see is a day that will not
        // reconcile and nobody able to say why, so what the gateway answered is
        // written down whichever way it went. Added, not overwritten: a visit can
        // be refunded in parts, and the headroom above reads this.
        if (result.Outcome != Psp.RefundOutcome.Refused) session.RefundedAmount += amount;
        session.RefundTxnId = result.TxnId;
        session.RefundCode = result.Code;
        session.RefundedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        switch (result.Outcome)
        {
            case Psp.RefundOutcome.Accepted:
                log.LogInformation("Đã yêu cầu {Provider} hoàn {Amount} cho {Reference}.",
                    session.Provider, amount, reference);
                return true;

            case Psp.RefundOutcome.Refused:
                // docs/07 §10 — "Không được thì chuyển thành số dư trong tài khoản
                // sàn và báo khách." The caller does both.
                log.LogWarning("{Provider} từ chối hoàn {Amount} cho {Reference}; chuyển sang số dư.",
                    session.Provider, amount, reference);
                return false;

            default:
                // Nobody knows. The money becomes balance so the guest is not left
                // with nothing, and this is shouted about because it is the one
                // outcome that can end in a guest being paid twice — the refund
                // may yet land at the gateway under the request id in the log.
                log.LogError(
                    "Không rõ {Provider} đã hoàn {Amount} cho {Reference} hay chưa. " +
                    "Đã chuyển sang số dư — cần đối chiếu tay ở cổng.",
                    session.Provider, amount, reference);
                return false;
        }
    }
}
