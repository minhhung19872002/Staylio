using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// Stands in for a real payment processor. There is no PSP behind this build,
/// so a charge succeeds unless the card was set up to be refused — which is the
/// only way to exercise the failed-second-charge path of docs/03 §1 end to end.
/// </summary>
public class PaymentGateway(
    ILogger<PaymentGateway> log, StayHostDbContext db, Gateways.PspRouter router)
{
    /// <summary>A card ending in these four digits always declines.</summary>
    public const string DecliningCard = "0000";

    /// <remarks>
    /// docs/07 §8 — a refusal names a reason rather than carrying a sentence.
    /// The wording belongs to <see cref="Payments.Message"/>, so every path that
    /// takes money says the same thing about the same failure.
    /// </remarks>
    public record Result(bool Ok, DeclineReason Decline = DeclineReason.Unknown)
    {
        public string? Reason => Ok ? null : Payments.Message(Decline);
    }

    /// <summary>
    /// docs/07 §5 — a card ending in these four digits always sends the guest to
    /// their bank's OTP page.
    ///
    /// In reality the issuer decides, and for Vietnamese cards the answer is
    /// nearly always yes. There is no 3-D Secure server behind this build, so
    /// putting every payment through a page that accepts any code would be worse
    /// than useless — it would look like authentication without being it. A test
    /// card selects the branch instead, the same way <see cref="DecliningCard"/>
    /// selects the refusal branch. Wiring a real PSP means making this true for
    /// every card.
    /// </summary>
    public const string AuthenticatingCard = "0002";

    /// <summary>The OTP the stand-in accepts. A real one comes from the guest's bank.</summary>
    public const string TestOtp = "123456";

    /// <summary>
    /// What this gateway remembers charging, keyed by the attempt key — which is
    /// what makes the self-check of docs/07 §5 possible: the platform can ask
    /// what really happened rather than believing the browser.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, decimal> _charged = new();

    public Result Charge(decimal amount, string method, string? cardLast4, string? key = null)
    {
        if (amount <= 0) return new Result(true);

        // Checked before the key below: a key the stand-in once wrote for a
        // method a gateway now owns must not turn into a free "already paid".
        if (!router.StandInMay(method))
        {
            log.LogWarning("Bản giả lập từ chối thu {Amount} qua {Method}: không có quyền thu phương thức này.",
                amount, method);
            return new Result(false, DeclineReason.MethodUnavailable);
        }

        // Already taken under this key — which is what happens after a 3-D Secure
        // authorisation: the bank moved the money on its own page, and this call
        // is the platform catching up. Charging again would be the exact fault
        // docs/07 §7 calls the worst one in the module.
        if (key is not null && (_charged.ContainsKey(key) || db.GatewayCharges.Any(c => c.Reference == key)))
            return new Result(true);

        if (cardLast4 == DecliningCard)
        {
            log.LogInformation("Charge of {Amount} refused: test card.", amount);
            return new Result(false, DeclineReason.BankRefused);
        }

        if (key is not null)
        {
            _charged[key] = amount;

            // The gateway's own book. Business code never writes here — that is
            // what makes the daily reconciliation of docs/07 §7 mean something.
            db.GatewayCharges.Add(new GatewayCharge { Reference = key, Amount = amount, Method = method });
            db.SaveChanges();
        }

        log.LogInformation("Charged {Amount} via {Method}.", amount, method);
        return new Result(true);
    }

    /// <summary>
    /// docs/07 §13 — a charge a real gateway took on its own page, written into
    /// the same book the stand-in writes into.
    ///
    /// It goes through this class rather than being inserted by the checkout
    /// because <c>gateway_charges</c> is meant to be the gateway's record, not the
    /// platform's, and the daily reconciliation of docs/07 §7 only means something
    /// while business code stays out of it. Idempotent: a gateway that sends its
    /// IPN twice must not double the day's total.
    /// </summary>
    public void RecordExternalCharge(string reference, decimal amount, string method)
    {
        if (amount <= 0) return;
        if (_charged.ContainsKey(reference) || db.GatewayCharges.Any(c => c.Reference == reference)) return;

        _charged[reference] = amount;
        db.GatewayCharges.Add(new GatewayCharge { Reference = reference, Amount = amount, Method = method });
        db.SaveChanges();

        log.LogInformation("Cổng ngoài đã thu {Amount} qua {Method} ({Reference}).", amount, method, reference);
    }

    /// <summary>
    /// docs/07 §10 — a card ending in these four digits always hands the refund
    /// back: it stands for the expired or closed card of "Thẻ đã hết hạn hoặc đã
    /// đóng".
    ///
    /// The rule for that case has been in <see cref="Refunds.Redirect"/> since
    /// docs/07 was written and nothing ever called it, because nothing in this
    /// build could produce the event: there was no refund call on this gateway
    /// at all, so a bounced refund was not a thing that could happen. A test
    /// card selects the branch, the same way <see cref="DecliningCard"/> and
    /// <see cref="AuthenticatingCard"/> select theirs.
    /// </summary>
    public const string RefundRejectingCard = "0009";

    /// <summary>
    /// docs/07 §10 — sends money back to the card it came from.
    ///
    /// True means the bank took it. False means it would not, and the caller has
    /// to put the money somewhere the guest can still reach — which is the whole
    /// point of the rule, not an error to swallow.
    /// </summary>
    public bool Refund(decimal amount, string method, string? cardLast4)
    {
        if (amount <= 0) return true;

        // A refund with no gateway visit behind it is money the stand-in took.
        // Where the stand-in never runs it cannot claim to have given anything
        // back; the caller turns false into balance.
        if (!router.StandInEnabled) return false;

        if (PaymentMethods.IsCard(method) && cardLast4 == RefundRejectingCard)
        {
            log.LogInformation("Refund of {Amount} handed back: card closed (test card).", amount);
            return false;
        }

        log.LogInformation("Refunded {Amount} to {Method}.", amount, method);
        return true;
    }

    /// <summary>
    /// docs/07 §12.2 — the small test transfer to a host's new payout account.
    /// Money going out rather than in, so the checkout rules above do not apply;
    /// the account ending 0000 stands for one the bank refuses.
    /// </summary>
    public Result TestTransfer(decimal amount, string? accountLast4)
    {
        if (accountLast4 == DecliningCard) return new Result(false, DeclineReason.BankRefused);
        log.LogInformation("Test transfer of {Amount} sent.", amount);
        return new Result(true);
    }

    /// <summary>docs/07 §5 — does this card send the guest to their bank first?</summary>
    public bool NeedsAuthentication(string method, string? cardLast4) =>
        PaymentMethods.IsCard(method) && cardLast4 == AuthenticatingCard;

    /// <summary>
    /// docs/07 §5 — "hệ thống phải tự kiểm tra lại kết quả với cổng thanh toán".
    /// Returns what the gateway knows about an attempt, or null when it has
    /// never heard of it.
    /// </summary>
    public AuthOutcome? Lookup(string key) =>
        _charged.ContainsKey(key) || db.GatewayCharges.Any(c => c.Reference == key)
            ? AuthOutcome.Succeeded
            : null;

    /// <summary>
    /// docs/07 §7 — the gateway's list of a day's transactions, which is one half
    /// of the daily reconciliation. The other half is the platform's own.
    /// </summary>
    public async Task<IReadOnlyList<Reconciliation.Record>> StatementAsync(DateOnly day, CancellationToken ct)
    {
        var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = from.AddDays(1);

        return await db.GatewayCharges
            .Where(c => c.ChargedAt >= from && c.ChargedAt < to)
            .Select(c => new Reconciliation.Record(c.Reference, c.Amount))
            .ToListAsync(ct);
    }

    /// <summary>The stand-in's OTP check. A real one happens on the bank's own page.</summary>
    public bool CodeAccepted(string? code) => code?.Trim() == TestOtp;

    /// <summary>
    /// Stands in for what happens on the bank's own page: the code is checked and,
    /// if it is right, the money moves — before the platform hears anything.
    ///
    /// That ordering is the whole point. It is why docs/07 §5 insists the platform
    /// ask the gateway rather than believe the browser, and why a guest whose
    /// connection dies on the way back has still been charged.
    /// </summary>
    public Result Authorise(string key, decimal amount, string method, string? cardLast4, string? code)
    {
        if (!CodeAccepted(code)) return new Result(false, DeclineReason.IncorrectDetails);
        return Charge(amount, method, cardLast4, key);
    }
}
