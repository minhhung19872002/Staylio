using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/07 §7, §11 and §15 — the finance desk: the daily reconciliation, looking
/// a transaction up, the bank-dispute queue, and the four numbers TC-A-04 asks
/// for.
///
/// Everything here needs <see cref="AdminScope.Finance"/> and leaves an audit
/// row, because everything here can move somebody's money.
/// </summary>
[ApiController]
[Route("api/admin/finance")]
public class FinanceController(
    StayHostDbContext db, AdminAudit audit, AdminGate gate, PaymentGateway gateway,
    NotificationService notifications, RefundGateway refunds,
    Services.Gateways.GatewayStatement statements)
    : ControllerBase
{
    private Task<User?> RequireAsync(CancellationToken ct) => audit.RequireAsync(AdminScope.Finance, ct);

    private ActionResult Refuse(AdminGate.Verdict v) =>
        StatusCode(v.Status ?? 403, new { message = v.Refusal });

    /// <summary>
    /// docs/08 §1.3 — a refund or a hold has two people on it, and the admin must
    /// be related to neither. The gate checks one target; the other side is
    /// checked here so "I refunded my own stay" cannot slip through as the host.
    /// </summary>
    private async Task<string?> OtherPartyConflictAsync(User admin, int? otherUserId, CancellationToken ct)
    {
        if (otherUserId is not { } other) return null;
        var conflict = await gate.ConflictAsync(admin, other, ct);
        return AdminConflict.Blocks(conflict) ? AdminConflict.Message(conflict) : null;
    }

    /* ------------------------------------------------- TC-A-04, the report */

    /// <summary>
    /// docs/07 §15 TC-A-04 — "doanh thu phí, tiền đang giữ hộ, thuế phải nộp,
    /// thất thoát", read off the ledger. The ledger is the only place these can
    /// be read from honestly: it is the record that has to balance.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<FinanceReportDto>> Report(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var admin = await RequireAsync(ct);
        if (admin is null) return this.Denied();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? today.AddDays(-30);
        var end = to ?? today;

        var fromUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await db.LedgerEntries
            .Where(e => e.CreatedAt >= fromUtc && e.CreatedAt < toUtc)
            .Select(e => new { e.Account, e.Direction, e.Amount })
            .ToListAsync(ct);

        // A credit balance on a revenue account is revenue; a debit against it is
        // revenue given back. Netting them is the only figure worth reporting.
        decimal Net(LedgerAccount account) =>
            rows.Where(r => r.Account == account)
                .Sum(r => r.Direction == LedgerDirection.Credit ? r.Amount : -r.Amount);

        var guestFees = Net(LedgerAccount.GuestServiceFeeRevenue);
        var hostFees = Net(LedgerAccount.HostServiceFeeRevenue);

        // "Tiền đang giữ hộ" — money on the books that belongs to somebody else.
        // It is a running balance, not a period figure, so it is read whole.
        var all = await db.LedgerEntries
            .Select(e => new { e.Account, e.Direction, e.Amount })
            .ToListAsync(ct);

        decimal Balance(LedgerAccount account) =>
            all.Where(r => r.Account == account)
               .Sum(r => r.Direction == LedgerDirection.Credit ? r.Amount : -r.Amount);

        var lines = new List<FinanceLineDto>
        {
            new("guest-fee", "Phí dịch vụ thu của khách", guestFees, "Doanh thu"),
            new("host-fee", "Phí dịch vụ thu của chủ nhà", hostFees, "Doanh thu"),
            new("held-host", "Đang giữ hộ chủ nhà", Balance(LedgerAccount.HostPayable), "Giữ hộ"),
            new("held-refund", "Đang chờ hoàn khách", Balance(LedgerAccount.GuestRefundPayable), "Giữ hộ"),
            new("held-third-party", "Đang giữ hộ bên thứ ba", Balance(LedgerAccount.ThirdPartyPayable), "Giữ hộ"),
            new("shield-fund", "Quỹ Staylio Shield", Balance(LedgerAccount.ShieldFund), "Giữ hộ"),
            new("credit", "Số dư khuyến mãi khách đang giữ", Balance(LedgerAccount.PromotionalCredit), "Giữ hộ"),
            new("tax", "Thuế thu hộ, phải nộp", Balance(LedgerAccount.TaxPayable), "Thuế"),
            new("expense", "Chi phí nền tảng và thất thoát", -Net(LedgerAccount.PlatformExpense), "Thất thoát"),
            new("receivable", "Khách còn nợ chưa thu được", Balance(LedgerAccount.GuestReceivable), "Thất thoát")
        };

        // docs/07 §11 — a lost chargeback is a real loss, and it is not visible
        // anywhere in the ledger accounts above.
        var lostToBanks = await db.Chargebacks
            .Where(c => (c.Status == ChargebackStatus.Lost || c.Status == ChargebackStatus.Expired)
                        && c.ReceivedAt >= fromUtc && c.ReceivedAt < toUtc)
            .SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;

        lines.Add(new FinanceLineDto("chargeback-loss", "Thua khiếu nại ngân hàng", lostToBanks, "Thất thoát"));

        var balanced = all.Sum(r => r.Direction == LedgerDirection.Credit ? r.Amount : -r.Amount);

        return Ok(new FinanceReportDto(
            start, end,
            FeeRevenue: guestFees + hostFees,
            HeldForOthers: Balance(LedgerAccount.HostPayable) + Balance(LedgerAccount.GuestRefundPayable)
                           + Balance(LedgerAccount.ThirdPartyPayable) + Balance(LedgerAccount.ShieldFund),
            TaxPayable: Balance(LedgerAccount.TaxPayable),
            Losses: -Net(LedgerAccount.PlatformExpense) + lostToBanks,
            LedgerDifference: balanced,
            Lines: lines));
    }

    /* --------------------------------------------- TC-A-01, reconciliation */

    /// <summary>
    /// docs/07 §7 — "so danh sách giao dịch của sàn với danh sách của cổng thanh
    /// toán. Lệch một giao dịch là báo động."
    /// </summary>
    [HttpGet("reconciliation")]
    public async Task<ActionResult<ReconciliationDto>> Reconcile([FromQuery] DateOnly? day, CancellationToken ct)
    {
        var admin = await RequireAsync(ct);
        if (admin is null) return this.Denied();

        var on = day ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var fromUtc = on.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = fromUtc.AddDays(1);

        // The platform's side: every attempt it believes took money that day.
        var ours = await db.PaymentAttempts
            .Where(a => a.Status == PaymentAttemptStatus.Succeeded
                        && a.CompletedAt >= fromUtc && a.CompletedAt < toUtc)
            .Select(a => new Reconciliation.Record(a.Key, a.Amount))
            .ToListAsync(ct);

        // Gateway visits with no attempt row are money the platform took too: a
        // gift card, a ticket, a service, a split share, the rest of a deposit.
        // Leaving them out showed every one of them as money the gateway had and
        // the platform did not.
        var known = ours.Select(r => r.Reference).ToHashSet();
        var visits = await db.PaymentSessions
            .Where(s => s.Status == PaymentSessionStatus.Paid && s.CompletedAt >= fromUtc && s.CompletedAt < toUtc)
            .Select(s => new Reconciliation.Record(s.AttemptKey, s.Amount))
            .ToListAsync(ct);
        ours.AddRange(visits.Where(v => known.Add(v.Reference)));

        // docs/07 §7 — their list, from them. This used to read the same table
        // the line above reads, so the report compared one of our records against
        // another of our records and balanced every day by construction.
        var theirs = await statements.ForAsync(on, ct);

        var report = Reconciliation.Compare(on, ours, theirs);

        return Ok(new ReconciliationDto(
            report.Day, report.Balanced, report.OursCount, report.TheirsCount,
            report.OursTotal, report.TheirsTotal, report.Difference,
            Reconciliation.Summary(report),
            report.Discrepancies
                .Select(d => new DiscrepancyDto(
                    d.Kind.ToString(), Reconciliation.KindLabel(d.Kind),
                    d.Reference, d.Ours, d.Theirs, d.Difference))
                .ToList()));
    }

    /* ------------------------------------------- TC-A-02, one transaction */

    /// <summary>
    /// Looks a payment up by booking reference, payment reference or the guest's
    /// email — the three things somebody has in front of them when they call.
    /// </summary>
    [HttpGet("transactions")]
    public async Task<ActionResult<IReadOnlyList<TransactionDto>>> Transactions(
        [FromQuery] string? q, CancellationToken ct)
    {
        var admin = await RequireAsync(ct);
        if (admin is null) return this.Denied();

        var query = db.Bookings
            .Include(b => b.Payment)
            .Include(b => b.Listing)
            .Include(b => b.GuestUser)
            .AsQueryable();

        var term = (q ?? "").Trim();
        if (term.Length > 0)
        {
            query = query.Where(b =>
                b.Reference.ToLower().Contains(term.ToLower())
                || (b.Payment != null && b.Payment.Reference.ToLower().Contains(term.ToLower()))
                || (b.GuestEmail != null && b.GuestEmail.ToLower().Contains(term.ToLower())));
        }

        var rows = await query
            .Where(b => b.Payment != null)
            .OrderByDescending(b => b.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return Ok(rows.Select(ToDto).ToList());
    }

    /// <summary>
    /// docs/07 §15 TC-A-02 — a manual refund, for the cases the automatic rules
    /// do not cover. It goes through the same ledger postings as any other
    /// refund, so it cannot put the books out; the difference is only that a
    /// person decided it, and that person's name is on it.
    /// </summary>
    [HttpPost("transactions/{bookingId:int}/refund")]
    public async Task<ActionResult<TransactionDto>> Refund(
        int bookingId, [FromBody] ManualRefundRequest req, CancellationToken ct)
    {
        var booking = await db.Bookings
            .Include(b => b.Payment).Include(b => b.Listing!).ThenInclude(l => l.Host)
            .Include(b => b.GuestUser)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking?.Payment is null) return NotFound(new { message = "Không tìm thấy giao dịch." });

        // docs/08 §2 and §1 — through the gate: the matrix row, the 10-character
        // reason, and the §1.3 conflict check against BOTH parties to the money.
        var v = await gate.AllowAsync(AdminAction.ManualRefund, req.Reason, ct, booking.GuestUserId);
        if (!v.Ok) return Refuse(v);

        if (await OtherPartyConflictAsync(v.Admin!, booking.Listing?.Host?.UserId, ct) is { } hostConflict)
            return StatusCode(403, new { message = hostConflict });

        var admin = v.Admin!;
        var reason = req.Reason!.Trim();

        // What was actually taken. DepositPaid carries it on every path that goes
        // through the pay endpoint; a captured payment that never set it was
        // charged its own amount, and refusing to refund a real stay over a
        // bookkeeping gap would be the wrong way round.
        var taken = booking.DepositPaid > 0
            ? booking.DepositPaid
            : booking.Payment.Status == PaymentStatus.Captured ? booking.Payment.Amount : 0m;

        // Balance is taken off the price before the card is charged (docs/07 §3),
        // so DepositPaid is already the money alone and the balance sits beside it.
        // Subtracting it again made a stay paid half in balance refundable in
        // money only up to the difference.
        var left = taken + booking.CreditUsed - booking.RefundedAmount;
        if (req.Amount <= 0 || req.Amount > left)
            return BadRequest(new { message = $"Số tiền hoàn phải trong khoảng 1₫ – {left:#,##0}₫." });

        // docs/08 §10 — above the threshold, one signature is not enough. The
        // request is parked as a row rather than refused, so the second person
        // has something to act on instead of the first having to ask around.
        if (AdminOversight.NeedsSecondApproval(req.Amount))
        {
            var already = await db.MoneyApprovals.FirstOrDefaultAsync(
                m => m.Target == $"booking:{booking.Reference}"
                     && m.Action == "finance.refund"
                     && m.Amount == req.Amount
                     && m.ExecutedAt == null, ct);

            if (already is null)
            {
                db.MoneyApprovals.Add(new MoneyApproval
                {
                    Action = "finance.refund",
                    Target = $"booking:{booking.Reference}",
                    Amount = req.Amount,
                    Reason = reason,
                    RequestedByUserId = admin.Id
                });
                await db.SaveChangesAsync(ct);

                return StatusCode(202, new
                {
                    message = AdminOversight.SecondApprovalMessage(req.Amount),
                    needsSecondApproval = true
                });
            }

            if (already.IsOpen)
                return StatusCode(202, new { message = "Khoản này đang chờ người thứ hai duyệt.", needsSecondApproval = true });

            if (already.RejectedAt is not null)
                return BadRequest(new { message = $"Khoản này đã bị từ chối: {already.RejectedReason}" });

            already.ExecutedAt = DateTime.UtcNow;
        }

        // docs/07 §10 — back the way it came, card before balance.
        var split = Refunds.Allocate(
            new Refunds.Sources(taken, booking.CreditUsed),
            req.Amount, booking.RefundedAmount);

        // The card part goes through the gateway that took it. This used to write
        // "refund settled" to the books with no call to anybody, so the guest was
        // told money was coming that no bank had been asked to send.
        var sent = await refunds.SendForAsync(
            s => s.BookingId == booking.Id, split.ToCard, booking.Reference,
            booking.Payment.Method, booking.Payment.CardLast4, $"admin:{admin.Id}", ct);
        var toCard = Math.Min(split.ToCard, sent.ToCard);
        var asBalance = split.ToCredit + (split.ToCard - toCard);

        // Owed first, then paid — otherwise the refund account goes negative and
        // the report reads as if the platform were owed money by its own guests.
        db.LedgerEntries.AddRange(Ledger.ManualRefund(booking, req.Amount, DateTime.UtcNow));
        if (toCard > 0)
            db.LedgerEntries.AddRange(Ledger.SettleRefund(booking, toCard, DateTime.UtcNow));

        if (asBalance > 0)
        {
            db.LedgerEntries.AddRange(Ledger.SettleRefundAsCredit(booking, asBalance, DateTime.UtcNow));

            // The books alone are not balance: the wallet reads CreditEntries, and
            // without this row the guest was told of balance they could not spend.
            if (booking.GuestUserId is { } owner)
                db.CreditEntries.Add(CreditLedger.Grant(
                    owner, asBalance, CreditReason.Returned,
                    $"Hoàn tiền đơn {booking.Reference}", DateTime.UtcNow, booking.Id));
        }

        split = new Refunds.Split(toCard, asBalance, split.Unrefundable);

        booking.RefundedAmount += req.Amount;

        audit.Record(admin, "finance.refund", $"booking:{booking.Reference}",
            $"đã hoàn {booking.RefundedAmount - req.Amount:#,##0}₫",
            $"đã hoàn {booking.RefundedAmount:#,##0}₫", reason);

        await db.SaveChangesAsync(ct);

        await notifications.QueueWithEmailAsync(booking.GuestUser, NotificationKind.RefundIssued,
            "Staylio đã hoàn tiền cho bạn",
            $"{req.Amount:#,##0}₫ cho đơn {booking.Reference}. {Refunds.TimingNotice(split)}",
            $"/trips/{booking.Id}", ct);

        return Ok(await OneAsync(booking.Id, ct));
    }

    /// <summary>
    /// docs/03 §4 and docs/06 §8 — an admin recognises force majeure on a
    /// booking: a typhoon, a flood, an order closing the area.
    ///
    /// Nothing could reach this before. <c>Cancellation</c> has had a
    /// force-majeure pre-rule since it was written and <c>CancelledBy</c> has had
    /// the value, but every call site passed Host, Guest or Platform, so the
    /// branch was unreachable and so was Q-A behind it. The guest is refunded in
    /// full and the host is paid Q-A of the booking out of the fund, neither of
    /// which anybody has to file for.
    /// </summary>
    [HttpPost("bookings/{id:int}/force-majeure")]
    public async Task<ActionResult<TransactionDto>> ForceMajeure(
        int id, [FromBody] ForceMajeureRequest req, CancellationToken ct)
    {
        var admin = await RequireAsync(ct);
        if (admin is null) return this.Denied();

        var reason = (req.Reason ?? "").Trim();
        if (reason.Length < 8)
            return BadRequest(new { message = "Ghi rõ sự kiện bất khả kháng (tối thiểu 8 ký tự)." });

        var booking = await db.Bookings
            .Include(b => b.Payment).Include(b => b.GuestUser)
            .Include(b => b.Listing!).ThenInclude(l => l.Host)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();

        // The fund pays the host Q-A here, so a finance admin who hosts this
        // stay — or stayed in it — must not be the one to call it.
        if (await gate.PartyConflictAsync(admin, booking.GuestUserId, booking.Listing?.Host?.UserId, ct) is { } refusal)
            return StatusCode(403, new { message = refusal });

        // Force majeure lands on the host's side of the ledger (docs/03 §4), so
        // it has to be a legal move to CancelledByHost — the same gate the host's
        // own cancel button passes through.
        if (!BookingLifecycle.CanTransition(booking.Status, BookingStatus.CancelledByHost))
            return BadRequest(new
            {
                message = $"Đơn đang ở trạng thái \"{BookingLifecycle.Label(booking.Status)}\" nên không huỷ được."
            });

        var outcome = Cancellation.Refund(new Cancellation.Context
        {
            Booking = booking,
            Now = DateTime.UtcNow,
            By = CancelledBy.ForceMajeure
        });

        // docs/07 §10 — ask the gateway before deciding where the money lands.
        // This used to default to "the card took it" without asking anything,
        // which was harmless until a real gateway held the money.
        var sentBack = await refunds.SendAsync(
            booking, outcome.Amount, $"admin:{admin.Id}", reason, ct);

        BookingsController.PostCancellation(
            db, booking, outcome, CancelledBy.ForceMajeure, $"Bất khả kháng: {reason}", sentBack);

        audit.Record(admin, "finance.force-majeure", $"booking:{booking.Reference}",
            BookingLifecycle.Label(BookingStatus.Confirmed),
            BookingLifecycle.Label(booking.Status), reason);

        await db.SaveChangesAsync(ct);

        if (booking.GuestUser is not null)
            await notifications.QueueWithEmailAsync(booking.GuestUser, NotificationKind.RefundIssued,
                "Chuyến đi bị huỷ vì bất khả kháng",
                $"Đơn {booking.Reference} đã được huỷ và hoàn 100%. Lý do: {reason}.",
                $"/trips/{booking.Id}", ct);

        // docs/06 §8 — the host is told what they are owed and why, because they
        // did nothing wrong and nobody asked them.
        var hostUserId = booking.Listing?.Host?.UserId;
        var award = Shield.ForceMajeureHostAward(booking.Total);
        if (hostUserId is { } hostId && award > 0
            && await db.Users.FirstOrDefaultAsync(u => u.Id == hostId, ct) is { } hostUser)
        {
            await notifications.QueueWithEmailAsync(hostUser, NotificationKind.System,
                "Đền bù bất khả kháng",
                $"Đơn {booking.Reference} bị huỷ vì {reason}. Bạn được đền bù " +
                $"{award:#,##0}₫ từ quỹ Staylio Shield, không cần mở hồ sơ.",
                "/hosting", ct);
        }

        return Ok(await OneAsync(booking.Id, ct));
    }

    /// <summary>
    /// docs/07 §15 TC-A-02 — "điều chỉnh khoản chuyển". Lifts a hold, or puts one
    /// on, on a host's payout. It never changes the amount: that is what the
    /// host earned, and moving it would take the ledger out of step.
    /// </summary>
    [HttpPost("payouts/{bookingId:int}/adjust")]
    public async Task<ActionResult<TransactionDto>> AdjustPayout(
        int bookingId, [FromBody] AdjustPayoutRequest req, CancellationToken ct)
    {
        var payment = await db.Payments
            .Include(p => p.Booking!).ThenInclude(b => b.Listing!).ThenInclude(l => l.Host!).ThenInclude(h => h.User)
            .FirstOrDefaultAsync(p => p.BookingId == bookingId, ct);

        if (payment is null) return NotFound(new { message = "Không tìm thấy khoản chuyển." });

        if (payment.PayoutStatus == PayoutStatus.Paid)
            return BadRequest(new { message = "Khoản này đã chuyển, không điều chỉnh được nữa." });

        var hostUserId = payment.Booking?.Listing?.Host?.UserId;

        var v = await gate.AllowAsync(AdminAction.AdjustPayout, req.Reason, ct, hostUserId);
        if (!v.Ok) return Refuse(v);

        if (await OtherPartyConflictAsync(v.Admin!, payment.Booking?.GuestUserId, ct) is { } guestConflict)
            return StatusCode(403, new { message = guestConflict });

        var admin = v.Admin!;
        var reason = req.Reason!.Trim();

        var before = payment.PayoutStatus == PayoutStatus.OnHold
            ? Payouts.HoldLabel(payment.PayoutHoldReason)
            : "chờ chuyển";

        if (req.Release)
        {
            // docs/08 §10 QT-E — releasing held money IS moving money. Above the
            // threshold it waits for a second signature like a refund would.
            if (AdminOversight.NeedsSecondApproval(payment.HostPayout))
            {
                var target = $"booking:{payment.Booking!.Reference}";

                var parked = await db.MoneyApprovals.FirstOrDefaultAsync(
                    m => m.Target == target && m.Action == "finance.payout-adjust"
                         && m.Amount == payment.HostPayout && m.ExecutedAt == null, ct);

                if (parked is null)
                {
                    db.MoneyApprovals.Add(new MoneyApproval
                    {
                        Action = "finance.payout-adjust",
                        Target = target,
                        Amount = payment.HostPayout,
                        Reason = reason,
                        RequestedByUserId = admin.Id
                    });
                    await db.SaveChangesAsync(ct);

                    return StatusCode(202, new
                    {
                        message = AdminOversight.SecondApprovalMessage(payment.HostPayout),
                        needsSecondApproval = true
                    });
                }

                if (parked.IsOpen)
                    return StatusCode(202, new { message = "Khoản này đang chờ người thứ hai duyệt.", needsSecondApproval = true });

                if (parked.RejectedAt is not null)
                    return BadRequest(new { message = $"Khoản này đã bị từ chối: {parked.RejectedReason}" });

                parked.ExecutedAt = DateTime.UtcNow;
            }

            payment.PayoutStatus = PayoutStatus.Scheduled;
            payment.PayoutHoldReason = PayoutHoldReason.None;
            // A released payout goes out on the next sweep, not in three days'
            // time, so the attempt counter starts again.
            payment.PayoutAttempts = 0;
            payment.PayoutLastAttemptOn = null;
        }
        else
        {
            payment.PayoutStatus = PayoutStatus.OnHold;
            payment.PayoutHoldReason = PayoutHoldReason.Dispute;
        }

        audit.Record(admin, "finance.payout-adjust", $"booking:{payment.Booking!.Reference}",
            before, req.Release ? "chờ chuyển" : Payouts.HoldLabel(PayoutHoldReason.Dispute), reason);

        await db.SaveChangesAsync(ct);

        await notifications.QueueWithEmailAsync(
            payment.Booking.Listing?.Host?.User, NotificationKind.PayoutSent,
            req.Release ? "Khoản chuyển tiền đã được mở lại" : "Khoản chuyển tiền đang tạm giữ",
            $"Đơn {payment.Booking.Reference}: {reason}", "/hosting", ct);

        return Ok(await OneAsync(bookingId, ct));
    }

    /* ---------------------------------------------- TC-P-12, the bank disputes */

    [HttpGet("chargebacks")]
    public async Task<ActionResult<IReadOnlyList<ChargebackDto>>> Chargebacks(CancellationToken ct)
    {
        var admin = await RequireAsync(ct);
        if (admin is null) return this.Denied();

        var now = DateTime.UtcNow;

        var rows = await db.Chargebacks
            .Include(c => c.Booking!).ThenInclude(b => b.Listing)
            .OrderByDescending(c => c.ReceivedAt)
            .Take(60)
            .ToListAsync(ct);

        return Ok(rows.Select(c => new ChargebackDto(
            c.Id,
            c.Booking?.Reference ?? "",
            c.Booking?.Listing?.Title ?? "",
            c.Amount,
            c.Reason,
            c.Status.ToString(),
            Domain.Chargebacks.StatusLabel(c.Status),
            c.ReceivedAt,
            Domain.Chargebacks.EvidenceDueBy(c.ReceivedAt),
            Domain.Chargebacks.EvidenceOverdue(c, now),
            c.Evidence,
            c.HostAtFault,
            Domain.Chargebacks.EvidenceChecklist)).ToList());
    }

    /// <summary>
    /// docs/07 §11 step 1 — the bank has taken the money back. Opening the case
    /// is what puts the host's payout on hold, so it happens here rather than
    /// being noticed later.
    /// </summary>
    [HttpPost("chargebacks")]
    public async Task<ActionResult<IReadOnlyList<ChargebackDto>>> OpenChargeback(
        [FromBody] OpenChargebackRequest req, CancellationToken ct)
    {
        var admin = await RequireAsync(ct);
        if (admin is null) return this.Denied();

        var booking = await db.Bookings
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.Reference == req.BookingReference, ct);

        if (booking is null) return NotFound(new { message = "Không tìm thấy đơn." });

        var already = await db.Chargebacks.AnyAsync(
            c => c.BookingId == booking.Id
                 && (c.Status == ChargebackStatus.Received || c.Status == ChargebackStatus.Contested), ct);

        if (already) return BadRequest(new { message = "Đơn này đã có hồ sơ khiếu nại đang mở." });

        db.Chargebacks.Add(new Chargeback
        {
            BookingId = booking.Id,
            Amount = req.Amount > 0 ? req.Amount : booking.DepositPaid,
            Reason = (req.Reason ?? "").Trim()
        });

        audit.Record(admin, "finance.chargeback-open", $"booking:{booking.Reference}",
            null, $"{req.Amount:#,##0}₫", req.Reason);

        await db.SaveChangesAsync(ct);
        return await Chargebacks(ct);
    }

    /// <summary>docs/07 §11 steps 3–4 — the evidence pack, inside seven days.</summary>
    [HttpPost("chargebacks/{id:int}/evidence")]
    public async Task<ActionResult<IReadOnlyList<ChargebackDto>>> Contest(
        int id, [FromBody] ChargebackEvidenceRequest req, CancellationToken ct)
    {
        var admin = await RequireAsync(ct);
        if (admin is null) return this.Denied();

        var c = await db.Chargebacks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();

        var evidence = (req.Evidence ?? "").Trim();
        if (evidence.Length < 10) return BadRequest(new { message = "Cần mô tả bằng chứng đã gửi." });

        c.Evidence = evidence;
        c.Status = ChargebackStatus.Contested;
        c.RespondedAt = DateTime.UtcNow;

        audit.Record(admin, "finance.chargeback-contest", $"chargeback:{c.Id}", null, "đã gửi bằng chứng", evidence);

        await db.SaveChangesAsync(ct);
        return await Chargebacks(ct);
    }

    /// <summary>
    /// docs/07 §11 step 5 — the bank's ruling. Who wears the loss follows from
    /// <c>HostAtFault</c> and nothing else: "Chủ nhà không bị mất tiền vì khiếu
    /// nại của khách, trừ khi phân xử cho thấy lỗi thuộc về chủ nhà."
    /// </summary>
    [HttpPost("chargebacks/{id:int}/decide")]
    public async Task<ActionResult<IReadOnlyList<ChargebackDto>>> Decide(
        int id, [FromBody] DecideChargebackRequest req, CancellationToken ct)
    {
        var admin = await RequireAsync(ct);
        if (admin is null) return this.Denied();

        var c = await db.Chargebacks
            .Include(x => x.Booking!).ThenInclude(b => b.Listing!).ThenInclude(l => l.Host)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (c is null) return NotFound();

        c.Status = req.Won ? ChargebackStatus.Won : ChargebackStatus.Lost;
        c.HostAtFault = !req.Won && req.HostAtFault;
        c.DecidedAt = DateTime.UtcNow;

        // The loss lands on the host only when arbitration put it there; the
        // platform wears it otherwise.
        if (Domain.Chargebacks.HostBearsLoss(c) && c.Booking?.Listing?.Host is { } host)
        {
            host.OwedToPlatform += c.Amount;
        }

        /*
         * docs/07 §11 step 6 — "Tài khoản khách có nhiều lần khiếu nại vô căn cứ
         * → gắn cờ, yêu cầu xác minh cho các đơn sau."
         *
         * Chargebacks.GuestNeedsWatching had held the threshold since the rule
         * was written and had never been called from anywhere, so however many
         * times an account went to its bank and was found wrong, nothing
         * followed. A loss the host was at fault for is not the guest's pattern
         * and is left out of the count.
         */
        if (!req.Won && !c.HostAtFault && c.Booking?.GuestUserId is { } guestId)
        {
            // Every *other* one, plus the one being decided right now. Counting
            // straight from the table would miss it: the status above is still
            // only in memory until SaveChangesAsync below, so two disputes in a
            // row each counted one and the threshold of two was never reached.
            var lost = 1 + await db.Chargebacks
                .CountAsync(x => x.Id != c.Id
                                 && x.Booking!.GuestUserId == guestId
                                 && !x.HostAtFault
                                 && (x.Status == ChargebackStatus.Lost
                                     || x.Status == ChargebackStatus.Expired), ct);

            var already = await db.RiskFlags.AnyAsync(
                f => f.UserId == guestId
                     && f.Kind == RiskKind.RepeatChargebacks
                     && f.Status == RiskFlagStatus.Open, ct);

            if (Domain.Chargebacks.GuestNeedsWatching(lost) && !already)
            {
                db.RiskFlags.Add(new RiskFlag
                {
                    UserId = guestId,
                    BookingId = c.BookingId,
                    Kind = RiskKind.RepeatChargebacks,
                    Severity = RiskSeverity.Review,
                    Summary = $"{lost} lần khiếu nại ngân hàng bị xử thua",
                    Detail = "Các đơn sau của tài khoản này cần xác minh danh tính trước khi đặt "
                             + "(docs/07 §11 bước 6)."
                });
            }
        }

        audit.Record(admin, "finance.chargeback-decide", $"chargeback:{c.Id}",
            Domain.Chargebacks.StatusLabel(ChargebackStatus.Contested),
            Domain.Chargebacks.StatusLabel(c.Status),
            c.HostAtFault ? "Lỗi thuộc về chủ nhà" : "Sàn chịu khoản này");

        await db.SaveChangesAsync(ct);
        return await Chargebacks(ct);
    }

    /* ------------------------------------------------------------- helpers */

    private async Task<TransactionDto?> OneAsync(int bookingId, CancellationToken ct)
    {
        var b = await db.Bookings
            .Include(x => x.Payment).Include(x => x.Listing).Include(x => x.GuestUser)
            .FirstOrDefaultAsync(x => x.Id == bookingId, ct);

        return b?.Payment is null ? null : ToDto(b);
    }

    /* ------------------------------------------- docs/07 §2.3, bank transfers */

    /// <summary>
    /// What the platform is waiting to be paid by transfer right now, so the
    /// desk can see whether a credit they are looking at belongs to anything.
    /// </summary>
    [HttpGet("bank-transfers")]
    public async Task<ActionResult<BankTransferDeskDto>> BankTransfers(
        [FromServices] BankTransferService transfers, CancellationToken ct)
    {
        var admin = await RequireAsync(ct);
        if (admin is null) return this.Denied();

        var awaited = await transfers.AwaitedAsync(ct);
        var open = await transfers.OpenAsync(ct);

        return Ok(new BankTransferDeskDto(
            awaited.Select(a => new AwaitedTransferDto(a.Key, a.Value)).OrderBy(a => a.Reference).ToList(),
            open.Select(c => new BankCreditDto(
                c.Id, c.BankReference, c.Amount, c.Description,
                c.Verdict.ToString(), Domain.BankTransfers.VerdictLabel(c.Verdict),
                c.MatchedReference, c.Expected, c.ImportedAt)).ToList()));
    }

    /// <summary>
    /// docs/07 §2.3 — a statement, read against the bookings waiting for money.
    ///
    /// The rows arrive already split into columns because bank exports disagree
    /// about column order, headings and decimal separators, and that mapping is
    /// something a person does once while looking at their own file. What must
    /// not be guessed at is here instead: which booking a memo belongs to, and
    /// whether this credit has been seen before.
    /// </summary>
    [HttpPost("bank-transfers/import")]
    public async Task<ActionResult<BankImportResultDto>> ImportStatement(
        [FromBody] ImportStatementRequest req,
        [FromServices] BankTransferService transfers,
        CancellationToken ct)
    {
        var admin = await RequireAsync(ct);
        if (admin is null) return this.Denied();

        if (req.Lines is not { Count: > 0 })
            return BadRequest(new { message = "Chưa có dòng nào để nhập." });

        if (req.Lines.Count > 1000)
            return BadRequest(new { message = "Mỗi lần nhập tối đa 1000 dòng." });

        var result = await transfers.ImportAsync(
            admin.Id,
            req.Lines.Select(l => new BankTransferService.Line(
                l.BankReference, l.Amount, l.Description)).ToList(),
            ct);

        audit.Record(admin, "finance.bank-import", $"lines:{req.Lines.Count}", null,
            $"{result.Settled} khớp đơn, {result.Pending} cần xử lý, {result.Skipped} đã nhập trước đó");
        await db.SaveChangesAsync(ct);

        return Ok(new BankImportResultDto(
            result.Settled, result.Pending, result.Skipped,
            result.Rows.Select(r => new BankImportRowDto(
                r.BankReference, r.Amount, r.Description,
                r.Verdict.ToString(), Domain.BankTransfers.VerdictLabel(r.Verdict),
                r.MatchedReference, r.Expected, r.Explanation)).ToList()));
    }

    /// <summary>
    /// A person has dealt with a credit the machine could not. What they did is
    /// kept on the row: it is the platform's only record of where that money went.
    /// </summary>
    [HttpPost("bank-transfers/{id:long}/resolve")]
    public async Task<ActionResult> ResolveCredit(
        long id, [FromBody] ResolveCreditRequest req,
        [FromServices] BankTransferService transfers,
        CancellationToken ct)
    {
        var admin = await RequireAsync(ct);
        if (admin is null) return this.Denied();

        if (string.IsNullOrWhiteSpace(req.Note))
            return BadRequest(new { message = "Cần ghi lại đã xử lý thế nào." });

        if (!await transfers.ResolveAsync(id, admin.Id, req.Note, ct))
            return NotFound(new { message = "Không tìm thấy giao dịch, hoặc đã xử lý rồi." });

        audit.Record(admin, "finance.bank-resolve", $"credit:{id}", null, req.Note.Trim());
        await db.SaveChangesAsync(ct);

        return Ok(new { ok = true });
    }

    /* ------------------------------------------ docs/07 §13, paying the hosts */

    /// <summary>
    /// The transfers the platform owes and where each one has got to.
    ///
    /// This screen exists because option A of §13 has no API behind it: a
    /// licensed gateway settles every guest's payment into the platform's own
    /// account, and splitting that between hosts is a file somebody uploads to
    /// internet banking. What the platform can do is decide the transfers
    /// exactly, write them down, and refuse to call any of them paid until a
    /// person says the bank took it.
    ///
    /// Account numbers are masked here. They are in the clear only in the file,
    /// which is a separate action and is audited.
    /// </summary>
    [HttpGet("payout-batches")]
    public async Task<ActionResult<PayoutBatchesDto>> PayoutBatches(
        [FromServices] PayoutAccounts accounts, CancellationToken ct)
    {
        var admin = await RequireAsync(ct);
        if (admin is null) return this.Denied();

        var rows = await db.PayoutBatches
            .Include(b => b.Host)
            .OrderByDescending(b => b.Status == PayoutBatchStatus.Pending)
            .ThenByDescending(b => b.Id)
            .Take(200)
            .ToListAsync(ct);

        var waiting = rows.Where(b => b.Status is PayoutBatchStatus.Pending or PayoutBatchStatus.Exported)
                          .ToList();

        return Ok(new PayoutBatchesDto(
            rows.Select(b => new PayoutBatchDto(
                b.Id, b.Reference, b.Host?.Name ?? "", b.AccountName, b.BankName,
                PayoutFiles.Mask(b.AccountNumber), b.Amount, b.Deducted, b.BookingCount,
                b.Status.ToString(), StatusLabel(b.Status), b.DueOn, b.SettledAt, b.Note)).ToList(),
            waiting.Count,
            waiting.Sum(b => b.Amount),
            // The one thing an operator needs told loudly: with no key the sweep
            // cannot even write these rows, so an empty screen would look like
            // "nothing to pay" when it means "cannot pay anyone".
            accounts.CanStore ? null : PayoutAccounts.NoKeyNotice));
    }

    private static string StatusLabel(PayoutBatchStatus status) => status switch
    {
        PayoutBatchStatus.Pending => "Chờ tải file",
        PayoutBatchStatus.Exported => "Đã tải, chờ ngân hàng",
        PayoutBatchStatus.Settled => "Ngân hàng đã chuyển",
        _ => "Ngân hàng từ chối"
    };

    /// <summary>
    /// docs/07 §13 — the file itself, in the six columns every Vietnamese bank's
    /// bulk template is built from.
    ///
    /// Downloading it marks the transfers exported, which is not the same as
    /// paid: it only records that the numbers have left this building. The
    /// account numbers are in the clear here because internet banking cannot use
    /// them any other way, so the action is audited by name.
    /// </summary>
    [HttpGet("payout-batches/file")]
    public async Task<IActionResult> PayoutFile(CancellationToken ct)
    {
        var v = await gate.AllowAsync(AdminAction.RunPayoutTransfers,
            "Tải file chuyển tiền hàng loạt", ct);
        if (!v.Ok) return Refuse(v);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var due = await db.PayoutBatches
            .Where(b => b.Status == PayoutBatchStatus.Pending || b.Status == PayoutBatchStatus.Exported)
            .OrderBy(b => b.Id)
            .ToListAsync(ct);

        if (due.Count == 0)
            return BadRequest(new { message = "Không có lệnh chuyển nào đang chờ." });

        var csv = PayoutFiles.Csv(due);
        var now = DateTime.UtcNow;

        foreach (var batch in due.Where(b => b.Status == PayoutBatchStatus.Pending))
        {
            batch.Status = PayoutBatchStatus.Exported;
            batch.ExportedAt = now;
        }

        audit.Record(v.Admin!, "finance.payout-file", $"batches:{due.Count}", null,
            $"Tải file chuyển tiền {due.Count} lệnh, tổng {due.Sum(b => b.Amount):#,##0}₫");

        await db.SaveChangesAsync(ct);

        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8",
            PayoutFiles.FileName(today));
    }

    /// <summary>
    /// A person saw the bank execute it. This is what posts the payout to the
    /// ledger — nothing earlier does, because nothing earlier moved money.
    /// </summary>
    [HttpPost("payout-batches/{id:long}/settled")]
    public async Task<IActionResult> SettleBatch(
        long id, [FromBody] ResolveCreditRequest req,
        [FromServices] PayoutService payouts, CancellationToken ct)
    {
        var v = await gate.AllowAsync(AdminAction.RunPayoutTransfers, req.Note, ct);
        if (!v.Ok) return Refuse(v);

        if (!await payouts.SettleAsync(id, $"admin:{v.Admin!.Id}", req.Note, ct))
            return BadRequest(new { message = "Không tìm thấy lệnh chuyển, hoặc đã xác nhận rồi." });

        audit.Record(v.Admin!, "finance.payout-settled", $"batch:{id}", null, req.Note!.Trim());
        await db.SaveChangesAsync(ct);

        return Ok(new { ok = true });
    }

    /// <summary>
    /// docs/07 §15.4 — the second pair of eyes on the only button that posts a
    /// payout.
    ///
    /// The operator pastes the outgoing side of the bank statement; every line
    /// the bank shows for the right reference and the right amount settles its
    /// batch, through the same call the button makes. Everything else is
    /// reported rather than acted on, and the answer carries what the statement
    /// did <em>not</em> account for — a transfer exported days ago and never seen
    /// is the failure this whole screen exists to catch, and no statement line
    /// will ever mention it.
    /// </summary>
    [HttpPost("payout-batches/reconcile")]
    public async Task<ActionResult<PayoutReconcileResultDto>> ReconcilePayouts(
        [FromBody] ReconcilePayoutsRequest req,
        [FromServices] PayoutStatementService statementsOut,
        CancellationToken ct)
    {
        var v = await gate.AllowAsync(AdminAction.RunPayoutTransfers, req.Note, ct);
        if (!v.Ok) return Refuse(v);

        if (req.Lines is not { Count: > 0 })
            return BadRequest(new { message = "Chưa có dòng nào để đối chiếu." });

        if (req.Lines.Count > 1000)
            return BadRequest(new { message = "Mỗi lần đối chiếu tối đa 1000 dòng." });

        var result = await statementsOut.ImportAsync(
            $"admin:{v.Admin!.Id}", req.Note,
            req.Lines.Select(l => new PayoutStatementService.Line(
                l.BankReference, l.Amount, l.Description)).ToList(),
            ct);

        audit.Record(v.Admin!, "finance.payout-reconcile", $"lines:{req.Lines.Count}", null,
            $"{result.Settled} khớp lệnh, {result.Pending} cần xử lý, {result.Skipped} đã xác nhận trước đó");
        await db.SaveChangesAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var waiting = (await statementsOut.AwaitingBankAsync(ct))
            .Select(b => new PayoutAwaitingBankDto(
                b.Id, b.Reference, b.Host?.User?.FullName ?? b.AccountName, b.Amount,
                b.BankName, b.AccountName, b.DueOn, today.DayNumber - b.DueOn.DayNumber))
            .ToList();

        return Ok(new PayoutReconcileResultDto(
            result.Settled, result.Pending, result.Skipped,
            result.Rows.Select(r => new PayoutReconcileRowDto(
                r.BankReference, r.Amount, r.Description,
                r.Verdict.ToString(), Domain.PayoutStatements.VerdictLabel(r.Verdict),
                r.MatchedReference, r.Expected, r.Explanation)).ToList(),
            waiting));
    }

    /// <summary>The bank refused it. Nothing is reversed, because nothing was posted.</summary>
    [HttpPost("payout-batches/{id:long}/failed")]
    public async Task<IActionResult> FailBatch(
        long id, [FromBody] ResolveCreditRequest req,
        [FromServices] PayoutService payouts, CancellationToken ct)
    {
        var v = await gate.AllowAsync(AdminAction.RunPayoutTransfers, req.Note, ct);
        if (!v.Ok) return Refuse(v);

        if (!await payouts.FailAsync(id, $"admin:{v.Admin!.Id}", req.Note, ct))
            return BadRequest(new { message = "Không tìm thấy lệnh chuyển, hoặc đã chốt rồi." });

        audit.Record(v.Admin!, "finance.payout-failed", $"batch:{id}", null, req.Note!.Trim());
        await db.SaveChangesAsync(ct);

        return Ok(new { ok = true });
    }

    private static TransactionDto ToDto(Booking b) => new(
        b.Id,
        b.Reference,
        b.Payment!.Reference,
        b.GuestEmail ?? b.GuestUser?.Email,
        b.Listing?.Title ?? "",
        b.Payment.Amount,
        b.RefundedAmount,
        b.Payment.Method,
        b.Payment.CardLast4,
        b.Payment.Status.ToString(),
        b.Status.ToString(),
        BookingLifecycle.Label(b.Status),
        b.Payment.PayoutStatus.ToString(),
        b.Payment.PayoutHoldReason == PayoutHoldReason.None
            ? null
            : Payouts.HoldLabel(b.Payment.PayoutHoldReason),
        b.Payment.PayoutReference,
        b.Payment.CreatedAt,
        b.DisplayCurrency,
        b.DisplayRate);
}
