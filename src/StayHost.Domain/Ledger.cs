namespace StayHost.Domain;

/// <summary>
/// Where money sits. docs/00 §6.1: every amount is recorded on both sides, so
/// the sum of every debit must equal the sum of every credit, forever.
/// </summary>
public enum LedgerAccount
{
    /// <summary>Cash the platform is holding on the guest's behalf.</summary>
    GuestFunds = 0,
    /// <summary>Owed to the host until payout runs.</summary>
    HostPayable = 1,
    /// <summary>Platform revenue from the guest service fee.</summary>
    GuestServiceFeeRevenue = 2,
    /// <summary>Platform revenue from the host service fee.</summary>
    HostServiceFeeRevenue = 3,
    /// <summary>Collected on behalf of the tax authority; never platform revenue.</summary>
    TaxPayable = 4,
    /// <summary>Owed back to the guest after a cancellation, until the refund settles.</summary>
    GuestRefundPayable = 5,
    /// <summary>Promotional balance the platform has granted, e.g. host-cancellation goodwill.</summary>
    PromotionalCredit = 6,
    /// <summary>The platform's own expense line, funding credits and goodwill.</summary>
    PlatformExpense = 7,
    /// <summary>
    /// The half of a part-paid booking the guest still owes (docs/01 ĐP-06).
    /// The stay is recognised whole at booking time, so this stands in for the
    /// cash until the second charge lands.
    /// </summary>
    GuestReceivable = 8,
    /// <summary>
    /// Shares collected for a split bill (docs/01 ĐP-07) before the booking is
    /// confirmed. Nothing is recognised while the money sits here — it either
    /// becomes a booking or goes back to the people who sent it.
    /// </summary>
    SplitEscrow = 9,
    /// <summary>
    /// Gift cards sold but not yet moved onto anyone's balance. Somebody paid
    /// real money for these, so they are owed until redeemed.
    /// </summary>
    GiftCardLiability = 10,
    /// <summary>
    /// The Staylio Shield fund (docs/06 §5). Money set aside out of service-fee
    /// revenue, spent on cases, and topped back up when the platform recovers
    /// what it paid out.
    /// </summary>
    ShieldFund = 11,
    /// <summary>
    /// Owed to somebody who was never on the booking — a neighbour, a building
    /// (docs/06 §3.1 C4). Kept apart from what the platform owes hosts, because
    /// it is not a payout and never nets against one.
    /// </summary>
    ThirdPartyPayable = 12
}

public enum LedgerDirection
{
    Debit = 1,
    Credit = 2
}

/// <summary>
/// One immutable half of a transaction. Rows are only ever inserted — a
/// correction is a new transaction, never an edit (docs/00 §6.2).
/// </summary>
public class LedgerEntry
{
    public long Id { get; set; }

    /// <summary>Groups the halves of one transaction; every group must balance.</summary>
    public Guid TransactionId { get; set; }
    /// <summary>What happened: "booking-captured", "booking-refunded", "host-payout".</summary>
    public string TransactionKind { get; set; } = "";

    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }

    /// <summary>Set instead of <see cref="BookingId"/> when the money is for a session.</summary>
    public int? ExperienceBookingId { get; set; }
    public ExperienceBooking? ExperienceBooking { get; set; }

    /// <summary>Set when the money is for a service job (docs/01 MR-05 → MR-07).</summary>
    public int? ServiceBookingId { get; set; }
    public ServiceBooking? ServiceBooking { get; set; }

    public LedgerAccount Account { get; set; }
    public LedgerDirection Direction { get; set; }
    /// <summary>Always positive; <see cref="Direction"/> carries the sign.</summary>
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";

    public string Memo { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Signed amount, for summing a column.</summary>
    public decimal Signed => Direction == LedgerDirection.Debit ? Amount : -Amount;
}

/// <summary>
/// Builds balanced transactions. Nothing here touches the database; callers add
/// the returned rows to their unit of work, which keeps the rules testable.
/// </summary>
public static class Ledger
{
    public sealed class UnbalancedTransactionException(decimal debits, decimal credits)
        : InvalidOperationException($"Bút toán không cân: nợ {debits:#,##0}₫ ≠ có {credits:#,##0}₫.");

    private sealed record Leg(LedgerAccount Account, LedgerDirection Direction, decimal Amount, string Memo);

    private static List<LedgerEntry> Post(
        string kind, int? bookingId, DateTime at, params Leg[] legs) =>
        Post(kind, bookingId, null, at, legs);

    private static List<LedgerEntry> Post(
        string kind, int? bookingId, int? experienceBookingId, DateTime at, params Leg[] legs) =>
        Post(kind, bookingId, experienceBookingId, null, at, legs);

