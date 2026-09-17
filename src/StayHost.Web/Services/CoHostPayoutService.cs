using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// docs/02 G8, docs/07 §19 — paying the people who help run a listing.
///
/// Two jobs, deliberately kept apart:
///
///  * <see cref="AllocateAsync"/> runs inside the ordinary payout sweep, at the
///    moment a booking's transfer is decided. It carves the co-hosts' shares out
///    of the owner's earnings so the owner's transfer is already the right size.
///  * <see cref="SweepAsync"/> then gathers those shares into transfers of their
///    own, one per co-host per day, through the same batch table, the same CSV,
///    the same "a person clicked Đã chuyển" confirmation as any other payout.
///
/// A co-host is paid like a host because financially they are one: their own
/// bank account, their own verification, their own debt to the platform. That is
/// also why nothing here writes to the ledger — <see cref="PayoutService.SettleAsync"/>
/// posts when the bank actually executed the file, and a payout that posts any
/// earlier is a payout that says money moved when it did not.
/// </summary>
public class CoHostPayoutService(
    StayHostDbContext db, PayoutAccounts accounts, NotificationService notifications,
    ILogger<CoHostPayoutService> log)
{
    public sealed record Result(int Paid, int Held)
    {
        public bool Any => Paid + Held > 0;
        public override string ToString() => $"{Paid} đã lên lệnh, {Held} tạm giữ";
    }

    /* ------------------------------------------------- carving out the shares */

    /// <summary>
    /// Works out what each co-host is owed for one booking and writes it down,
    /// returning the total so the caller can shrink the owner's transfer by it.
    ///
    /// Called with the payment about to be paid out, so the earnings it divides
    /// are the ones that survived: a stay cancelled down to a fraction of itself
    /// has already had <see cref="Payment.HostPayout"/> reduced, and a percentage
    /// of the reduced figure is exactly what a co-host is entitled to. Computing
    /// this at booking time instead would pay a co-host in full for a stay that
    /// never happened.
    /// </summary>
    public async Task<decimal> AllocateAsync(Payment payment, Booking booking, CancellationToken ct)
    {
        // Already divided on an earlier sweep — the shares stand, and the payment
        // remembers what they came to. Recomputing would let terms agreed since
        // then reach back into a transfer that is already on its way.
        if (payment.CoHostShare > 0) return payment.CoHostShare;

        var terms = await TermsForAsync(booking.ListingId, ct);
        if (terms.Count == 0) return 0m;

        var earnings = payment.HostPayout;
        if (earnings <= 0) return 0m;

        var cleaning = CoHostPayouts.CleaningShare(booking.CleaningFee, booking.Subtotal, earnings);

        var split = CoHostPayouts.Allocate(
            earnings, cleaning,
            terms.Select(t => new CoHostPayouts.Terms(t.Id, t.PayoutKind, t.PayoutPercent, t.PayoutFixed)));

        if (split.Shares.Count == 0) return 0m;

        // A booking that came back round must not pay the same person twice. The
        // unique index enforces it; this is what keeps the sweep from throwing
        // inside the worker's tick and taking every sweep after it down.
        var already = await db.CoHostPayouts
            .Where(p => p.BookingId == booking.Id)
            .Select(p => p.CoHostId)
            .ToListAsync(ct);

        var total = 0m;

        foreach (var share in split.Shares)
        {
            if (already.Contains(share.CoHostId)) continue;

            var term = terms.First(t => t.Id == share.CoHostId);
            if (term.PayeeHostId is not { } payeeId) continue;

            db.CoHostPayouts.Add(new CoHostPayout
            {
                CoHostId = term.Id,
                PayeeHostId = payeeId,
                BookingId = booking.Id,
                Amount = share.Amount,
                Basis = share.Basis,
                Kind = term.PayoutKind,
                Percent = term.PayoutPercent,
                Fixed = term.PayoutFixed,
                Earnings = earnings
            });

            total += share.Amount;
        }

        payment.CoHostShare = total;
        return total;
    }

    /// <summary>
    /// The confirmed terms that apply to one listing: the ones written for that
    /// listing, plus the owner's blanket arrangements that cover every listing
    /// they have.
    ///
    /// Only <see cref="CoHostPayoutStatus.Active"/> counts. A proposal nobody
    /// answered diverts nothing, which is the whole point of asking.
    /// </summary>
    private async Task<List<CoHost>> TermsForAsync(int listingId, CancellationToken ct)
    {
        var ownerUserId = await db.Listings
            .Where(l => l.Id == listingId)
            .Select(l => l.Host!.UserId)
            .FirstOrDefaultAsync(ct);

        if (ownerUserId is null) return [];

        return await db.CoHosts
            .Where(c => c.OwnerUserId == ownerUserId
                        && c.Status == CoHostStatus.Active
                        && c.PayoutStatus == CoHostPayoutStatus.Active
                        && c.PayeeHostId != null
                        && (c.ListingId == null || c.ListingId == listingId))
            .ToListAsync(ct);
    }

    /* -------------------------------------------------------- the transfers */

    /// <summary>
    /// Gathers every share that has been decided but not yet lined up into one
    /// transfer per co-host per day, exactly as a host's own payouts are grouped
    /// (docs/07 §12.3 — the bank charges per transfer).
    ///
    /// The holds that matter here are the payee's own: an unverified account, an
    /// account changed in the last three days, a debt to the platform. The
    /// booking-level holds were already answered when the owner's payout passed
    /// them, because a share only exists once that happened.
    /// </summary>
    public async Task<Result> SweepAsync(CancellationToken ct, DateOnly? asOf = null)
    {
        var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;

        var pending = await db.CoHostPayouts
            .Where(p => p.Status == PayoutStatus.Scheduled && p.PayoutReference == null && p.Amount > 0)
            .Include(p => p.PayeeHost!).ThenInclude(h => h.User)
            .Include(p => p.Booking)
            .Include(p => p.CoHost)
            .Take(200)
            .ToListAsync(ct);

        var paid = 0;
        var held = 0;

        foreach (var group in pending.GroupBy(p => p.PayeeHostId))
        {
            var rows = group.ToList();
            var payee = rows[0].PayeeHost;
            if (payee is null) continue;

            // docs/07 §12.2 — the same three-day freeze and the same verification
            // an owner's account is held to. Somebody who takes over a co-host
            // account should not be able to redirect a transfer either.
            if (!payee.PayoutAccountVerified || Payouts.AccountFrozen(payee.PayoutAccountChangedAt, now))
            {
                held += rows.Count;
                continue;
            }

            var accountNumber = accounts.Open(payee.PayoutAccountSealed);
            var missing = PayoutFiles.Missing(payee.PayoutBankName, payee.PayoutAccountName, accountNumber);

            if (missing is not null)
            {
                held += rows.Count;

                await notifications.QueueWithEmailAsync(payee.User, NotificationKind.PayoutSent,
                    "Chưa chuyển được phần chia cho bạn",
                    accounts.CanStore ? missing : PayoutAccounts.NoKeyNotice, "/hosting?tab=earnings", ct);

                log.LogWarning("Người đồng quản lý {HostId}: {Count} phần chia chờ tài khoản nhận tiền — {Reason}",
                    payee.Id, rows.Count, missing);
                continue;
            }

            var gross = rows.Sum(r => r.Amount);
            var deduction = Payouts.Deduct(gross, payee.OwedToPlatform);

            // Counted off the batch table, not derived from a row id: a co-host
            // who is also a host can have an owner transfer and a share transfer
            // fall on the same day, and two rows sharing a reference is a unique
            // index violation thrown inside the worker's tick.
            var soFar = await db.PayoutBatches.CountAsync(b => b.HostId == payee.Id && b.DueOn == today, ct);
            var reference = Payouts.BatchReference(payee.Id, today, soFar + 1);

            db.PayoutBatches.Add(new PayoutBatch
            {
                Reference = reference,
                HostId = payee.Id,
                Amount = deduction.Transfer,
                Deducted = deduction.Applied,
                BookingCount = rows.Count,
                BankName = payee.PayoutBankName ?? "",
                AccountName = payee.PayoutAccountName ?? "",
                AccountNumber = accountNumber!,
                DueOn = today,
                Note = "Phần chia đồng quản lý"
            });

            // The debt comes off the transfer as a whole, so it is spread across
            // the shares in it rather than landing entirely on the first one and
            // making that row's statement line unreadable.
            var left = deduction.Applied;

            foreach (var row in rows)
            {
                row.Status = PayoutStatus.Sent;
                row.PayoutReference = reference;

                var share = row == rows[^1] ? left : Math.Min(left,
                    Math.Round(deduction.Applied * row.Amount / gross, 0, MidpointRounding.AwayFromZero));
                left -= share;
                row.Deducted = share;
                paid++;
            }

            payee.OwedToPlatform = deduction.StillOwed;

            var what = rows.Count == 1
                ? $"đơn {rows[0].Booking?.Reference}"
                : $"{rows.Count} đơn bạn đồng quản lý";

            var note = deduction.Applied > 0
                ? " " + Payouts.DeductionNote(deduction.Applied, deduction.StillOwed)
                : "";

            await notifications.QueueWithEmailAsync(payee.User, NotificationKind.PayoutSent,
                "Đã lên lệnh chuyển phần chia cho bạn",
                PayoutFiles.QueuedNotice(deduction.Transfer, what, reference) + note,
                "/hosting?tab=earnings", ct);
        }

        if (paid + held > 0)
        {
            await db.SaveChangesAsync(ct);
            log.LogInformation("Chia đồng quản lý {Today}: {Paid} đã lên lệnh, {Held} tạm giữ.",
                today, paid, held);
        }

        return new Result(paid, held);
    }

    /* --------------------------------------------------- proposals that lapse */

    /// <summary>
    /// docs/07 §19.2 — an offer nobody answered inside
    /// <see cref="CoHostPayouts.ConfirmWindow"/> stops being an offer.
    ///
    /// Without this a proposal sent in March would still be sitting there in
    /// September waiting to be accepted, and the owner — who has long since
    /// stopped thinking about it — would find money leaving on terms they set
    /// half a year ago. The owner is told, so making the offer again is a
    /// decision rather than a discovery.
    /// </summary>
    public async Task<int> ExpireProposalsAsync(CancellationToken ct, DateTime? asOf = null)
    {
        var now = asOf ?? DateTime.UtcNow;
        var cutoff = now - CoHostPayouts.ConfirmWindow;

        var stale = await db.CoHosts
            .Where(c => c.PayoutStatus == CoHostPayoutStatus.Proposed
                        && c.PayoutProposedAt != null && c.PayoutProposedAt <= cutoff)
            .Include(c => c.OwnerUser)
            .Include(c => c.CoHostUser)
            .Take(200)
            .ToListAsync(ct);

        foreach (var row in stale)
        {
            row.PayoutStatus = CoHostPayoutStatus.Expired;
            row.PayoutRespondedAt = now;

            await notifications.QueueWithEmailAsync(row.OwnerUser, NotificationKind.System,
                "Đề nghị chia thu nhập đã hết hạn",
                CoHostPayouts.ExpiredNotice(row.CoHostUser?.FullName ?? row.Email),
                "/hosting?tab=team", ct);
        }

        if (stale.Count > 0) await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    /* ------------------------------------------------------- taking it back */

    /// <summary>
    /// docs/07 §19.4 — bookings whose host earnings shrank after their co-host
    /// shares had already been decided.
    ///
    /// A sweep rather than a hook on the cancellation path, and deliberately so.
    /// <c>PostCancellation</c> is static and reached from seven places; a refund
    /// can also come from an admin ruling, a Shield case or a chargeback, and
    /// patching each of those is the way to miss exactly one — a co-host quietly
    /// keeping the whole share of a stay that was refunded to the guest, with
    /// nothing anywhere to show it.
    ///
    /// The trigger is arithmetic, not an event: a row remembers the earnings it
    /// was carved out of, so a payment now worth less than that is a booking that
    /// needs redividing. After that the row remembers the new figure, so a
    /// reconciled booking costs one comparison a minute and nothing else.
    /// </summary>
    public async Task<int> ReconcileSweepAsync(CancellationToken ct)
    {
        var stale = await db.CoHostPayouts
            .Where(p => db.Payments.Any(x => x.BookingId == p.BookingId && x.HostPayout < p.Earnings))
            .Select(p => p.BookingId)
            .Distinct()
            .OrderBy(id => id)
            .Take(100)
            .ToListAsync(ct);

        var touched = 0;

        foreach (var bookingId in stale)
        {
            var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);
            var payment = await db.Payments.FirstOrDefaultAsync(p => p.BookingId == bookingId, ct);
            if (booking is null || payment is null) continue;

            await ReconcileAsync(booking, payment, ct);
            touched++;
        }

        if (touched > 0) await db.SaveChangesAsync(ct);
        return touched;
    }

    /// <summary>
    /// Redivides one booking against what its host earnings are worth now.
    ///
    /// A share still waiting to go out is simply cut down — nothing was posted,
    /// so there is nothing to reverse. A share already in somebody's bank cannot
    /// be cut down, so it is recorded as a debt and comes off their next
    /// transfers through the same <see cref="HostProfile.OwedToPlatform"/> an
    /// owner is held to.
    ///
    /// No ledger entry is written here, on purpose: a debt is not a movement of
    /// money. The posting belongs to the transfer that actually recovers it,
    /// which is where <see cref="Ledger.RecoverFromCoHost"/> is called from —
    /// the same shape a lost chargeback and a fee owed on a stay paid at the
    /// property already have. Writing one here as well counts the recovery twice
    /// and the books stop agreeing with what the bank did.
    ///
    /// Returns what had to be clawed back.
    /// </summary>
    public async Task<decimal> ReconcileAsync(Booking booking, Payment payment, CancellationToken ct)
    {
        var rows = await db.CoHostPayouts
            .Where(p => p.BookingId == booking.Id)
            .Include(p => p.PayeeHost!).ThenInclude(h => h.User)
            .Include(p => p.CoHost)
            .ToListAsync(ct);

        if (rows.Count == 0) return 0m;

        var earnings = Math.Max(0m, payment.HostPayout);

        var terms = rows
            .Where(r => r.CoHost is not null)
            .Select(r => new CoHostPayouts.Terms(
                r.CoHostId, r.CoHost!.PayoutKind, r.CoHost.PayoutPercent, r.CoHost.PayoutFixed));

        var cleaning = CoHostPayouts.CleaningShare(booking.CleaningFee, booking.Subtotal, earnings);
        var split = CoHostPayouts.Allocate(earnings, cleaning, terms);

        var clawed = 0m;

        foreach (var row in rows)
        {
            var entitled = split.Shares.FirstOrDefault(s => s.CoHostId == row.CoHostId).Amount;
            var over = row.Amount - row.ClawedBack - entitled;

            if (over > 0)
            {
                if (row.Status == PayoutStatus.Paid)
                {
                    // Already in their bank. Recorded as a debt, and said to them
                    // in words that name the booking: a deduction nobody explains
                    // is how somebody concludes their money was taken.
                    if (row.PayeeHost is not null)
                    {
                        row.PayeeHost.OwedToPlatform += over;

                        await notifications.QueueWithEmailAsync(row.PayeeHost.User, NotificationKind.PayoutSent,
                            "Phần chia của một đơn đã hoàn tiền",
                            CoHostPayouts.ClawbackNotice(over, booking.Reference), "/hosting?tab=earnings", ct);
                    }

                    row.ClawedBack += over;
                }
                else
                {
                    row.Amount = Math.Max(0m, entitled);
                }

                clawed += over;
            }

            // What this row was divided out of, as it stands now. It is the mark
            // that says this booking has been reconciled, so the sweep does not
            // pick it up again every minute for the rest of its life.
            row.Earnings = earnings;
        }

        // The owner's transfer and their ledger posting are both sized by this
        // column, so it has to follow the shares down. A share already in
        // somebody's bank still counts: that money did leave the owner's
        // earnings, whatever the refund said afterwards.
        payment.CoHostShare = rows.Sum(r => r.Amount);

        return clawed;
    }
}
