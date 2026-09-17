using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// docs/01 ĐP-07 — the parts of a split bill that happen away from a request:
/// sending the invitations, turning a fully-paid split into a real booking, and
/// giving the money back when the day runs out.
/// </summary>
public class SplitBillService(
    StayHostDbContext db, PaymentCompletion completion, NotificationService notifications,
    WalletService wallet, RefundGateway refunds, ILogger<SplitBillService> log)
{
    public async Task InviteAsync(BillSplit split, Booking booking, CancellationToken ct)
    {
        foreach (var share in split.Shares.Where(s => s.Email != null))
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == share.Email, ct);

            // docs/01 TK-09 — the frame follows the invitee's language when they
            // have an account. The pay TOKEN in the body is a secret, so
            // RawTitle stays null and the machine-translation pass skips this.
            var name = share.Name ?? share.Email;
            db.EmailMessages.Add(new EmailMessage
            {
                ToEmail = share.Email,
                ToName = name,
                Subject = $"Trả phần của bạn cho chuyến đi {booking.Reference}",
                Body = Emails.Compose(user?.Language, name,
                    $"Bạn được mời cùng trả cho chuyến đi tại {booking.Listing?.Title}.",
                    $"Phần của bạn: {share.Amount:#,##0}₫.\n" +
                    $"Mở liên kết này để trả: /split/{share.Token}\n" +
                    "Liên kết có hiệu lực trong 24 giờ.", null),
                Language = user?.Language
            });

            if (user is not null)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = user.Id,
                    Kind = NotificationKind.System,
                    Title = "Bạn được mời cùng trả một chuyến đi",
                    Body = $"Phần của bạn là {share.Amount:#,##0}₫ cho đơn {booking.Reference}.",
                    Link = $"/split/{share.Token}"
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// One share's money arrived, by whichever road. False when the share is no
    /// longer waiting for it — already paid, or the split closed while the payer
    /// was on the gateway's page — which tells a gateway caller to send it back.
    /// </summary>
    public async Task<bool> SharePaidAsync(
        int shareId, decimal amount, CancellationToken ct, string? name = null, string? cardLast4 = null)
    {
        var share = await db.BillShares
            .Include(s => s.Split!).ThenInclude(x => x.Shares)
            .FirstOrDefaultAsync(s => s.Id == shareId, ct);
        if (share?.Split is not { } split) return false;

        if (!BillSplitRules.IsOpen(split.Status) || share.Status != BillShareStatus.Waiting
            || amount != share.Amount)
            return false;

        var booking = await db.Bookings
            .Include(b => b.Payment).Include(b => b.Events).Include(b => b.Listing)
            .FirstAsync(b => b.Id == split.BookingId, ct);

        var now = DateTime.UtcNow;
        share.Status = BillShareStatus.Paid;
        share.PaidAt = now;
        if (!string.IsNullOrWhiteSpace(cardLast4)) share.CardLast4 = cardLast4;
        if (!string.IsNullOrWhiteSpace(name)) share.Name = name.Trim();

        db.LedgerEntries.AddRange(Ledger.HoldShare(booking.Id, booking.Reference, share.Amount, now));
        await db.SaveChangesAsync(ct);

        // The last share turns the whole thing into an ordinary paid booking.
        if (split.Shares.All(s => s.Status == BillShareStatus.Paid))
            await CompleteAsync(split, booking, ct);

        return true;
    }

    /// <summary>
    /// Everyone paid. The money held in escrow becomes the booking's, and from
    /// here on it is an ordinary confirmed booking with an ordinary receipt.
    /// </summary>
    public async Task CompleteAsync(BillSplit split, Booking booking, CancellationToken ct)
    {
        // The price the organiser agreed to, coupon and balance included. A fresh
        // quote without them was larger than what the shares added up to, and the
        // difference went on the books as money the guest still owed — while the
        // balance they had put in was never taken from their wallet at all, and
        // came back to them in full when the stay was cancelled.
        var price = await completion.QuoteFromRecordAsync(booking, ct)
                    ?? throw new InvalidOperationException($"Không dựng lại được giá đơn {booking.Reference}.");

        var now = DateTime.UtcNow;
        db.LedgerEntries.AddRange(Ledger.ReleaseEscrow(booking, split.Total, now));
        db.LedgerEntries.AddRange(Ledger.CaptureBooking(booking, price, now, split.Total, booking.CreditUsed));

        if (booking.CreditUsed > 0 && booking.GuestUserId is { } spender)
            wallet.Add(spender, -booking.CreditUsed, CreditReason.Spent,
                $"Dùng cho đơn {booking.Reference}", booking.Id);

        db.BookingEvents.Add(BookingLifecycle.Transition(
            booking, BookingStatus.Confirmed, $"guest:{split.OrganiserUserId}",
            $"Chia hoá đơn cho {split.Shares.Count} người, đã trả đủ."));

        booking.DepositPaid = split.Total;
        booking.BalanceDue = 0;
        booking.BalanceStatus = BalanceStatus.None;

        if (booking.Payment is not null)
        {
            booking.Payment.Status = PaymentStatus.Captured;
            booking.Payment.CapturedAt = now;
            booking.Payment.Method = "split";
        }

        split.Status = BillSplitStatus.Complete;
        split.CompletedAt = now;

        await db.SaveChangesAsync(ct);

        var organiser = await db.Users.FirstOrDefaultAsync(u => u.Id == split.OrganiserUserId, ct);
        await notifications.QueueWithEmailAsync(
            organiser, NotificationKind.BookingConfirmed,
            "Mọi người đã trả đủ",
            $"Đơn {booking.Reference} đã được xác nhận.",
            $"/trips/{booking.Id}", ct);

        await db.SaveChangesAsync(ct);
        log.LogInformation("Split bill for {Reference} completed.", booking.Reference);
    }

    /// <summary>
    /// The split is over without completing. Everything collected goes back to
    /// the people who sent it, and the dates return to the market.
    /// </summary>
    public async Task UnwindAsync(BillSplit split, BillSplitStatus status, string reason, CancellationToken ct)
    {
        var booking = split.Booking ?? await db.Bookings
            .Include(b => b.Events)
            .FirstAsync(b => b.Id == split.BookingId, ct);

        // One query for every payer's language, not one per share. Most shares
        // belong to strangers with no account; null means Vietnamese.
        var paidEmails = split.Shares
            .Where(s => s.Status == BillShareStatus.Paid && s.Email != null)
            .Select(s => s.Email!.ToLower()).ToList();
        var payers = await db.Users
            .Where(u => paidEmails.Contains(u.Email))
            .Select(u => new { u.Id, u.Email, u.Language })
            .ToListAsync(ct);

        foreach (var share in split.Shares.Where(s => s.Status == BillShareStatus.Paid))
        {
            db.LedgerEntries.AddRange(
                Ledger.ReturnShare(booking.Id, booking.Reference, share.Amount, DateTime.UtcNow));
            share.Status = BillShareStatus.Returned;

            var payer = share.Email is null
                ? null
                : payers.FirstOrDefault(p => string.Equals(p.Email, share.Email, StringComparison.OrdinalIgnoreCase));

            // docs/07 §10 — back to the card it came from. The email below used to
            // say so with no call to any gateway behind it.
            var shareId = share.Id;
            var sent = await refunds.SendForAsync(
                s => s.BillShareId == shareId, share.Amount, booking.Reference,
                "card", share.CardLast4, "system", ct);

            var body = $"{reason}\nSố tiền {share.Amount:#,##0}₫ đã được hoàn về phương thức bạn đã dùng.";
            if (sent.Bounced > 0)
            {
                if (payer is not null)
                {
                    db.CreditEntries.Add(CreditLedger.Grant(
                        payer.Id, sent.Bounced, CreditReason.Returned,
                        $"Hoàn phần chia đơn {booking.Reference} — thẻ không nhận được", DateTime.UtcNow));
                    body = $"{reason}\n{Refunds.RedirectNotice(sent.Bounced)}";
                }
                else
                {
                    // A stranger with no account has no balance to hold it. Said
                    // loudly: this is money support has to send by hand.
                    log.LogError("Không hoàn được {Amount} cho phần chia {ShareId} của đơn {Reference}; người trả không có tài khoản.",
                        sent.Bounced, share.Id, booking.Reference);
                }
            }

            var name = share.Name ?? share.Email;
            db.EmailMessages.Add(new EmailMessage
            {
                ToEmail = share.Email,
                ToName = name,
                Subject = $"Đã hoàn lại phần của bạn — đơn {booking.Reference}",
                Body = Emails.Compose(payer?.Language, name ?? "",
                    $"Đã hoàn lại phần của bạn cho đơn {booking.Reference}.", body, null),
                Language = payer?.Language
            });
        }

        split.Status = status;

        if (booking.Status == BookingStatus.PendingPayment)
        {
            db.BookingEvents.Add(BookingLifecycle.Transition(
                booking, BookingStatus.PaymentFailed, "system", reason));
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Split bill for {Reference} unwound: {Reason}", booking.Reference, reason);
    }

    /// <summary>Splits whose day ran out. Called from the lifecycle sweep.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var stale = await db.BillSplits
            .Include(s => s.Shares)
            .Include(s => s.Booking!).ThenInclude(b => b.Events)
            .Where(s => s.Status == BillSplitStatus.Collecting && s.ExpiresAt <= now)
            .ToListAsync(ct);

        foreach (var split in stale)
            await UnwindAsync(split, BillSplitStatus.Expired, "Quá 24 giờ mà chưa đủ người trả.", ct);

        return stale.Count;
    }
}
