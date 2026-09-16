using Microsoft.Extensions.Options;
using StayHost.Domain;

namespace StayHost.Web.Services.Gateways;

/// <summary>
/// docs/07 §13 phương án A — OnePay, a second gateway for the same two card rows
/// VNPay serves.
///
/// It exists because of something VNPay's sandbox cannot do. Their published
/// test card is NCB, a domestic one; the international row can be opened, the
/// signature is accepted and their Visa form appears, but no test card exists to
/// finish with, so the Visa path was never proven end to end. OnePay's public
/// test merchant authorises a Visa card properly — <c>vpc_TxnResponseCode=0,
/// vpc_Message=Approved, vpc_Card=VC</c> — which is what let this file be
/// written against observed behaviour rather than against a PDF.
///
/// One difference matters beyond testing. OnePay returns <c>vpc_CardNum</c>
/// masked as <c>400555xxxxxx0001</c> on an <em>ordinary</em> payment, so the
/// last four digits docs/07 §4 wants — for refund routing and expiry reminders —
/// arrive without the guest having to save anything. On VNPay they only ever
/// arrive through the token API.
///
/// Like the other three, no card number passes through this process. The guest
/// types it on OnePay's page.
/// </summary>
public class OnePayProvider(
    IOptions<PspSettings> options, IHttpClientFactory http, ILogger<OnePayProvider> log)
    : IPspProvider
{
    private readonly PspSettings _psp = options.Value;
    private PspSettings.OnePayOptions Cfg => _psp.Onepay;

    public string Key => Psp.OnePay;
    public bool IsConfigured => Cfg.IsConfigured;

    public Task<PspStart> StartAsync(PspOrder order, CancellationToken ct)
    {
        if (!IsConfigured)
            return Task.FromResult(new PspStart(false, Error: "OnePay chưa được cấu hình."));

        var back = $"{_psp.PublicUrl}/api/payments/onepay/return";

        var fields = new Dictionary<string, string>
        {
            ["vpc_Version"] = "2",
            ["vpc_Command"] = "pay",
            ["vpc_AccessCode"] = Cfg.AccessCode,
            ["vpc_Merchant"] = Cfg.Merchant,
            ["vpc_Locale"] = "vn",
            ["vpc_ReturnURL"] = back,
            ["vpc_MerchTxnRef"] = order.OrderRef,
            ["vpc_OrderInfo"] = Reference(order.OrderRef),
            ["vpc_Amount"] = Psp.OnePayAmount(order.Amount).ToString(),
            ["vpc_Currency"] = "VND",
            // Their gateway rejects an address outside 7–45 characters, the same
            // trap VNPay set: a loopback ::1 from Kestrel reads as a signature
            // failure rather than as a bad field.
            ["vpc_TicketNo"] = Psp.ClientIp(order.ClientIp),

            // Not signed — it is outside the vpc_/user_ families on purpose, and
            // including it in the rebuild is what broke the first attempt at
            // verifying their reply.
            ["AgainLink"] = $"{_psp.PublicUrl}/"
        };

        var query = Psp.VnPayQuery(fields);
        var hash = Psp.OnePaySign(fields, Cfg.HashSecret);
        var payUrl = Psp.OnePayIsDomestic(order.Method) ? Cfg.DomesticPayUrl : Cfg.PayUrl;

        return Task.FromResult(new PspStart(true, $"{payUrl}?{query}&vpc_SecureHash={hash}"));
    }

    /// <summary>
    /// OnePay shows <c>vpc_OrderInfo</c> to the cardholder and puts it on the
    /// statement, and it refuses punctuation there. The order reference is
    /// already alphanumeric, so this only guards against a description arriving
    /// from somewhere else later.
    /// </summary>
    private static string Reference(string orderRef) =>
        new(orderRef.Where(char.IsLetterOrDigit).ToArray());

    public PspVerdict Read(IReadOnlyDictionary<string, string> payload)
    {
        if (!Psp.OnePayVerify(payload, Cfg.HashSecret))
        {
            log.LogWarning("OnePay callback for {Ref} failed its signature check.",
                payload.GetValueOrDefault("vpc_MerchTxnRef"));
            return PspVerdict.Forged;
        }

        var code = payload.GetValueOrDefault("vpc_TxnResponseCode") ?? "";
        var txn = payload.GetValueOrDefault("vpc_TransactionNo");

        var amount = decimal.TryParse(payload.GetValueOrDefault("vpc_Amount"), out var raw)
            ? raw / 100m
            : 0m;

        // docs/07 §4 — present on every approved payment here, unlike VNPay.
        var last4 = Psp.OnePayLast4(payload.GetValueOrDefault("vpc_CardNum"));

        // "VC" is a Visa credit card, "MC" a Mastercard, and so on. The platform
        // only cares whether it was domestic, and OnePay's card row is the
        // international one, so this is recorded as such rather than guessed.
        var card = payload.GetValueOrDefault("vpc_Card");

        if (Psp.OnePayPaid(code))
            return new PspVerdict(PaymentSessionStatus.Paid, amount, txn, code,
                CardLast4: last4, CardType: card);

        if (Psp.OnePayCancelled(code))
            return new PspVerdict(PaymentSessionStatus.Cancelled, amount, txn, code);

        return new PspVerdict(PaymentSessionStatus.Failed, amount, txn, code, Psp.OnePayDecline(code));
    }

    /// <summary>
    /// docs/07 §5 — asking OnePay directly, because the guest coming back proves
    /// nothing and on a developer machine they are the only ones who ever do.
    ///
    /// Their query API is a form post to a different endpoint, and it needs an
    /// operator account OnePay issues separately from the hash secret. Without
    /// one this cannot ask, and it says so once rather than reporting a payment
    /// as failed — which would cancel a booking somebody had paid for.
    /// </summary>
    public async Task<PspVerdict> QueryAsync(string orderRef, DateTime createdAtUtc, CancellationToken ct)
    {
        if (!IsConfigured) return PspVerdict.Unknown;

        if (!Cfg.HasApiUser)
        {
            log.LogWarning(
                "OnePay chưa có tài khoản API (Psp:Onepay:ApiUser) nên không tự hỏi lại được về {Ref}.",
                orderRef);
            return PspVerdict.Unknown;
        }

        var fields = new Dictionary<string, string>
        {
            ["vpc_Version"] = "1",
            ["vpc_Command"] = "queryDR",
            ["vpc_AccessCode"] = Cfg.AccessCode,
            ["vpc_Merchant"] = Cfg.Merchant,
            ["vpc_MerchTxnRef"] = orderRef,
            ["vpc_User"] = Cfg.ApiUser,
            ["vpc_Password"] = Cfg.ApiPassword
        };

        // Asked on the international side first, then the domestic one. Each is
        // fuller about its own transactions, and this method is not told which
        // kind it is looking at — the session only carries a reference.
        var answer = await AskAsync(Cfg.ApiUrl, fields, ct);

        if (answer is null || answer.GetValueOrDefault("vpc_DRExists") == "N")
            answer = await AskAsync(Cfg.DomesticApiUrl, fields, ct) ?? answer;

        if (answer is null) return PspVerdict.Unknown;

        var code = answer.GetValueOrDefault("vpc_TxnResponseCode") ?? "";
        var txn = answer.GetValueOrDefault("vpc_TransactionNo");

        var amount = decimal.TryParse(answer.GetValueOrDefault("vpc_Amount"), out var raw)
            ? raw / 100m
            : 0m;

        // They say outright whether they have heard of the order. "N" is not an
        // answer about the money — the guest may still be typing their card on
        // OnePay's page — so it must not be read as a failure.
        if (answer.GetValueOrDefault("vpc_DRExists") == "N") return PspVerdict.Unknown;
        if (code.Length == 0) return PspVerdict.Unknown;

        if (Psp.OnePayPaid(code))
            return new PspVerdict(PaymentSessionStatus.Paid, amount, txn, code,
                // Their own two spellings: the browser return says vpc_CardNum,
                // this API says vpc_CardNo. Reading only one leaves the last
                // four digits null on whichever path was not tested.
                CardLast4: Psp.OnePayLast4(answer.GetValueOrDefault("vpc_CardNo")
                                           ?? answer.GetValueOrDefault("vpc_CardNum")),
                CardType: answer.GetValueOrDefault("vpc_AdditionData")
                          ?? answer.GetValueOrDefault("vpc_Card"));

        return new PspVerdict(PaymentSessionStatus.Failed, amount, txn, code, Psp.OnePayDecline(code));
    }

    /// <summary>
    /// docs/07 §10 — the money back to the card it came from.
    ///
    /// <b>Unverified against a live gateway.</b> Their <c>queryDR</c> on the same
    /// endpoint needs no signature and answers happily with the demo operator
    /// account; <c>refund</c> demands a <c>vpc_SecureHash</c> that the payment
    /// secret does not produce — every rule that signs a payment correctly comes
    /// back "Invalid vpc_SecureHash" (255), and the domestic endpoint, which does
    /// accept that signature, has no refund command at all. The likeliest reading
    /// is that a public demo merchant is not granted refunds, which is sensible
    /// of them. So this is written to their documented shape and left honest: a
    /// call that does not land is Unknown, never Refused.
    ///
    /// The reference sent is the original payment's, which is what makes a
    /// repeat safe: OnePay matches a refund to the transaction it reverses, so a
    /// retry after a lost reply is not a second refund. A call that never landed
    /// is <em>Unknown</em>, never Refused — the difference decides whether the
    /// guest is paid back to their card or to their Staylio balance, and
    /// getting it wrong pays them twice.
    /// </summary>
    public async Task<PspRefundResult> RefundAsync(PspRefund refund, CancellationToken ct)
    {
        if (!IsConfigured) return new PspRefundResult(Psp.RefundOutcome.Unknown);

        if (!Cfg.HasApiUser)
        {
            log.LogError(
                "OnePay chưa có tài khoản API nên không hoàn được {Amount} cho {Ref}.",
                refund.Amount, refund.OrderRef);
            return new PspRefundResult(Psp.RefundOutcome.Unknown);
        }

        var fields = new Dictionary<string, string>
        {
            ["vpc_Version"] = "1",
            ["vpc_Command"] = "refund",
            ["vpc_AccessCode"] = Cfg.AccessCode,
            ["vpc_Merchant"] = Cfg.Merchant,
            ["vpc_MerchTxnRef"] = refund.OrderRef,
            ["vpc_Amount"] = Psp.OnePayAmount(refund.Amount).ToString(),
            ["vpc_User"] = Cfg.ApiUser,
            ["vpc_Password"] = Cfg.ApiPassword
        };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var answer = await AskAsync(Cfg.ApiUrl, fields, ct);

            if (answer is not null)
            {
                var code = answer.GetValueOrDefault("vpc_TxnResponseCode");
                var outcome = Psp.OnePayRefundOutcome(code);

                log.LogInformation("OnePay hoàn {Amount} cho {Ref}: mã {Code} → {Outcome}.",
                    refund.Amount, refund.OrderRef, code, outcome);

                if (outcome != Psp.RefundOutcome.Unknown)
                    return new PspRefundResult(outcome, answer.GetValueOrDefault("vpc_TransactionNo"), code);
            }

            if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
        }

        log.LogError("Không biết OnePay đã hoàn {Amount} cho {Ref} hay chưa.",
            refund.Amount, refund.OrderRef);

        return new PspRefundResult(Psp.RefundOutcome.Unknown);
    }

    /// <summary>
    /// One call to their operator API. It answers a URL-encoded form rather than
    /// JSON, which is why this parses a query string and not a document.
    /// </summary>
    private async Task<Dictionary<string, string>?> AskAsync(
        string endpoint, Dictionary<string, string> fields, CancellationToken ct)
    {
        var signed = new Dictionary<string, string>(fields)
        {
            ["vpc_SecureHash"] = Psp.OnePaySign(fields, Cfg.HashSecret)
        };

        try
        {
            using var client = http.CreateClient("psp");
            var res = await client.PostAsync(endpoint, new FormUrlEncodedContent(signed), ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            // ParseQuery never throws, so an error page comes back as a
            // dictionary of nonsense with no vpc_TxnResponseCode in it rather
            // than as a failure. The status is the only thing that tells the
            // two apart, and "not knowing" has to stay not knowing.
            if (!res.IsSuccessStatusCode)
                throw new GatewayCallException(
                    $"OnePay trả HTTP {(int)res.StatusCode} ({res.ReasonPhrase}): {GatewayReply.Snippet(body)}");

            return Microsoft.AspNetCore.WebUtilities.QueryHelpers
                .ParseQuery(body.StartsWith('?') ? body : "?" + body)
                .ToDictionary(p => p.Key, p => p.Value.ToString());
        }
        catch (Exception ex)
        {
            // Not knowing is not the same as "not paid", and not the same as
            // "not refunded" either. The caller retries or leaves it pending.
            log.LogWarning(ex, "Không gọi được API của OnePay ({Command}).",
                fields.GetValueOrDefault("vpc_Command"));
            return null;
        }
    }
}