    private static List<LedgerEntry> Post(
        string kind, int? bookingId, int? experienceBookingId, int? serviceBookingId,
        DateTime at, params Leg[] legs)
    {
        var real = legs.Where(l => l.Amount > 0).ToList();

        var debits = real.Where(l => l.Direction == LedgerDirection.Debit).Sum(l => l.Amount);
        var credits = real.Where(l => l.Direction == LedgerDirection.Credit).Sum(l => l.Amount);
        if (debits != credits) throw new UnbalancedTransactionException(debits, credits);

        var transactionId = Guid.NewGuid();
        return real.Select(l => new LedgerEntry
        {
            TransactionId = transactionId,
            TransactionKind = kind,
            BookingId = bookingId,
            ExperienceBookingId = experienceBookingId,
            ServiceBookingId = serviceBookingId,
            Account = l.Account,
            Direction = l.Direction,
            Amount = l.Amount,
            Memo = l.Memo,
            CreatedAt = at
        }).ToList();
    }

    /// <summary>
    /// The guest has paid. Cash lands in the platform's hands and is immediately
    /// split into what it owes the host, what it keeps, and what it holds for tax.
    /// </summary>
    /// <param name="paidNow">
    /// What the card was actually charged. Less than the total means the guest
    /// paid a deposit (docs/01 ĐP-06); the rest is carried as a receivable so
    /// the host's share, the fees and the tax are all recognised at once.
    /// </param>
    /// <param name="creditUsed">
    /// Balance the guest spent on this booking. It was already an expense when
    /// it was granted, so spending it discharges that liability rather than
    /// costing the platform a second time.
    /// </param>
    public static List<LedgerEntry> CaptureBooking(
        Booking booking, Pricing.Breakdown price, DateTime at,
        decimal? paidNow = null, decimal creditUsed = 0m)
    {
        var cash = paidNow is { } part ? Math.Clamp(part, 0, price.Total) : price.Total;
        var credit = Math.Clamp(creditUsed, 0, price.Promotion);

        return Post("booking-captured", booking.Id, at,
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Debit, cash, $"Khách trả đơn {booking.Reference}"),
            new Leg(LedgerAccount.GuestReceivable, LedgerDirection.Debit, price.Total - cash, "Phần khách còn nợ"),
            new Leg(LedgerAccount.PromotionalCredit, LedgerDirection.Debit, credit, "Khách dùng số dư"),
            // docs/01 TC-09 — a promo code is money the platform gives up now, so it
            // is an expense on capture. Balance is not: it already cost the platform
            // once, when it was granted, and spending it only discharges that.
            new Leg(LedgerAccount.PlatformExpense, LedgerDirection.Debit, price.Coupon,
                "Mã giảm giá sàn chịu"),
            new Leg(LedgerAccount.HostPayable, LedgerDirection.Credit, price.HostPayout, "Phần chủ nhà nhận"),
            new Leg(LedgerAccount.GuestServiceFeeRevenue, LedgerDirection.Credit, price.GuestServiceFee, "Phí dịch vụ khách"),
            new Leg(LedgerAccount.HostServiceFeeRevenue, LedgerDirection.Credit, price.HostServiceFee, "Phí dịch vụ chủ nhà"),
            new Leg(LedgerAccount.TaxPayable, LedgerDirection.Credit, price.Tax, "Thuế thu hộ"));
    }

    /// <summary>
    /// docs/01 CĐ-06 — a confirmed booking changed dates or guests, so the money
    /// already recognised moves by the difference. Each account is adjusted by
    /// new − old: the host's payout, the two fees and the tax shift, and the guest
    /// either pays the extra (cash in) or is owed a refund. Coupon and balance are
    /// left as they were — a change re-prices the stay, not the one-off discount —
    /// so the difference is exactly the change in the recognised gross, and the
    /// transaction balances the same way the original capture did.
    /// </summary>
    public static List<LedgerEntry> AdjustBooking(
        Booking booking, Pricing.Breakdown price, DateTime at)
    {
        var legs = new List<Leg>();

        Delta(legs, LedgerAccount.HostPayable, price.HostPayout - booking.HostPayout,
            increaseIs: LedgerDirection.Credit, "Điều chỉnh phần chủ nhà");
        Delta(legs, LedgerAccount.GuestServiceFeeRevenue, price.GuestServiceFee - booking.ServiceFee,
            increaseIs: LedgerDirection.Credit, "Điều chỉnh phí dịch vụ khách");
        Delta(legs, LedgerAccount.HostServiceFeeRevenue, price.HostServiceFee - booking.HostServiceFee,
            increaseIs: LedgerDirection.Credit, "Điều chỉnh phí dịch vụ chủ nhà");
        Delta(legs, LedgerAccount.TaxPayable, price.Tax - booking.Tax,
            increaseIs: LedgerDirection.Credit, "Điều chỉnh thuế");

        var diff = price.Total - booking.Total;
        if (diff >= 0)
            // Owed by the guest, not yet in hand: nothing is charged when the host
            // accepts, and posting it straight to guest funds paid the host for
            // nights nobody had paid for. The balance collection of docs/01 ĐP-06
            // takes it, and CollectBalance clears this receivable.
            Delta(legs, LedgerAccount.GuestReceivable, diff, increaseIs: LedgerDirection.Debit, "Khách trả thêm khi đổi lịch");
        else
            // Money owed back to the guest, held like any other refund payable.
            legs.Add(new Leg(LedgerAccount.GuestRefundPayable, LedgerDirection.Credit, -diff, "Hoàn bớt khi đổi lịch"));

        return Post("booking-adjusted", booking.Id, at, [.. legs]);
    }

    /// <summary>Adds a signed delta as a leg, flipping direction when it is negative.</summary>
    private static void Delta(
        List<Leg> legs, LedgerAccount account, decimal delta, LedgerDirection increaseIs, string memo)
    {
        if (delta == 0) return;
        var dir = delta > 0
            ? increaseIs
            : increaseIs == LedgerDirection.Credit ? LedgerDirection.Debit : LedgerDirection.Credit;
        legs.Add(new Leg(account, dir, Math.Abs(delta), memo));
    }

    /// <summary>
    /// docs/01 MR-03 — a seat at a session. Same accounts as a stay: the money
    /// engine does not care what was sold, only how it splits.
    ///
    /// The figures come off the booking rather than from a fresh quote, which is
    /// what lets this be posted long after the booking was made — docs/07 §2.3's
    /// bank transfer confirms when the money lands, by which time a re-quote
    /// could disagree with the total the guest actually paid. It also means
    /// capture and <see cref="RefundExperience"/> read the same numbers, so a
    /// full refund provably reverses the capture.
    /// </summary>
    public static List<LedgerEntry> CaptureExperience(ExperienceBooking booking, DateTime at) =>
        Post("experience-captured", null, booking.Id, at,
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Debit, booking.Total, $"Khách trả vé {booking.Reference}"),
            new Leg(LedgerAccount.HostPayable, LedgerDirection.Credit, booking.HostPayout, "Phần chủ trải nghiệm nhận"),
            new Leg(LedgerAccount.GuestServiceFeeRevenue, LedgerDirection.Credit, booking.ServiceFee, "Phí dịch vụ khách"),
            new Leg(LedgerAccount.HostServiceFeeRevenue, LedgerDirection.Credit, booking.HostServiceFee, "Phí dịch vụ chủ nhà"),
            new Leg(LedgerAccount.TaxPayable, LedgerDirection.Credit, booking.Tax, "Thuế thu hộ"));

    /// <summary>
    /// A ticket refunded, whether the guest pulled out or the session was called
    /// off. A full refund reverses exactly what the capture recognised.
    /// </summary>
    public static List<LedgerEntry> RefundExperience(
        ExperienceBooking booking, decimal amount, DateTime at)
    {
        if (amount <= 0) return [];

        // Everything is returned in the proportion it was taken, so a partial
        // refund never quietly comes out of the host's share alone.
        var share = booking.Total == 0 ? 0m : amount / booking.Total;
        var hostBack = Math.Round(booking.HostPayout * share, 0, MidpointRounding.AwayFromZero);
        var guestFeeBack = Math.Round(booking.ServiceFee * share, 0, MidpointRounding.AwayFromZero);
        var hostFeeBack = Math.Round(booking.HostServiceFee * share, 0, MidpointRounding.AwayFromZero);
        var taxBack = amount - hostBack - guestFeeBack - hostFeeBack;

        return Post("experience-refunded", null, booking.Id, at,
            new Leg(LedgerAccount.HostPayable, LedgerDirection.Debit, hostBack, "Thu lại phần chủ trải nghiệm"),
            new Leg(LedgerAccount.GuestServiceFeeRevenue, LedgerDirection.Debit, guestFeeBack, "Hoàn phí dịch vụ khách"),
            new Leg(LedgerAccount.HostServiceFeeRevenue, LedgerDirection.Debit, hostFeeBack, "Hoàn phí dịch vụ chủ nhà"),
            new Leg(LedgerAccount.TaxPayable, LedgerDirection.Debit, taxBack, "Hoàn thuế"),
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Credit, amount, $"Hoàn vé {booking.Reference}"));
    }

    /// <summary>
    /// docs/01 MR-05 → MR-07 — a service job. A partner job pays the platform a
    /// commission where a host's own service pays the host service fee; both land
    /// in the same revenue account because both are what the platform kept.
    /// </summary>
    /// <remarks>Reads the booking's own figures, for the reasons on <see cref="CaptureExperience"/>.</remarks>
    public static List<LedgerEntry> CaptureService(ServiceBooking booking, DateTime at) =>
        Post("service-captured", null, null, booking.Id, at,
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Debit, booking.Total, $"Khách trả dịch vụ {booking.Reference}"),
            new Leg(LedgerAccount.HostPayable, LedgerDirection.Credit, booking.ProviderPayout, "Phần bên cung cấp nhận"),
            new Leg(LedgerAccount.GuestServiceFeeRevenue, LedgerDirection.Credit, booking.ServiceFee, "Phí dịch vụ khách"),
            new Leg(LedgerAccount.HostServiceFeeRevenue, LedgerDirection.Credit, booking.PlatformCut, "Phần sàn giữ lại"),
            new Leg(LedgerAccount.TaxPayable, LedgerDirection.Credit, booking.Tax, "Thuế thu hộ"));

    /* ------------------------------------------------------- Staylio Shield */

    /// <summary>
    /// docs/06 §5 - the monthly set-aside. Revenue the platform has already
    /// earned is moved into a fund it has committed to spending on cases, so it
    /// stops being profit the moment it is set aside.
    /// </summary>
    public static List<LedgerEntry> FundShield(decimal amount, string period, DateTime at) =>
        amount <= 0
            ? []
            : Post("shield-funded", null, at,
                new Leg(LedgerAccount.PlatformExpense, LedgerDirection.Debit, amount, $"Trích quỹ Staylio Shield {period}"),
                new Leg(LedgerAccount.ShieldFund, LedgerDirection.Credit, amount, "Quỹ Staylio Shield"));

    /// <summary>
    /// A case paid out of the fund. Whatever the guest is owed becomes payable
    /// to them; whatever a host is owed goes onto what the platform owes hosts.
    /// </summary>
    public static List<LedgerEntry> PayFromShield(
        ShieldClaim claim, decimal toGuest, decimal toHost, DateTime at, decimal toThirdParty = 0m)
    {
        var total = Math.Max(0m, toGuest) + Math.Max(0m, toHost) + Math.Max(0m, toThirdParty);
        if (total <= 0) return [];

        return Post("shield-paid", claim.BookingId, at,
            new Leg(LedgerAccount.ShieldFund, LedgerDirection.Debit, total, $"Chi quỹ hồ sơ {claim.Reference}"),
            new Leg(LedgerAccount.GuestRefundPayable, LedgerDirection.Credit, Math.Max(0m, toGuest), "Trả khách"),
            new Leg(LedgerAccount.HostPayable, LedgerDirection.Credit, Math.Max(0m, toHost), "Trả chủ nhà"),
            new Leg(LedgerAccount.ThirdPartyPayable, LedgerDirection.Credit, Math.Max(0m, toThirdParty),
                "Trả bên thứ ba"));
    }

    /// <summary>
    /// docs/06 §8 — the force-majeure compensation. It comes out of the same
    /// fund a case would, but there is no case: nobody filed, so there is no
    /// <see cref="ShieldClaim"/> to hang it on and <see cref="PayFromShield"/>
    /// cannot be used.
    /// </summary>
    public static List<LedgerEntry> CompensateHostFromShield(Booking booking, decimal amount, DateTime at) =>
        amount <= 0
            ? []
            : Post("shield-force-majeure", booking.Id, at,
                new Leg(LedgerAccount.ShieldFund, LedgerDirection.Debit, amount,
                    $"Đền bù bất khả kháng đơn {booking.Reference}"),
                new Leg(LedgerAccount.HostPayable, LedgerDirection.Credit, amount, "Trả chủ nhà"));

    /// <summary>
    /// docs/06 §3.3 for a C4 case: what the guest is made to pay goes straight to
    /// the injured party rather than to the host, who was never out of pocket.
    /// </summary>
    public static List<LedgerEntry> ChargeForThirdParty(ShieldClaim claim, decimal amount, DateTime at) =>
        amount <= 0
            ? []
            : Post("shield-charged", claim.BookingId, at,
                new Leg(LedgerAccount.GuestFunds, LedgerDirection.Debit, amount, $"Thu từ khách hồ sơ {claim.Reference}"),
                new Leg(LedgerAccount.ThirdPartyPayable, LedgerDirection.Credit, amount, "Trả bên thứ ba"));

    /// <summary>
    /// docs/06 §3.3 - what the guest is made to pay never touches the fund:
    /// it moves straight from their money to what the platform owes the host.
    /// </summary>
    public static List<LedgerEntry> ChargeCounterparty(ShieldClaim claim, decimal amount, DateTime at) =>
        amount <= 0
            ? []
            : Post("shield-charged", claim.BookingId, at,
                new Leg(LedgerAccount.GuestFunds, LedgerDirection.Debit, amount, $"Thu từ khách hồ sơ {claim.Reference}"),
                new Leg(LedgerAccount.HostPayable, LedgerDirection.Credit, amount, "Trả chủ nhà"));

    /// <summary>
    /// docs/06 §5 - money chased down after the fund had already paid goes
    /// back into the fund, not into profit.
    /// </summary>
    public static List<LedgerEntry> RecoverToShield(ShieldClaim claim, decimal amount, DateTime at) =>
        amount <= 0
            ? []
            : Post("shield-recovered", claim.BookingId, at,
                new Leg(LedgerAccount.GuestFunds, LedgerDirection.Debit, amount, $"Thu hồi hồ sơ {claim.Reference}"),
                new Leg(LedgerAccount.ShieldFund, LedgerDirection.Credit, amount, "Hoàn lại quỹ Staylio Shield"));

    /// <summary>A service job refunded, in the proportion each account received it.</summary>
    public static List<LedgerEntry> RefundService(ServiceBooking booking, decimal amount, DateTime at)
    {
        if (amount <= 0) return [];

        var share = booking.Total == 0 ? 0m : amount / booking.Total;
        var providerBack = Math.Round(booking.ProviderPayout * share, 0, MidpointRounding.AwayFromZero);
        var guestFeeBack = Math.Round(booking.ServiceFee * share, 0, MidpointRounding.AwayFromZero);
        var cutBack = Math.Round(booking.PlatformCut * share, 0, MidpointRounding.AwayFromZero);
        var taxBack = amount - providerBack - guestFeeBack - cutBack;

        return Post("service-refunded", null, null, booking.Id, at,
            new Leg(LedgerAccount.HostPayable, LedgerDirection.Debit, providerBack, "Thu lại phần bên cung cấp"),
            new Leg(LedgerAccount.GuestServiceFeeRevenue, LedgerDirection.Debit, guestFeeBack, "Hoàn phí dịch vụ khách"),
            new Leg(LedgerAccount.HostServiceFeeRevenue, LedgerDirection.Debit, cutBack, "Hoàn phần sàn giữ"),
            new Leg(LedgerAccount.TaxPayable, LedgerDirection.Debit, taxBack, "Hoàn thuế"),
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Credit, amount, $"Hoàn dịch vụ {booking.Reference}"));
    }

    /// <summary>
    /// docs/07 §10 for a ticket or a service — the gateway would not take the
    /// refund back, so the money stays with the platform as the guest's balance.
    /// Posted after <see cref="RefundExperience"/>/<see cref="RefundService"/>,
    /// which already moved it out of guest funds; this puts it back there and
    /// names the balance that now holds it.
    /// </summary>
    public static List<LedgerEntry> RefundKeptAsCredit(
        int? experienceBookingId, int? serviceBookingId, string reference, decimal amount, DateTime at) =>
        amount <= 0
            ? []
            : Post("refund-as-credit", null, experienceBookingId, serviceBookingId, at,
                new Leg(LedgerAccount.GuestFunds, LedgerDirection.Debit, amount, "Hoàn bằng số dư"),
                new Leg(LedgerAccount.PromotionalCredit, LedgerDirection.Credit, amount,
                    $"Trả lại số dư {reference}"));

    /// <summary>docs/01 ĐP-07 — one person's share, held until the last one lands.</summary>
    public static List<LedgerEntry> HoldShare(int bookingId, string reference, decimal amount, DateTime at) =>
        Post("split-share-held", bookingId, at,
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Debit, amount, $"Phần chia đơn {reference}"),
            new Leg(LedgerAccount.SplitEscrow, LedgerDirection.Credit, amount, "Giữ chờ đủ người"));

    /// <summary>The last share landed: what was held becomes the money for the booking.</summary>
    public static List<LedgerEntry> ReleaseEscrow(Booking booking, decimal amount, DateTime at) =>
        Post("split-escrow-released", booking.Id, at,
            new Leg(LedgerAccount.SplitEscrow, LedgerDirection.Debit, amount, "Giải toả tiền giữ"),
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Credit, amount, $"Chuyển vào đơn {booking.Reference}"));

    /// <summary>Nobody finished paying: every share held goes back where it came from.</summary>
    public static List<LedgerEntry> ReturnShare(int bookingId, string reference, decimal amount, DateTime at) =>
        Post("split-share-returned", bookingId, at,
            new Leg(LedgerAccount.SplitEscrow, LedgerDirection.Debit, amount, "Trả lại phần đã giữ"),
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Credit, amount, $"Hoàn phần chia đơn {reference}"));

    /// <summary>
    /// docs/01 ĐP-06 — the second charge. Nothing is recognised again; the cash
    /// simply arrives and the receivable goes away.
    /// </summary>
    public static List<LedgerEntry> CollectBalance(Booking booking, decimal amount, DateTime at) =>
        Post("balance-collected", booking.Id, at,
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Debit, amount, $"Khách trả nốt đơn {booking.Reference}"),
            new Leg(LedgerAccount.GuestReceivable, LedgerDirection.Credit, amount, "Xoá phần còn nợ"));

    /// <summary>
    /// A part-paid booking cancelled before the rest was taken. What the guest
    /// is owed is set against what they still owe rather than moved as cash —
    /// nobody sends money in both directions on the same booking.
    /// </summary>
    public static List<LedgerEntry> NetRefundAgainstReceivable(Booking booking, decimal amount, DateTime at) =>
        amount <= 0
            ? []
            : Post("refund-netted", booking.Id, at,
                new Leg(LedgerAccount.GuestRefundPayable, LedgerDirection.Debit, amount, "Cấn trừ vào phần khách còn nợ"),
                new Leg(LedgerAccount.GuestReceivable, LedgerDirection.Credit, amount, $"Đơn {booking.Reference}"));

    /// <summary>
    /// A part-paid booking that ends before the balance was ever collected: the
    /// receivable is written off against the same accounts that recognised it,
    /// so the books do not carry a debt nobody is going to pay.
    /// </summary>
    public static List<LedgerEntry> WriteOffReceivable(Booking booking, decimal amount, DateTime at) =>
        amount <= 0
            ? []
            : Post("receivable-written-off", booking.Id, at,
                new Leg(LedgerAccount.PlatformExpense, LedgerDirection.Debit, amount, "Xoá nợ khách không trả"),
                new Leg(LedgerAccount.GuestReceivable, LedgerDirection.Credit, amount, $"Đơn {booking.Reference}"));

    /// <summary>
    /// A cancellation. Everything being returned is taken back out of the
    /// accounts that received it and parked as a payable to the guest.
    /// </summary>
    public static List<LedgerEntry> RefundBooking(
        Booking booking, Cancellation.Outcome outcome, decimal hostServiceFeeReturned, DateTime at)
    {
        var roomAndCleaning = outcome.RoomRefund + outcome.CleaningRefund;

        var entries = Post("booking-refunded", booking.Id, at,
            new Leg(LedgerAccount.HostPayable, LedgerDirection.Debit, roomAndCleaning - hostServiceFeeReturned, "Thu lại phần chủ nhà"),
            new Leg(LedgerAccount.HostServiceFeeRevenue, LedgerDirection.Debit, hostServiceFeeReturned, "Hoàn phí dịch vụ chủ nhà"),
            new Leg(LedgerAccount.GuestServiceFeeRevenue, LedgerDirection.Debit, outcome.ServiceFeeRefund, "Hoàn phí dịch vụ khách"),
            new Leg(LedgerAccount.TaxPayable, LedgerDirection.Debit, outcome.TaxRefund, "Hoàn thuế"),
            new Leg(LedgerAccount.GuestRefundPayable, LedgerDirection.Credit, outcome.Amount, $"Hoàn cho khách đơn {booking.Reference}"));

        if (outcome.GoodwillCredit > 0)
        {
            entries.AddRange(Post("goodwill-credit", booking.Id, at,
                new Leg(LedgerAccount.PlatformExpense, LedgerDirection.Debit, outcome.GoodwillCredit, "Bù đắp do chủ nhà huỷ"),
                new Leg(LedgerAccount.PromotionalCredit, LedgerDirection.Credit, outcome.GoodwillCredit, "Số dư tặng khách")));
        }

        return entries;
    }

    /// <summary>
    /// docs/07 §15 TC-A-02 — a refund a person decided on, outside the
    /// cancellation rules. Nobody is taking it off the host: an admin overruling
    /// the policy is the platform choosing to pay, so it lands on the platform's
    /// own expense line where the finance report will show it as a loss.
    /// </summary>
    public static List<LedgerEntry> ManualRefund(Booking booking, decimal amount, DateTime at) =>
        amount <= 0
            ? []
            : Post("manual-refund", booking.Id, at,
                new Leg(LedgerAccount.PlatformExpense, LedgerDirection.Debit, amount,
                    $"Hoàn tiền thủ công đơn {booking.Reference}"),
                new Leg(LedgerAccount.GuestRefundPayable, LedgerDirection.Credit, amount, "Phải trả khách"));

    /// <summary>
    /// Balance handed to a guest out of the platform's own pocket — a price
    /// match (docs/01 MR-10), a goodwill gesture, a referral. No host is out of
    /// anything, so it lands on the platform's expense line.
    /// </summary>
    public static List<LedgerEntry> GrantCredit(
        Booking? booking, decimal amount, string memo, DateTime at) =>
        amount <= 0
            ? []
            : Post("credit-granted", booking?.Id, at,
                new Leg(LedgerAccount.PlatformExpense, LedgerDirection.Debit, amount, memo),
                new Leg(LedgerAccount.PromotionalCredit, LedgerDirection.Credit, amount,
                    booking is null ? "Số dư tặng khách" : $"Số dư tặng khách đơn {booking.Reference}"));

    /// <summary>
    /// Somebody bought a gift card. Real money came in and the platform now
    /// owes that much in balance to whoever ends up holding the code.
    /// </summary>
    public static List<LedgerEntry> SellGiftCard(decimal amount, string code, DateTime at) =>
        Post("gift-card-sold", null, at,
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Debit, amount, $"Bán thẻ quà tặng {code}"),
            new Leg(LedgerAccount.GiftCardLiability, LedgerDirection.Credit, amount, "Nợ thẻ quà tặng"));

    /// <summary>
    /// A code was typed in. Nothing was bought and nothing was spent: the debt
    /// simply moves from "a card out there" to "this person's balance".
    /// </summary>
    public static List<LedgerEntry> RedeemGiftCard(decimal amount, string code, DateTime at) =>
        Post("gift-card-redeemed", null, at,
            new Leg(LedgerAccount.GiftCardLiability, LedgerDirection.Debit, amount, $"Đổi thẻ {code}"),
            new Leg(LedgerAccount.PromotionalCredit, LedgerDirection.Credit, amount, "Chuyển vào số dư khách"));

    /// <summary>
    /// The balance part of a refund. A guest who paid with credit is owed
    /// credit back, not cash, so the payable is cleared against the balance
    /// they hold rather than against the platform's bank.
    /// </summary>
    public static List<LedgerEntry> SettleRefundAsCredit(Booking booking, decimal amount, DateTime at) =>
        amount <= 0
            ? []
            : Post("refund-as-credit", booking.Id, at,
                new Leg(LedgerAccount.GuestRefundPayable, LedgerDirection.Debit, amount, "Hoàn bằng số dư"),
                new Leg(LedgerAccount.PromotionalCredit, LedgerDirection.Credit, amount,
                    $"Trả lại số dư đơn {booking.Reference}"));

    /// <summary>Cash actually leaves for the guest's card; the payable is cleared.</summary>
    public static List<LedgerEntry> SettleRefund(Booking booking, decimal amount, DateTime at) =>
        Post("refund-settled", booking.Id, at,
            new Leg(LedgerAccount.GuestRefundPayable, LedgerDirection.Debit, amount, "Chi hoàn tiền"),
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Credit, amount, $"Chuyển trả khách đơn {booking.Reference}"));

    /// <summary>Cash leaves for the host, 24 hours after check-in (docs/03 §5).</summary>
    public static List<LedgerEntry> PayoutHost(Booking booking, decimal amount, DateTime at) =>
        Post("host-payout", booking.Id, at,
            new Leg(LedgerAccount.HostPayable, LedgerDirection.Debit, amount, "Chi trả chủ nhà"),
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Credit, amount, $"Chuyển cho chủ nhà đơn {booking.Reference}"));

    /// <summary>
    /// docs/02 G8, docs/07 §19 — the part of a booking's host earnings that goes
    /// to a co-host instead of the owner.
    ///
    /// Deliberately the same two accounts as <see cref="PayoutHost"/>: this is not
    /// a new kind of money, it is the same host payable reaching a different
    /// person. The owner's own posting for the booking is reduced by exactly this
    /// much, so the two together still debit HostPayable by the whole payout and
    /// the reconciliation of docs/07 §7 has nothing new to explain.
    /// </summary>
    public static List<LedgerEntry> PayoutCoHost(Booking booking, decimal amount, DateTime at) =>
        amount <= 0
            ? []
            : Post("cohost-payout", booking.Id, at,
                new Leg(LedgerAccount.HostPayable, LedgerDirection.Debit, amount, "Chi trả người đồng quản lý"),
                new Leg(LedgerAccount.GuestFunds, LedgerDirection.Credit, amount,
                    $"Chuyển cho người đồng quản lý, đơn {booking.Reference}"));

    /// <summary>
    /// docs/07 §19.4 — a co-host was paid, then the stay was refunded. The money
    /// has left, so nothing is reversed; what is recorded is that they now owe it
    /// back, exactly as <see cref="RecoverFromHost"/> records the same fact about
    /// an owner. It comes off their next transfers.
    /// </summary>
    public static List<LedgerEntry> RecoverFromCoHost(Booking booking, decimal amount, DateTime at) =>
        amount <= 0
            ? []
            : Post("cohost-debt-recovered", booking.Id, at,
                new Leg(LedgerAccount.HostPayable, LedgerDirection.Debit, amount,
                    "Khấu trừ khoản người đồng quản lý nợ sàn"),
                new Leg(LedgerAccount.PlatformExpense, LedgerDirection.Credit, amount,
                    $"Thu hồi từ người đồng quản lý, đơn {booking.Reference}"));

    /// <summary>
    /// docs/09 §4 (MR-C-03) — paying an experience's host or a service's provider,
    /// a day after the session ends. Their money already sits in HostPayable from
    /// the capture; this is the cash leaving to reach them.
    /// </summary>
    public static List<LedgerEntry> PayoutExperience(ExperienceBooking booking, decimal amount, DateTime at) =>
        // The entry hangs off the experience booking, not off a stay: BookingId
        // is a foreign key into `bookings`, and an experience id is not one.
        Post("experience-payout", null, booking.Id, at,
            new Leg(LedgerAccount.HostPayable, LedgerDirection.Debit, amount, "Chi trả người dẫn"),
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Credit, amount, $"Chuyển cho người dẫn, đơn {booking.Reference}"));

    public static List<LedgerEntry> PayoutService(ServiceBooking booking, decimal amount, DateTime at) =>
        Post("service-payout", null, null, booking.Id, at,
            new Leg(LedgerAccount.HostPayable, LedgerDirection.Debit, amount, "Chi trả nhà cung cấp"),
            new Leg(LedgerAccount.GuestFunds, LedgerDirection.Credit, amount, $"Chuyển cho nhà cung cấp, đơn {booking.Reference}"));

    /// <summary>
    /// docs/07 §17.4 — the part of a payout kept back against what the host owes.
    /// It is not cash going out, so it never touches the bank leg: what the
    /// platform was holding for the host simply stops being owed to them.
    /// </summary>
    public static List<LedgerEntry> RecoverFromHost(Booking booking, decimal amount, DateTime at) =>
        amount <= 0
            ? []
            : Post("host-debt-recovered", booking.Id, at,
                new Leg(LedgerAccount.HostPayable, LedgerDirection.Debit, amount, "Khấu trừ khoản chủ nhà nợ sàn"),
                new Leg(LedgerAccount.PlatformExpense, LedgerDirection.Credit, amount,
                    $"Thu hồi từ chủ nhà, đơn {booking.Reference}"));

    /// <summary>
    /// docs/01 AT-04 — an admin's ruling on a claim. Money moves between the
    /// two sides of the booking, never out of thin air: whatever the guest gets
    /// back comes out of what the host was owed, and the reverse for damages.
    /// </summary>
    public static List<LedgerEntry> SettleClaim(
        Booking booking, decimal toGuest, decimal toHost, DateTime at)
    {
        if (toGuest > 0)
        {
            return Post("claim-to-guest", booking.Id, at,
                new Leg(LedgerAccount.HostPayable, LedgerDirection.Debit, toGuest, $"Bồi thường khách, hồ sơ đơn {booking.Reference}"),
                new Leg(LedgerAccount.GuestRefundPayable, LedgerDirection.Credit, toGuest, "Phải trả khách theo phân xử"));
        }

        if (toHost > 0)
        {
            return Post("claim-to-host", booking.Id, at,
                new Leg(LedgerAccount.GuestFunds, LedgerDirection.Debit, toHost, $"Khách bồi thường, hồ sơ đơn {booking.Reference}"),
                new Leg(LedgerAccount.HostPayable, LedgerDirection.Credit, toHost, "Phải trả chủ nhà theo phân xử"));
        }

        return [];
    }

    /// <summary>
    /// Daily reconciliation (docs/03 §5). A non-zero result is the alarm the
    /// spec asks for — one đồng out and something is wrong.
    /// </summary>
    public static decimal Imbalance(IEnumerable<LedgerEntry> entries) =>
        entries.Sum(e => e.Signed);
}
