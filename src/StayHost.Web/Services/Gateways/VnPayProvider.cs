using System.Text.Json;
using Microsoft.Extensions.Options;
using StayHost.Domain;

namespace StayHost.Web.Services.Gateways;

/// <summary>
/// docs/07 §13 phương án A — VNPay, the one gateway that covers both rows at the
/// top of the checkout: an international card (Visa / Mastercard / JCB / Amex)
/// and a domestic ATM card over NAPAS.
///
/// Nothing here ever sees a card number. The guest leaves for VNPay's own page,
/// types it there, and comes back with a signed answer — which is what docs/07
/// §14.1–2 requires and what the hand-written card form in this build was not.
/// </summary>
public class VnPayProvider(
    IOptions<PspSettings> options, IHttpClientFactory http, ILogger<VnPayProvider> log)
    : IPspProvider
{
    private readonly PspSettings _psp = options.Value;
    private PspSettings.VnPayOptions Cfg => _psp.Vnpay;

    public string Key => Psp.VnPay;
    public bool IsConfigured => Cfg.IsConfigured;

    /// <summary>
    /// VNPay's own name for "let the guest pick" versus the two shortcuts the
    /// checkout already asked for. Sending the guest straight to the right list
    /// is the difference between the two rows meaning something and both of them
    /// opening the same menu.
    /// </summary>
    private static string BankCode(string method) => method switch
    {
        "napas" => "VNBANK",   // domestic ATM / internet banking
        "card" => "INTCARD",   // Visa, Mastercard, JCB, Amex
        _ => ""
    };

    /// <summary>docs/07 §4 — whether this build may offer to keep a card at VNPay.</summary>
    public bool TokensEnabled => IsConfigured && Cfg.Tokens;

    public Task<PspStart> StartAsync(PspOrder order, CancellationToken ct)
    {
        if (!IsConfigured)
            return Task.FromResult(new PspStart(false, Error: "VNPay chưa được cấu hình."));

        // docs/07 §4 — keeping a card, or paying with one already kept, is a
        // different API on a different path with every parameter spelled
        // differently. Same secret, same sorted-query checksum.
        if (TokensEnabled && (order.SaveCard || order.Token is { Length: > 0 }))
            return Task.FromResult(StartToken(order));

        var now = Psp.Vn(DateTime.UtcNow);

        var fields = new Dictionary<string, string>
        {
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = Cfg.TmnCode,
            ["vnp_Amount"] = Psp.VnPayAmount(order.Amount).ToString(),
            ["vnp_CurrCode"] = "VND",
            ["vnp_TxnRef"] = order.OrderRef,
            ["vnp_OrderInfo"] = order.Description,
            ["vnp_OrderType"] = "other",
            ["vnp_Locale"] = "vn",
            ["vnp_ReturnUrl"] = $"{_psp.PublicUrl}/api/payments/vnpay/return",
            ["vnp_IpAddr"] = order.ClientIp,
            ["vnp_CreateDate"] = now.ToString("yyyyMMddHHmmss"),
            // The gateway closes its own window at the same moment the booking
            // stops holding the dates, so a guest cannot pay for a stay that has
            // already gone back on sale.
            ["vnp_ExpireDate"] = now.Add(PaymentSession.Window).ToString("yyyyMMddHHmmss")
        };

        var bank = BankCode(order.Method);
        if (bank.Length > 0) fields["vnp_BankCode"] = bank;

        var query = Psp.VnPayQuery(fields);
        var hash = Psp.VnPaySign(fields, Cfg.HashSecret);

        return Task.FromResult(new PspStart(true, $"{Cfg.PayUrl}?{query}&vnp_SecureHash={hash}"));
    }

    /// <summary>
    /// docs/07 §4 — pay, and keep the card at VNPay for next time. Or pay with
    /// one already kept.
    ///
    /// Every parameter name here is lower-case with underscores. That is not a
    /// typo carried over from somewhere: VNPay's token API genuinely spells them
    /// differently from its payment API, and mixing the two gets a blank error
    /// page with no reason on it. The checksum is the same sorted query — which
    /// their documentation does not say, and which was settled by sending their
    /// sandbox one request per candidate rule and seeing which reached a payment
    /// page rather than <c>error.html</c>.
    /// </summary>
    private PspStart StartToken(PspOrder order)
    {
        var now = Psp.Vn(DateTime.UtcNow);
        var back = $"{_psp.PublicUrl}/api/payments/vnpay/token-return";

        var fields = new Dictionary<string, string>
        {
            ["vnp_version"] = "2.1.0",
            ["vnp_command"] = order.Token is { Length: > 0 }
                ? Psp.VnPayTokenPayCommand
                : Psp.VnPayCreateTokenCommand,
            ["vnp_tmn_code"] = Cfg.TmnCode,
            ["vnp_app_user_id"] = order.UserRef ?? "guest",
            ["vnp_locale"] = "vn",
            ["vnp_card_type"] = Psp.VnPayCardType(order.Method),
            ["vnp_txn_ref"] = order.OrderRef,
            ["vnp_amount"] = Psp.VnPayAmount(order.Amount).ToString(),
            ["vnp_curr_code"] = "VND",
            ["vnp_txn_desc"] = order.Description,
            ["vnp_return_url"] = back,
            ["vnp_cancel_url"] = back,
            ["vnp_ip_addr"] = order.ClientIp,
            ["vnp_create_date"] = now.ToString("yyyyMMddHHmmss"),
            ["vnp_store_token"] = "1"
        };

        if (order.Token is { Length: > 0 }) fields["vnp_token"] = order.Token;

        var url = order.Token is { Length: > 0 } ? Cfg.TokenPayUrl : Cfg.CreateTokenUrl;

        return new PspStart(true,
            $"{url}?{Psp.VnPayQuery(fields)}&vnp_secure_hash={Psp.VnPaySign(fields, Cfg.HashSecret)}");
    }

    public PspVerdict Read(IReadOnlyDictionary<string, string> payload)
    {
        if (!Psp.VnPayVerify(payload, Cfg.HashSecret))
        {
            log.LogWarning("VNPay callback for {Ref} failed its signature check.",
                payload.GetValueOrDefault("vnp_TxnRef") ?? payload.GetValueOrDefault("vnp_txn_ref"));
            return PspVerdict.Forged;
        }

        // The token API answers in its own spelling, so every field is looked up
        // under both. One Read for both APIs, because everything after the
        // spelling is identical and two copies would drift.
        string? Field(string pay, string token) =>
            payload.GetValueOrDefault(pay) ?? payload.GetValueOrDefault(token);

        var code = Field("vnp_ResponseCode", "vnp_response_code") ?? "";
        var status = Field("vnp_TransactionStatus", "vnp_transaction_status") ?? code;
        var txn = Field("vnp_TransactionNo", "vnp_transaction_no");

        // vnp_Amount comes back in the same ×100 unit it was sent in.
        var amount = decimal.TryParse(Field("vnp_Amount", "vnp_amount"), out var raw)
            ? raw / 100m
            : 0m;

        // docs/07 §4 — the only place this platform ever learns four digits of a
        // card, now that the number is typed on VNPay's page (§14.2).
        var last4 = Psp.Last4Of(payload.GetValueOrDefault("vnp_card_number"));
        var token = payload.GetValueOrDefault("vnp_token");
        var cardType = payload.GetValueOrDefault("vnp_card_type");

        if (code == "00" && status == "00")
            return new PspVerdict(PaymentSessionStatus.Paid, amount, txn, code,
                CardLast4: last4, CardToken: token, CardType: cardType,
                PaidAt: Field("vnp_PayDate", "vnp_pay_date"));

        if (Psp.VnPayCancelled(code))
            return new PspVerdict(PaymentSessionStatus.Cancelled, amount, txn, code);

        return new PspVerdict(PaymentSessionStatus.Failed, amount, txn, code, Psp.VnPayDecline(code));
    }

    /// <summary>
    /// docs/07 §5 / TC-P-05 — querydr. The guest never came back, so VNPay is
    /// asked directly whether the money moved.
    /// </summary>
    public async Task<PspVerdict> QueryAsync(string orderRef, DateTime createdAtUtc, CancellationToken ct)
    {
        if (!IsConfigured) return PspVerdict.Unknown;

        var now = Psp.Vn(DateTime.UtcNow);
        var requestId = $"q{now:yyMMddHHmmssfff}";
        var created = Psp.Vn(createdAtUtc).ToString("yyyyMMddHHmmss");
        const string version = "2.1.0";
        const string command = "querydr";
        const string info = "Kiem tra giao dich";

        // A different checksum shape from the payment one: pipe-joined, fixed order.
        var checksum = Psp.VnPayApiSign(Cfg.HashSecret,
            requestId, version, command, Cfg.TmnCode, orderRef, created,
            now.ToString("yyyyMMddHHmmss"), "127.0.0.1", info);

        var body = new Dictionary<string, string>
        {
            ["vnp_RequestId"] = requestId,
            ["vnp_Version"] = version,
            ["vnp_Command"] = command,
            ["vnp_TmnCode"] = Cfg.TmnCode,
            ["vnp_TxnRef"] = orderRef,
            ["vnp_OrderInfo"] = info,
            ["vnp_TransactionDate"] = created,
            ["vnp_CreateDate"] = now.ToString("yyyyMMddHHmmss"),
            ["vnp_IpAddr"] = "127.0.0.1",
            ["vnp_SecureHash"] = checksum
        };

        try
        {
            using var client = http.CreateClient("psp");
            var res = await client.PostAsJsonAsync(Cfg.ApiUrl, body, ct);
            var json = await GatewayReply.JsonAsync(res, "VNPay", ct);

            var code = Text(json, "vnp_ResponseCode");
            var status = Text(json, "vnp_TransactionStatus");
            var amount = decimal.TryParse(Text(json, "vnp_Amount"), out var raw) ? raw / 100m : 0m;

            // 91 is "we have never heard of this order" — the guest never got as
            // far as paying. 94 means the query itself was duplicated; neither is
            // an answer about the money.
            if (code is "91" or "94") return PspVerdict.Unknown;

            if (code == "00" && status == "00")
                return new PspVerdict(PaymentSessionStatus.Paid, amount, Text(json, "vnp_TransactionNo"), code);

            if (status == "01") return PspVerdict.Unknown;    // still in progress

            return new PspVerdict(PaymentSessionStatus.Failed, amount,
                Text(json, "vnp_TransactionNo"), code, Psp.VnPayDecline(status));
        }
        catch (Exception ex)
        {
            // Not knowing is not the same as "not paid". A booking stays pending
            // and gets asked about again rather than being failed on a timeout.
            log.LogWarning(ex, "Không hỏi được VNPay về giao dịch {Ref}.", orderRef);
            return PspVerdict.Unknown;
        }
    }

    /// <summary>
    /// docs/07 §10 — asks VNPay to send the money back to the card it came from.
    ///
    /// Retried on a lost reply rather than given up on, and retried under the
    /// <em>same</em> request id: VNPay recognises a repeat and answers 94 rather
    /// than refunding twice, which is what makes retrying safe at all.
    /// </summary>
    public async Task<PspRefundResult> RefundAsync(PspRefund refund, CancellationToken ct)
    {
        if (!IsConfigured) return new PspRefundResult(Psp.RefundOutcome.Unknown);

        var now = Psp.Vn(DateTime.UtcNow);
        var requestId = Psp.RefundRequestId(refund.OrderRef, refund.Amount, DateTime.UtcNow);
        var amount = Psp.VnPayAmount(refund.Amount).ToString();
        var type = Psp.VnPayRefundType(refund.Amount, refund.OriginalAmount);
        var txnNo = refund.ProviderTxnId ?? "";
        var paidAt = refund.PaidAt ?? Psp.Vn(refund.CreatedAtUtc).ToString("yyyyMMddHHmmss");
        var created = now.ToString("yyyyMMddHHmmss");
        const string version = "2.1.0";
        const string command = "refund";
        var info = refund.Reason;

        var checksum = Psp.VnPayRefundSign(Cfg.HashSecret, requestId, version, command, Cfg.TmnCode,
            type, refund.OrderRef, amount, txnNo, paidAt, refund.By, created, "127.0.0.1", info);

        var body = new Dictionary<string, string>
        {
            ["vnp_RequestId"] = requestId,
            ["vnp_Version"] = version,
            ["vnp_Command"] = command,
            ["vnp_TmnCode"] = Cfg.TmnCode,
            ["vnp_TransactionType"] = type,
            ["vnp_TxnRef"] = refund.OrderRef,
            ["vnp_Amount"] = amount,
            ["vnp_OrderInfo"] = info,
            ["vnp_TransactionNo"] = txnNo,
            ["vnp_TransactionDate"] = paidAt,
            ["vnp_CreateBy"] = refund.By,
            ["vnp_CreateDate"] = created,
            ["vnp_IpAddr"] = "127.0.0.1",
            ["vnp_SecureHash"] = checksum
        };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var client = http.CreateClient("psp");
                var res = await client.PostAsJsonAsync(Cfg.ApiUrl, body, ct);
                var json = await GatewayReply.JsonAsync(res, "VNPay", ct);

                var code = Text(json, "vnp_ResponseCode");
                var status = Text(json, "vnp_TransactionStatus");
                var outcome = Psp.VnPayRefundOutcome(code, status);

                log.LogInformation(
                    "VNPay hoàn {Amount} cho {Ref}: mã {Code}, trạng thái {Status} → {Outcome}.",
                    refund.Amount, refund.OrderRef, code, status, outcome);

                if (outcome != Psp.RefundOutcome.Unknown)
                    return new PspRefundResult(outcome, Text(json, "vnp_TransactionNo"), code);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Không gọi được VNPay để hoàn tiền {Ref} (lần {Attempt}).",
                    refund.OrderRef, attempt);
            }

            if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
        }

        // Every attempt carried the same request id, so a refund that did land is
        // recorded at VNPay under it — which is what a person needs to look it up
        // in the merchant portal.
        log.LogError("Không biết VNPay đã hoàn {Amount} cho {Ref} hay chưa. Mã yêu cầu {RequestId}.",
            refund.Amount, refund.OrderRef, requestId);

        return new PspRefundResult(Psp.RefundOutcome.Unknown, Code: requestId);
    }

    /// <summary>
    /// docs/07 §15.5 — the guest deleted the card here, so VNPay is told to stop
    /// holding it too.
    ///
    /// Their remove endpoint is not one of the token pages the guest is sent to:
    /// it answers a query string server-to-server. Same lower-case-with-
    /// underscores spelling as the rest of the token API, and the same
    /// sorted-query signature — established the same way, by sending their
    /// sandbox one and reading what came back rather than trusting a manual.
    ///
    /// The platform deletes its own row whether or not this succeeds. A guest
    /// who asked to remove a card should not be told "no" because someone else's
    /// server is down, and a token left behind at VNPay is inert: only this
    /// merchant's secret can use it, and only with the cardholder present.
    /// </summary>
    public async Task<bool> ForgetCardAsync(string token, string userRef, CancellationToken ct)
    {
        if (!IsConfigured || token.Length == 0) return false;

        var now = Psp.Vn(DateTime.UtcNow);

        var fields = new Dictionary<string, string>
        {
            ["vnp_version"] = "2.1.0",
            ["vnp_command"] = Psp.VnPayRemoveTokenCommand,
            ["vnp_tmn_code"] = Cfg.TmnCode,
            ["vnp_app_user_id"] = userRef,
            ["vnp_token"] = token,
            ["vnp_txn_ref"] = $"rm{now:yyMMddHHmmssfff}",
            ["vnp_create_date"] = now.ToString("yyyyMMddHHmmss"),
            ["vnp_ip_addr"] = "127.0.0.1",
            ["vnp_locale"] = "vn"
        };

        var url = $"{Cfg.RemoveTokenUrl}?{Psp.VnPayQuery(fields)}"
                  + $"&vnp_secure_hash={Psp.VnPaySign(fields, Cfg.HashSecret)}";

        try
        {
            using var client = http.CreateClient("psp");
            var body = await client.GetStringAsync(url, ct);

            var answer = Microsoft.AspNetCore.WebUtilities.QueryHelpers
                .ParseQuery(body.StartsWith('?') ? body : "?" + body);

            var code = answer.TryGetValue("vnp_response_code", out var c) ? c.ToString() : "";
            var forgotten = Psp.VnPayTokenForgotten(code);

            if (forgotten)
                log.LogInformation("VNPay đã bỏ thẻ đã lưu của {User} (mã {Code}).", userRef, code);
            else
                log.LogWarning("VNPay không bỏ được thẻ đã lưu của {User}: mã {Code} — {Message}.",
                    userRef, code,
                    answer.TryGetValue("vnp_message", out var m) ? m.ToString() : "");

            return forgotten;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Không gọi được VNPay để bỏ thẻ đã lưu của {User}.", userRef);
            return false;
        }
    }

    private static string Text(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.Number ? v.ToString() : v.GetString() ?? ""
            : "";
}
