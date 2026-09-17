using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// Everything that happens once money has actually moved for a booking.
///
/// It lives here rather than inside the pay endpoint because docs/07 §5 says the
/// platform must confirm a booking it later discovers was paid — the guest whose
/// connection dropped between the bank taking the money and the reply reaching
/// them. That case arrives from a background sweep, not from a request, and both
/// have to do exactly the same thing or the two paths drift.
/// </summary>
public class PaymentCompletion(
    StayHostDbContext db, NotificationService notifications, ThreadMessenger messenger,
    WalletService wallet, RiskWatch risk, CatalogService catalog)
{
    /// <summary>
    /// The breakdown for a booking that was priced some time ago, rebuilt for a
    /// confirmation arriving after the fact — a card the gateway settled while
    /// the guest's connection was down (docs/07 §5), or a bank transfer landing
    /// hours after checkout (docs/07 §2.3).
    ///
    /// It reproduces the total the guest agreed to rather than re-pricing the
    /// stay: the coupon and the balance spent are the figures frozen on the
    /// booking, so a campaign that has since ended cannot change a total that
    /// has already been paid.
    /// </summary>
    public async Task<Pricing.Breakdown?> QuoteFromRecordAsync(Booking booking, CancellationToken ct)
    {
        var party = new PartySize(booking.Adults, booking.Children, booking.Infants, booking.Pets);

        var fresh = await catalog.BuildQuoteRequestAsync(
            booking.ListingId, booking.CheckIn, booking.CheckOut, party, ct, booking.Id, booking.RoomTypeId,
            // docs/01 ĐP-17 — the offer's rate must survive the off-site rescue too.
            nightlyOverride: booking.NightlyOverride, plan: booking.Plan, loyaltyPercent: booking.LoyaltyPercent);

        if (fresh is null) return null;

        if (booking.CouponDiscount > 0)
            fresh = fresh with { CouponAmount = booking.CouponDiscount, CouponLabel = "Mã giảm giá" };

        if (booking.CreditUsed > 0)
            fresh = fresh with { PromotionAmount = booking.CreditUsed, PromotionLabel = "Số dư Staylio" };

        return Pricing.Quote(fresh);
    }

    /// <param name="guestUserId">
    /// docs/07 §2.5 — null when nobody signed in for this booking. Two call
    /// sites already passed <c>GuestUserId ?? 0</c>, which reached a user lookup
    /// that found nothing and quietly sent no confirmation at all — and the
    /// reference in that email is the only way a guest with no account gets back
    /// to their booking.
    /// </param>
    public async Task ConfirmAsync(
        Booking booking, Pricing.Breakdown price, decimal charged, bool partial,
        DateOnly today, int? guestUserId, string? method, string? cardLast4, CancellationToken ct)
    {
        db.BookingEvents.Add(BookingLifecycle.Transition(
            booking, BookingStatus.Confirmed,
            guestUserId is { } who ? $"guest:{who}" : $"guest:{booking.SessionId}",
            partial
                ? $"Đã đặt cọc {charged:#,##0}₫ trên tổng {price.Total:#,##0}₫."
                : "Thanh toán thành công."));

        booking.DepositPaid = charged;
        booking.BalanceDue = price.Total - charged;
        booking.BalanceDueOn = partial ? PartialPayment.BalanceDueOn(booking.CheckIn, today) : null;
        booking.BalanceStatus = partial ? BalanceStatus.Scheduled : BalanceStatus.None;

        if (booking.Payment is not null)
        {
            booking.Payment.Status = PaymentStatus.Captured;
            booking.Payment.CapturedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(method)) booking.Payment.Method = method;
            if (!string.IsNullOrWhiteSpace(cardLast4)) booking.Payment.CardLast4 = cardLast4;
        }

        // Balance is an account's, and GuestCheckout refuses a booking that tries
        // to spend it without one — so this pairing cannot arise, and asserting
        // it here beats spending user zero's balance if it ever did.
        if (booking.CreditUsed > 0 && guestUserId is { } spender)
        {
            wallet.Add(spender, -booking.CreditUsed, CreditReason.Spent,
                $"Dùng cho đơn {booking.Reference}", booking.Id);
        }

        db.LedgerEntries.AddRange(
            Ledger.CaptureBooking(booking, price, DateTime.UtcNow, charged, booking.CreditUsed));

        // docs/01 TC-03, docs/07 §12.3 — a stay of 28+ nights pays the host month
        // by month. The single-shot payout is stood down (PayoutDueOn nulled so the
        // ordinary sweep skips it) and a monthly schedule takes over. Only set up
        // once, in case this confirm runs twice from the rescue sweep.
        var schedule = Payouts.MonthlySchedule(price.HostPayout, booking.CheckIn, booking.Nights);
        if (schedule.Count > 0 && booking.Payment is not null
            && !await db.PayoutInstallments.AnyAsync(i => i.BookingId == booking.Id, ct))
        {
            booking.Payment.PayoutDueOn = null;
            foreach (var inst in schedule)
                db.PayoutInstallments.Add(new PayoutInstallment
                {
                    BookingId = booking.Id, Amount = inst.Amount, DueOn = inst.DueOn
                });
        }

        await db.SaveChangesAsync(ct);

        var listing = booking.Listing!;

        // docs/01 TN-04 — the conversation carries the order's own milestones.
        await messenger.PostAsync(booking,
            $"Đơn {booking.Reference} đã được xác nhận: {booking.CheckIn:dd/MM} – {booking.CheckOut:dd/MM}, " +
            $"{booking.Nights} đêm, {booking.Guests} khách.", ct);

        var hostUser = await db.Users.FirstOrDefaultAsync(u => u.HostProfile!.Id == listing.HostId, ct);

        await notifications.QueueWithEmailAsync(hostUser, NotificationKind.BookingConfirmed,
            "Bạn có lượt đặt mới",
            $"{booking.GuestName} đặt \"{listing.Title}\" từ {booking.CheckIn:dd/MM} đến {booking.CheckOut:dd/MM} " +
            $"({booking.Nights} đêm, {booking.Guests} khách)." +
            string.Concat(StayDetails.Sentences(booking).Select(s => " " + s)),
            "/hosting?tab=bookings", ct);

        var line = $"Mã đặt chỗ {booking.Reference} · {listing.Title} · {booking.Nights} đêm.";

        if (guestUserId is { } id)
        {
            var guest = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            await notifications.QueueWithEmailAsync(guest, NotificationKind.BookingConfirmed,
                "Đặt chỗ đã được xác nhận", line, $"/trips/{booking.Id}", ct);
        }
        else
        {
            // docs/07 §2.5 — no account, so no in-app row and no preference mask.
            // The address they typed is the only way to reach them, and this mail
            // carries the reference the whole guest-checkout promise rests on.
            notifications.QueueEmailOnly(booking.GuestEmail, booking.GuestName,
                "Đặt chỗ đã được xác nhận", line, "/dat-cho");
        }

        // docs/01 AT-11 — a paid booking is the moment worth looking at the
        // account's pattern; the flag never stands in the guest's way.
        // A pattern belongs to an account; there is none to look at here.
        if (guestUserId is { } riskUser) await risk.EvaluateAsync(riskUser, booking, ct);

        await db.SaveChangesAsync(ct);
    }
}
