using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Controllers;

namespace StayHost.Web.Services;

/// <summary>
/// docs/01 ĐP-06 and docs/03 §1 — takes the rest of a part-paid booking on its
/// date, tries again for 72 hours if the card refuses, and cancels the booking
/// under the guest's own policy if it never goes through.
/// </summary>
public class BalanceCollector(
    StayHostDbContext db,
    PaymentGateway gateway,
    NotificationService notifications,
    RefundGateway refunds,
    Gateways.PspRouter router,
    ILogger<BalanceCollector> log)
{
    public sealed class Result
    {
        public int Collected { get; set; }
        public int Refused { get; set; }
        public int Cancelled { get; set; }

        public bool Any => Collected + Refused + Cancelled > 0;

        public override string ToString() =>
            $"{Collected} thu đủ, {Refused} bị từ chối, {Cancelled} huỷ vì không thu được";
    }

    public async Task<Result> SweepAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var result = new Result();

        var due = await db.Bookings
            .Include(b => b.Payment).Include(b => b.Events).Include(b => b.Listing)
            .Where(b => b.BalanceDue > 0
                        && (b.BalanceStatus == BalanceStatus.Scheduled || b.BalanceStatus == BalanceStatus.Retrying)
                        && b.BalanceDueOn != null && b.BalanceDueOn <= today
                        && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.InProgress))
            .ToListAsync(ct);

        foreach (var booking in due)
        {
            // A refusal is retried on a schedule rather than on every tick.
            if (booking.BalanceStatus == BalanceStatus.Retrying)
            {
                var first = booking.BalanceFirstFailedAt ?? now;

                if (PartialPayment.GaveUp(first, now))
                {
                    await GiveUpAsync(booking, ct);
                    result.Cancelled++;
                    continue;
                }

                if (!PartialPayment.ShouldRetry(first, booking.BalanceLastAttemptAt ?? first, now)) continue;
            }

            var method = booking.Payment?.Method ?? "card";

            // docs/07 §13 — a deposit taken on a gateway's page leaves nothing
            // this platform can charge again: no card number, and no token unless
            // the guest kept one. The stand-in used to "collect" it anyway, so the
            // host was paid in full for a stay half paid. The guest is asked to
            // pay the rest on the gateway instead, on the same 72-hour clock a
            // refused card gets.
            if (router.IsLive(method))
            {
                booking.BalanceAttempts++;
                booking.BalanceLastAttemptAt = now;
                booking.BalanceFirstFailedAt ??= now;
                booking.BalanceStatus = BalanceStatus.Retrying;
                result.Refused++;

                await NotifyAsync(booking, "Đến hạn trả phần còn lại",
                    $"Đơn {booking.Reference} còn {booking.BalanceDue:#,##0}₫ cần trả. " +
                    "Mở đơn và bấm \"Trả phần còn lại\" trong vòng 72 giờ, nếu không đơn sẽ bị huỷ.", ct);
                continue;
            }

            var attempt = gateway.Charge(booking.BalanceDue, method, booking.Payment?.CardLast4);

            booking.BalanceAttempts++;
            booking.BalanceLastAttemptAt = now;

            if (!attempt.Ok)
            {
                booking.BalanceFirstFailedAt ??= now;
                booking.BalanceStatus = BalanceStatus.Retrying;
                result.Refused++;

                await NotifyAsync(booking, "Chưa thu được phần còn lại",
                    $"Chúng tôi chưa thu được {booking.BalanceDue:#,##0}₫ của đơn {booking.Reference}. " +
                    "Vui lòng cập nhật thẻ trong vòng 72 giờ, nếu không đơn sẽ bị huỷ.", ct);
                continue;
            }

            await ApplyAsync(booking, "system", $"Đã thu nốt {booking.BalanceDue:#,##0}₫ theo lịch.", now, ct);
            result.Collected++;
        }

        if (result.Any) await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>
    /// docs/07 §13 — a gateway says the rest of a part-paid stay arrived. False
    /// when the stay is no longer waiting for exactly that amount (it was
    /// cancelled, or already settled another way), which tells the caller to
    /// send the money back.
    /// </summary>
    public async Task<bool> CollectedAsync(int bookingId, decimal amount, string by, CancellationToken ct)
    {
        var booking = await db.Bookings
            .Include(b => b.Payment).Include(b => b.Events).Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null
            || booking.Status is not (BookingStatus.Confirmed or BookingStatus.InProgress)
            || booking.BalanceStatus is BalanceStatus.None or BalanceStatus.Paid or BalanceStatus.Failed
            || booking.BalanceDue != amount)
            return false;

        await ApplyAsync(booking, by, $"Đã thu nốt {amount:#,##0}₫ qua cổng thanh toán.", DateTime.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The balance is in: one set of steps, whichever road brought it.</summary>
    internal async Task ApplyAsync(Booking booking, string actor, string note, DateTime now, CancellationToken ct)
    {
        db.LedgerEntries.AddRange(Ledger.CollectBalance(booking, booking.BalanceDue, now));
        db.BookingEvents.Add(BookingLifecycle.Note(booking, actor, note));

        booking.DepositPaid += booking.BalanceDue;
        booking.BalanceDue = 0;
        booking.BalanceStatus = BalanceStatus.Paid;
        booking.BalanceFirstFailedAt = null;

        await NotifyAsync(booking, "Đã thu nốt phần còn lại",
            $"Đơn {booking.Reference} đã được thanh toán đủ.", ct);
    }

    /// <summary>
    /// The 72 hours are up. The booking is cancelled as the guest's own policy
    /// would have it, so the host keeps whatever that policy entitles them to
    /// rather than losing the dates for nothing.
    /// </summary>
    private async Task GiveUpAsync(Booking booking, CancellationToken ct)
    {
        var yearAgo = DateTime.UtcNow.AddYears(-1);
        var used = booking.GuestUserId is null
            ? 0
            : await db.Bookings.CountAsync(b =>
                b.GuestUserId == booking.GuestUserId &&
                b.Status == BookingStatus.CancelledByGuest &&
                // docs/03 §4 pre-rule 2 counts the guest's own cancellations.
                b.CancelledBy == CancelledBy.Guest &&
                b.RefundedAmount > 0 &&
                b.CreatedAt >= yearAgo, ct);

        var outcome = Cancellation.Refund(new Cancellation.Context
        {
            Booking = booking,
            Now = DateTime.UtcNow,
            By = CancelledBy.Guest,
            ServiceFeeRefundsUsed = used
        });

        // docs/07 §10 — ask the gateway before deciding where the money lands.
        // This used to default to "the card took it" without asking anything,
        // which was harmless until a real gateway held the money.
        var sentBack = await refunds.SendAsync(
            booking, outcome.Amount, "system", "Khong thu duoc phan con lai", ct);

        BookingsController.PostCancellation(
            db, booking, outcome, CancelledBy.Guest, "Không thu được phần còn lại trong 72 giờ.",
            sentBack);

        log.LogInformation("Booking {Reference} cancelled: balance never collected.", booking.Reference);

        await NotifyAsync(booking, "Đơn đã bị huỷ",
            $"Đơn {booking.Reference} bị huỷ vì chưa thu được phần còn lại sau 72 giờ. " +
            $"Số tiền hoàn lại: {outcome.Amount:#,##0}₫.", ct);
    }

    private async Task NotifyAsync(Booking booking, string title, string body, CancellationToken ct)
    {
        if (booking.GuestUserId is not { } guestId) return;

        var guest = await db.Users.FirstOrDefaultAsync(u => u.Id == guestId, ct);
        await notifications.QueueWithEmailAsync(
            guest, NotificationKind.System, title, body, $"/trips/{booking.Id}", ct);
    }
}
