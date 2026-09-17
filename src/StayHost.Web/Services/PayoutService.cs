using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// docs/07 §12 — actually sends hosts their money.
///
/// Until this existed a payout was scheduled and then sat there: every booking
/// ever completed still read "Scheduled", and the five hold reasons of §12.4
/// were a list nobody consulted. The transfer itself goes through the same
/// stand-in as a card charge — there is no bank behind this build — but every
/// decision around it is real.
/// </summary>
public class PayoutService(
    StayHostDbContext db, PayoutAccounts accounts, NotificationService notifications,
    CoHostPayoutService coHosts, ILogger<PayoutService> log)
{
    public sealed record Result(int Paid, int Held, int Failed)
    {
        public bool Any => Paid + Held + Failed > 0;
        public override string ToString() => $"{Paid} đã chuyển, {Held} tạm giữ, {Failed} thất bại";
    }

    public async Task<Result> SweepAsync(CancellationToken ct, DateOnly? asOf = null)
    {
        var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;

        // Sent is left alone: that booking is already a row in a transfer file
        // waiting on the bank. Picking it up again put it in a second file the
        // next day — the retry step below is one day — and paid the host twice
        // once both were executed. A refused file puts it back to Scheduled.
        var due = await db.Payments
            .Where(p => p.PayoutStatus != PayoutStatus.Paid && p.PayoutStatus != PayoutStatus.Sent
                        && p.PayoutDueOn != null && p.PayoutDueOn <= today
                        && p.Status == PaymentStatus.Captured)
            .Include(p => p.Booking!).ThenInclude(b => b.Listing!).ThenInclude(l => l.Host!).ThenInclude(h => h.User)
            .Take(200)
            .ToListAsync(ct);

        var paid = 0;
        var held = 0;
        var failed = 0;

        // Everything that survives the holds, kept per host so one bank transfer
        // can carry the lot (docs/07 §12.3).
        var payable = new Dictionary<int, (HostProfile Host, List<Payment> Payments)>();

        foreach (var payment in due)
        {
            var booking = payment.Booking;
            var host = booking?.Listing?.Host;
            if (booking is null || host is null) continue;

            // docs/07 §12.5 — a failed transfer waits its turn before the next go.
            if (payment.PayoutAttempts > 0 && payment.PayoutLastAttemptOn is { } last)
            {
                var next = Payouts.NextAttemptOn(last, payment.PayoutAttempts);
                if (next is null || next > today) continue;
            }

            var reason = Payouts.HoldReason(await ConditionsAsync(booking, host, payment.HostPayout, ct), now);

            if (reason != PayoutHoldReason.None)
            {
                if (payment.PayoutStatus != PayoutStatus.OnHold || payment.PayoutHoldReason != reason)
                {
                    payment.PayoutStatus = PayoutStatus.OnHold;
                    payment.PayoutHoldReason = reason;

                    await notifications.QueueWithEmailAsync(host.User, NotificationKind.PayoutSent,
                        "Khoản chuyển tiền đang tạm giữ",
                        $"Đơn {booking.Reference}: {Payouts.HoldLabel(reason)}.",
                        "/hosting", ct);
                }
                held++;
                continue;
            }

            // docs/02 G8, docs/07 §19 — anyone the owner agreed to share with is
            // paid out of this same booking, so their shares are carved off before
            // the owner's transfer is sized. Done here rather than at booking time
            // because HostPayout is what survived a cancellation, and a percentage
            // of the reduced figure is what a co-host is entitled to.
            await coHosts.AllocateAsync(payment, booking, ct);

            if (!payable.TryGetValue(host.Id, out var batch))
            {
                batch = (host, []);
                payable[host.Id] = batch;
            }
            batch.Payments.Add(payment);
        }

        foreach (var (hostId, batch) in payable)
        {
            var (host, payments) = batch;

            // A host normally gets one transfer a day; anything that only became
            // payable after the first sweep is a transfer of its own, and must
            // not borrow the first one's reference.
            //
            // Counted off the batch table rather than off PaidOutAt. That column
            // used to be set the moment a transfer was decided, so it worked as a
            // tally; it now means "a bank executed this", which for a transfer
            // still waiting is null. Counting it gave every same-day transfer the
            // same reference, the unique index refused the second one, and because
            // that throw happens inside the worker's tick it took the sweeps after
            // it down too — silently, until somebody read the log.
            var soFar = await db.PayoutBatches
                .CountAsync(b => b.HostId == hostId && b.DueOn == today, ct);

            var reference = Payouts.BatchReference(hostId, today, soFar + 1);

            // What is left after the co-hosts (docs/07 §19). Their shares travel
            // in transfers of their own, to their own banks, so the owner's file
            // row must not carry them.
            var gross = payments.Sum(p => p.HostPayout - p.CoHostShare);
            var deduction = Payouts.Deduct(gross, host.OwedToPlatform);

            // docs/07 §14.3 — the number the transfer actually needs. Kept sealed
            // and opened here, once, for the one job that requires it.
            var accountNumber = accounts.Open(host.PayoutAccountSealed);
            var missing = PayoutFiles.Missing(host.PayoutBankName, host.PayoutAccountName, accountNumber);

            if (missing is not null)
            {
                // Nowhere to send it. Not a failure of the transfer — it was never
                // attempted — so it waits rather than burning a retry, and the
                // host is told what is missing. This is also what a host sees when
                // the server has no encryption key at all.
                held += payments.Count;

                foreach (var payment in payments)
                {
                    if (payment.PayoutStatus == PayoutStatus.OnHold) continue;
                    payment.PayoutStatus = PayoutStatus.OnHold;
                    payment.PayoutHoldReason = PayoutHoldReason.None;

                    await notifications.QueueWithEmailAsync(host.User, NotificationKind.PayoutSent,
                        "Chưa chuyển tiền được cho bạn",
                        accounts.CanStore ? missing : PayoutAccounts.NoKeyNotice, "/hosting", ct);
                }

                log.LogWarning("Chủ nhà {HostId}: {Count} đơn đã tới hạn chuyển nhưng {Reason}",
                    hostId, payments.Count, missing);
                continue;
            }

            foreach (var payment in payments)
            {
                payment.PayoutAttempts++;
                payment.PayoutLastAttemptOn = today;
            }

            // docs/07 §13 option A — one transfer for the batch, because the bank
            // charges per transfer and that is the whole reason for grouping. It
            // is written down rather than executed: nothing here can move money,
            // and pretending otherwise is what the old call to the stand-in
            // gateway did.
            db.PayoutBatches.Add(new PayoutBatch
            {
                Reference = reference,
                HostId = hostId,
                Amount = deduction.Transfer,
                Deducted = deduction.Applied,
                BookingCount = payments.Count,
                BankName = host.PayoutBankName ?? "",
                AccountName = host.PayoutAccountName ?? "",
                AccountNumber = accountNumber!,
                DueOn = today
            });

            // The debt comes off the batch as a whole, so it is spread across the
            // bookings in it — otherwise the first booking of the day would wear
            // the entire deduction and its report line would read as nonsense.
            var left = deduction.Applied;

            foreach (var payment in payments)
            {
                // Sent, not Paid. The ledger entries and PaidOutAt belong to the
                // moment a bank executed the file, which is Settle() below.
                payment.PayoutStatus = PayoutStatus.Sent;
                payment.PayoutHoldReason = PayoutHoldReason.None;
                payment.PayoutReference = reference;

                var share = payment == payments[^1] || gross <= 0 ? left : Math.Min(left,
                    Math.Round(deduction.Applied * (payment.HostPayout - payment.CoHostShare) / gross,
                        0, MidpointRounding.AwayFromZero));
                left -= share;
                payment.PayoutDeducted = share;
                paid++;
            }

            host.OwedToPlatform = deduction.StillOwed;

            // The money arrives as one line on the statement, so say so — and say
            // how many bookings made it up, because that is the question a host
            // asks next (docs/07 §12.3, "báo cáo vẫn tách theo từng đơn").
            var what = payments.Count == 1
                ? $"đơn {payments[0].Booking!.Reference}"
                : $"{payments.Count} đơn";

            var note = deduction.Applied > 0
                ? " " + Payouts.DeductionNote(deduction.Applied, deduction.StillOwed)
                : "";

            // docs/07 §19 — an owner seeing a smaller number than they expected
            // is owed the reason in the same message, not on another screen.
            var shared = payments.Sum(p => p.CoHostShare);
            if (shared > 0)
            {
                var people = await db.CoHostPayouts
                    .Where(p => payments.Select(x => x.BookingId).Contains(p.BookingId))
                    .Select(p => p.CoHostId)
                    .Distinct()
                    .CountAsync(ct);

                note += " " + CoHostPayouts.DeductionNote(shared, Math.Max(1, people));
            }

            await notifications.QueueWithEmailAsync(host.User, NotificationKind.PayoutSent,
                "Đã lên lệnh chuyển tiền cho bạn",
                PayoutFiles.QueuedNotice(deduction.Transfer, what, reference) + note,
                "/hosting?tab=earnings", ct);
        }

        if (paid + held + failed > 0)
        {
            await db.SaveChangesAsync(ct);
            log.LogInformation("Chuyển tiền {Today}: {Paid} đã chuyển, {Held} tạm giữ, {Failed} thất bại.",
                today, paid, held, failed);
        }

        return new Result(paid, held, failed);
    }

    /// <summary>
    /// docs/01 TC-03, docs/07 §12.3 — the monthly payout for long stays. Each due
    /// instalment is paid on its own, held by the same conditions as any payout,
    /// with the same retry backoff. When the last one settles, the booking's
    /// payment is marked paid so the reports agree.
    /// </summary>
    public async Task<Result> InstallmentSweepAsync(CancellationToken ct, DateOnly? asOf = null)
    {
        var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;

        var due = await db.PayoutInstallments
            .Where(i => !i.Paid && i.DueOn <= today)
            .Include(i => i.Booking!).ThenInclude(b => b.Listing!).ThenInclude(l => l.Host!).ThenInclude(h => h.User)
            .Include(i => i.Booking!).ThenInclude(b => b.Payment)
            .Take(200)
            .ToListAsync(ct);

        int paid = 0, held = 0, failed = 0;

        foreach (var inst in due)
        {
            var booking = inst.Booking;
            var host = booking?.Listing?.Host;
            if (booking is null || host is null || booking.Payment?.Status != PaymentStatus.Captured) continue;

            // Same retry cadence as an ordinary payout.
            if (inst.Attempts > 0 && inst.LastAttemptOn is { } last)
            {
                var next = Payouts.NextAttemptOn(last, inst.Attempts);
                if (next is null || next > today) continue;
            }

            var reason = Payouts.HoldReason(await ConditionsAsync(booking, host, inst.Amount, ct), now);
            if (reason != PayoutHoldReason.None) { held++; continue; }

            var accountNumber = accounts.Open(host.PayoutAccountSealed);
            if (PayoutFiles.Missing(host.PayoutBankName, host.PayoutAccountName, accountNumber) is not null)
            {
                held++;
                continue;
            }

            inst.Attempts++;
            inst.LastAttemptOn = today;

            var deduction = Payouts.Deduct(inst.Amount, host.OwedToPlatform);

            // Same as an ordinary payout: the instalment is lined up in a batch of
            // its own and the ledger waits for a bank. A monthly stay that pretends
            // to have paid is no better than a daily one that does.
            //
            // The sequence is counted, not derived from the instalment id: two
            // instalments for one host on one day would otherwise be able to land
            // on the same reference, and the unique index turns that into a throw
            // that takes the rest of the tick with it.
            var soFar = await db.PayoutBatches
                .CountAsync(x => x.HostId == host.Id && x.DueOn == today, ct);

            var reference = Payouts.BatchReference(host.Id, today, soFar + 1);

            db.PayoutBatches.Add(new PayoutBatch
            {
                Reference = reference,
                HostId = host.Id,
                Amount = deduction.Transfer,
                Deducted = deduction.Applied,
                BookingCount = 1,
                BankName = host.PayoutBankName ?? "",
                AccountName = host.PayoutAccountName ?? "",
                AccountNumber = accountNumber!,
                DueOn = today,
                Note = $"Đợt tháng · đơn {booking.Reference}"
            });

            host.OwedToPlatform = deduction.StillOwed;
            inst.Paid = true;
            inst.PaidAt = now;
            paid++;

            // When the final instalment is lined up, the payment itself stops
            // showing as still-to-pay; it becomes Paid when its batch settles.
            var remaining = await db.PayoutInstallments
                .CountAsync(x => x.BookingId == booking.Id && !x.Paid && x.Id != inst.Id, ct);
            if (remaining == 0 && booking.Payment is not null)
            {
                booking.Payment.PayoutStatus = PayoutStatus.Sent;
                booking.Payment.PayoutReference = reference;
            }

            await notifications.QueueWithEmailAsync(host.User, NotificationKind.PayoutSent,
                "Đã lên lệnh chuyển đợt tiền theo tháng",
                PayoutFiles.QueuedNotice(deduction.Transfer,
                    $"đơn {booking.Reference} (đơn dài, trả theo tháng)", reference),
                "/hosting?tab=earnings", ct);
        }

        if (paid + held + failed > 0)
        {
            await db.SaveChangesAsync(ct);
            log.LogInformation("Trả theo tháng {Today}: {Paid} đợt đã chuyển, {Held} giữ, {Failed} lỗi.",
                today, paid, held, failed);
        }

        return new Result(paid, held, failed);
    }

    /* ------------------------------------------------ docs/07 §13, the bank */

    /// <summary>
    /// A person put the file through internet banking and the bank took it.
    ///
    /// This is the only place a payout is posted to the ledger, because it is the
    /// only moment the money stopped being the platform's. Everything before it —
    /// deciding the transfer, writing the file, downloading it — moves no money,
    /// and the books have to agree with that or the daily reconciliation of
    /// docs/07 §7 is comparing one fiction against another.
    /// </summary>
    public async Task<bool> SettleAsync(long batchId, string actor, string? note, CancellationToken ct)
    {
        var batch = await db.PayoutBatches
            .Include(b => b.Host!).ThenInclude(h => h.User)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct);

        if (batch is null || batch.Status == PayoutBatchStatus.Settled) return false;

        var now = DateTime.UtcNow;

        var payments = await db.Payments
            .Where(p => p.PayoutReference == batch.Reference)
            .Include(p => p.Booking)
            .ToListAsync(ct);

        foreach (var payment in payments)
        {
            if (payment.PayoutStatus == PayoutStatus.Paid) continue;

            payment.PayoutStatus = PayoutStatus.Paid;
            payment.PaidOutAt = now;

            db.LedgerEntries.AddRange(Ledger.RecoverFromHost(payment.Booking!, payment.PayoutDeducted, now));

            // docs/07 §19 — the owner is posted for their own share only. The
            // co-hosts' part is posted against the transfer that carries it, and
            // the two together still debit HostPayable by the whole payout, so
            // nothing new has to be explained to the daily reconciliation.
            db.LedgerEntries.AddRange(Ledger.PayoutHost(payment.Booking!,
                payment.HostPayout - payment.PayoutDeducted - payment.CoHostShare, now));
        }

        // docs/02 G8 — the shares of anyone helping run the listing. They travel
        // in their own transfer to their own bank, so they settle on their own
        // reference, on whatever day that transfer was executed.
        foreach (var share in await db.CoHostPayouts
                     .Where(p => p.PayoutReference == batch.Reference && p.Status != PayoutStatus.Paid)
                     .Include(p => p.Booking)
                     .ToListAsync(ct))
        {
            share.Status = PayoutStatus.Paid;
            share.PaidOutAt = now;

            db.LedgerEntries.AddRange(Ledger.RecoverFromCoHost(share.Booking!, share.Deducted, now));
            db.LedgerEntries.AddRange(
                Ledger.PayoutCoHost(share.Booking!, share.Amount - share.Deducted, now));
        }

        // docs/09 §4 — experiences and services are paid out of the same file and
        // through the same confirmation. They carry their own reference columns
        // (XP…, SV…) because ledger_entries.BookingId is a foreign key to stays.
        foreach (var ticket in await db.ExperienceBookings
                     .Where(b => b.PayoutReference == batch.Reference
                                 && b.PayoutStatus != PayoutStatus.Paid)
                     .ToListAsync(ct))
        {
            ticket.PayoutStatus = PayoutStatus.Paid;
            ticket.PaidOutAt = now;
            db.LedgerEntries.AddRange(Ledger.PayoutExperience(ticket, ticket.HostPayout, now));
        }

        foreach (var job in await db.ServiceBookings
                     .Where(b => b.PayoutReference == batch.Reference
                                 && b.PayoutStatus != PayoutStatus.Paid)
                     .ToListAsync(ct))
        {
            job.PayoutStatus = PayoutStatus.Paid;
            job.PaidOutAt = now;
            db.LedgerEntries.AddRange(Ledger.PayoutService(job, job.ProviderPayout, now));
        }

        batch.Status = PayoutBatchStatus.Settled;
        batch.SettledAt = now;
        batch.SettledBy = actor;
        if (!string.IsNullOrWhiteSpace(note)) batch.Note = note.Trim();

        var what = batch.BookingCount == 1 ? "1 đơn" : $"{batch.BookingCount} đơn";

        await notifications.QueueWithEmailAsync(batch.Host?.User, NotificationKind.PayoutSent,
            "Đã chuyển tiền cho bạn",
            PayoutFiles.SettledNotice(batch.Amount, what, batch.Reference), "/hosting?tab=earnings", ct);

        await db.SaveChangesAsync(ct);

        log.LogInformation("Lệnh chuyển {Reference} đã được ngân hàng thực hiện ({Amount}).",
            batch.Reference, batch.Amount);

        return true;
    }

    /// <summary>
    /// The bank refused it, or the operator found the row wrong.
    ///
    /// Nothing is reversed because nothing was posted. The bookings go back to
    /// the retry ladder of docs/07 §12.5 and the debt that was recovered against
    /// this transfer is handed back to the host's balance, or the platform would
    /// collect it twice on the retry.
    /// </summary>
    public async Task<bool> FailAsync(long batchId, string actor, string? note, CancellationToken ct)
    {
        var batch = await db.PayoutBatches
            .Include(b => b.Host!).ThenInclude(h => h.User)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct);

        if (batch is null || batch.Status is PayoutBatchStatus.Settled or PayoutBatchStatus.Failed)
            return false;

        var payments = await db.Payments
            .Where(p => p.PayoutReference == batch.Reference)
            .ToListAsync(ct);

        foreach (var payment in payments)
        {
            payment.PayoutStatus = PayoutStatus.Scheduled;
            payment.PayoutReference = null;
            payment.PayoutDeducted = 0;
        }

        foreach (var ticket in await db.ExperienceBookings
                     .Where(b => b.PayoutReference == batch.Reference).ToListAsync(ct))
        {
            ticket.PayoutStatus = PayoutStatus.Scheduled;
            ticket.PayoutReference = null;
        }

        foreach (var job in await db.ServiceBookings
                     .Where(b => b.PayoutReference == batch.Reference).ToListAsync(ct))
        {
            job.PayoutStatus = PayoutStatus.Scheduled;
            job.PayoutReference = null;
        }

        // docs/02 G8 — a refused transfer of co-host shares goes back in the
        // queue whole. Nothing was posted, so there is nothing to reverse.
        foreach (var share in await db.CoHostPayouts
                     .Where(p => p.PayoutReference == batch.Reference).ToListAsync(ct))
        {
            share.Status = PayoutStatus.Scheduled;
            share.PayoutReference = null;
            share.Deducted = 0m;
        }

        if (batch.Deducted > 0 && batch.Host is not null)
            batch.Host.OwedToPlatform += batch.Deducted;

        batch.Status = PayoutBatchStatus.Failed;
        batch.SettledAt = DateTime.UtcNow;
        batch.SettledBy = actor;
        if (!string.IsNullOrWhiteSpace(note)) batch.Note = note.Trim();

        await notifications.QueueWithEmailAsync(batch.Host?.User, NotificationKind.PayoutSent,
            "Chuyển tiền không thành công", PayoutFiles.RefusedNotice(batch.Reference), "/hosting", ct);

        if (payments.Any(p => Payouts.OutOfAttempts(p.PayoutAttempts)))
        {
            await notifications.QueueWithEmailAsync(batch.Host?.User, NotificationKind.PayoutSent,
                "Không chuyển được tiền cho bạn", Payouts.ExhaustedNotice(), "/hosting", ct);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>docs/07 §12.4 — the five questions, answered from live data.</summary>
    private async Task<Payouts.Conditions> ConditionsAsync(
        Booking booking, HostProfile host, decimal payable, CancellationToken ct)
    {
        // Still running, either way: a case that has been ruled on or dropped
        // has stopped being a reason to sit on somebody's money.
        var disputed = await db.ResolutionCases.AnyAsync(
            c => c.BookingId == booking.Id
                 && (c.Status == ResolutionStatus.AwaitingResponse || c.Status == ResolutionStatus.Disputed), ct);

        if (!disputed)
        {
            disputed = await db.ShieldClaims.AnyAsync(
                c => c.BookingId == booking.Id
                     && (c.Status == ShieldStatus.Open
                         || c.Status == ShieldStatus.UnderReview
                         || c.Status == ShieldStatus.Appealed), ct);
        }

        var chargeback = await db.Chargebacks
            .Where(c => c.BookingId == booking.Id)
            .Select(c => c.Status)
            .ToListAsync(ct);

        return new Payouts.Conditions(
            HasOpenDispute: disputed,
            HasChargeback: chargeback.Any(Chargebacks.HoldsPayout),
            ListingSuspended: booking.Listing is { IsPublished: false },
            AccountVerified: host.PayoutAccountVerified,
            AccountChangedAt: host.PayoutAccountChangedAt,
            OwedToPlatform: host.OwedToPlatform,
            Payable: payable,
            // docs/08 §5.2/§6 — read from the account itself every sweep, so an
            // admin hold is never undone by this recomputation.
            AccountUnderReview: host.User is { } hu
                && (hu.IsSuspended || hu.IsBanned
                    || Restrictions.Has(hu.RestrictionMask, RestrictionKind.PayoutsHeld)));
    }

    /// <summary>
    /// docs/09 §4 (MR-C-03, scenario 12) — pays experience hosts and service
    /// providers a day after their session ENDS (not after it starts, the way a
    /// stay pays from check-in). Their money already sits in HostPayable from the
    /// capture; this releases the cash. Only confirmed/completed bookings reach
    /// here, so a refund and a payout can never both fire on one booking.
    /// </summary>
    public async Task<Result> SweepSessionsAsync(CancellationToken ct, DateTime? asOf = null)
    {
        var now = asOf ?? DateTime.UtcNow;
        int paid = 0, held = 0, failed = 0;

        var experiences = await db.ExperienceBookings
            .Include(b => b.Slot!).ThenInclude(s => s.Experience!).ThenInclude(x => x.Host!).ThenInclude(h => h.User)
            .Where(b => b.PayoutStatus == PayoutStatus.Scheduled
                && (b.Status == ExperienceBookingStatus.Confirmed || b.Status == ExperienceBookingStatus.Completed)
                && b.Slot!.StartsAt <= now)
            .Take(200)
            .ToListAsync(ct);

        foreach (var b in experiences)
        {
            var exp = b.Slot!.Experience!;
            if (!Payouts.SessionPayoutReady(b.Slot!.StartsAt, exp.DurationMinutes, now)) continue;

            var reference = $"XP{b.Id:D6}";

            switch (Queue(exp.Host, b.HostPayout, reference, $"Trải nghiệm · đơn {b.Reference}", now))
            {
                case PayResult.Nothing:
                    b.PayoutStatus = PayoutStatus.Paid;
                    b.PaidOutAt = now;
                    b.PayoutReference = reference;
                    paid++;
                    break;
                case PayResult.Queued:
                    b.PayoutStatus = PayoutStatus.Sent;
                    b.PayoutReference = reference;
                    paid++;
                    if (exp.Host?.User is { } u)
                        await notifications.QueueWithEmailAsync(u, NotificationKind.PayoutSent,
                            "Đã lên lệnh chuyển tiền trải nghiệm",
                            PayoutFiles.QueuedNotice(b.HostPayout, $"đơn {b.Reference}", reference),
                            "/hosting?tab=earnings", ct);
                    break;
                case PayResult.Held: held++; break;
                default: failed++; break;
            }
        }

        var services = await db.ServiceBookings
            .Include(b => b.Offering!).ThenInclude(o => o.Host!).ThenInclude(h => h.User)
            .Where(b => b.PayoutStatus == PayoutStatus.Scheduled
                && (b.Status == ServiceBookingStatus.Confirmed || b.Status == ServiceBookingStatus.Completed)
                && b.StartsAt <= now)
            .Take(200)
            .ToListAsync(ct);

        foreach (var b in services)
        {
            if (!Payouts.SessionPayoutReady(b.StartsAt, b.DurationMinutes, now)) continue;

            var reference = $"SV{b.Id:D6}";

            switch (Queue(b.Offering!.Host, b.ProviderPayout, reference, $"Dịch vụ · đơn {b.Reference}", now))
            {
                case PayResult.Nothing:
                    b.PayoutStatus = PayoutStatus.Paid;
                    b.PaidOutAt = now;
                    b.PayoutReference = reference;
                    paid++;
                    break;
                case PayResult.Queued:
                    b.PayoutStatus = PayoutStatus.Sent;
                    b.PayoutReference = reference;
                    paid++;
                    if (b.Offering!.Host?.User is { } u)
                        await notifications.QueueWithEmailAsync(u, NotificationKind.PayoutSent,
                            "Đã lên lệnh chuyển tiền dịch vụ",
                            PayoutFiles.QueuedNotice(b.ProviderPayout, $"đơn {b.Reference}", reference),
                            "/hosting?tab=earnings", ct);
                    break;
                case PayResult.Held: held++; break;
                default: failed++; break;
            }
        }

        if (paid + held + failed > 0)
        {
            await db.SaveChangesAsync(ct);
            log.LogInformation("Trả buổi {Now:d}: {Paid} đã chuyển, {Held} giữ, {Failed} lỗi.", now, paid, held, failed);
        }

        return new Result(paid, held, failed);
    }

    private enum PayResult
    {
        /// <summary>Nothing was owed, so there is nothing to transfer and nothing to wait for.</summary>
        Nothing,
        /// <summary>Lined up in a batch. The ledger waits for a bank (docs/07 §13).</summary>
        Queued,
        Held,
        Failed
    }

    /// <summary>
    /// Writes down one transfer to a provider, or says why it cannot be written.
    ///
    /// It posts nothing to the ledger. That used to happen here, on the word of
    /// the stand-in gateway; it now happens in <see cref="SettleAsync"/>, when a
    /// person has seen a bank execute the file.
    /// </summary>
    private PayResult Queue(HostProfile? host, decimal amount, string reference, string note, DateTime now)
    {
        if (amount <= 0) return PayResult.Nothing;
        if (host is null) return PayResult.Held;

        var accountNumber = accounts.Open(host.PayoutAccountSealed);
        if (PayoutFiles.Missing(host.PayoutBankName, host.PayoutAccountName, accountNumber) is not null)
            return PayResult.Held;

        db.PayoutBatches.Add(new PayoutBatch
        {
            Reference = reference,
            HostId = host.Id,
            Amount = amount,
            BookingCount = 1,
            BankName = host.PayoutBankName ?? "",
            AccountName = host.PayoutAccountName ?? "",
            AccountNumber = accountNumber!,
            DueOn = DateOnly.FromDateTime(now),
            Note = note
        });

        return PayResult.Queued;
    }
}
