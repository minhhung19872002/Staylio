using System.Text.Json;
using Microsoft.Extensions.Options;
using StayHost.Domain;

namespace StayHost.Web.Services.Gateways;

/// <summary>
/// docs/07 §13 phương án A — ZaloPay, the "ZaloPay" row of the checkout.
///
/// Two things about this one are unlike the other two and both have bitten
/// people before: the order id must carry today's date <em>in Vietnam</em>
/// (<see cref="Psp.ZaloTransId"/>), and the callback is signed with a second key
/// that is not the one signing outgoing calls.
/// </summary>
public class ZaloPayProvider(
    IOptions<PspSettings> options, IHttpClientFactory http, ILogger<ZaloPayProvider> log)
    : IPspProvider
{
    private readonly PspSettings _psp = options.Value;
    private PspSettings.ZaloPayOptions Cfg => _psp.Zalopay;

    public string Key => Psp.ZaloPay;
    public bool IsConfigured => Cfg.IsConfigured;

    public async Task<PspStart> StartAsync(PspOrder order, CancellationToken ct)
    {
        if (!IsConfigured) return new PspStart(false, Error: "ZaloPay chưa được cấu hình.");

        var now = DateTime.UtcNow;
        var transId = Psp.ZaloTransId(order.OrderRef, now);
        var amount = (long)Math.Round(order.Amount, MidpointRounding.AwayFromZero);
        var appTime = Psp.ZaloTime(now);

        // The address the guest is bounced back to after the ZaloPay app is done.
        // Sent inside embed_data because ZaloPay has no field of its own for it.
        var embed = JsonSerializer.Serialize(new
        {
            redirecturl = $"{_psp.PublicUrl}/api/payments/zalopay/return?ref={order.OrderRef}"
        });

        var item = JsonSerializer.Serialize(new[]
        {
            new { itemid = order.OrderRef, itemname = order.Description, itemprice = amount, itemquantity = 1 }
        });

        var mac = Psp.ZaloCreateMac(Cfg.Key1, Cfg.AppId, transId, "stayhost", amount, appTime, embed, item);

        var form = new Dictionary<string, string>
        {
            ["app_id"] = Cfg.AppId,
            ["app_trans_id"] = transId,
            ["app_user"] = "stayhost",
            ["app_time"] = appTime.ToString(),
            ["amount"] = amount.ToString(),
            ["item"] = item,
            ["embed_data"] = embed,
            ["description"] = order.Description,
            ["bank_code"] = "",
            ["callback_url"] = $"{_psp.PublicUrl}/api/payments/zalopay/callback",
            ["mac"] = mac
        };

        try
        {
            using var client = http.CreateClient("psp");
            var res = await client.PostAsync($"{Cfg.Endpoint}/create", new FormUrlEncodedContent(form), ct);
            var json = await GatewayReply.JsonAsync(res, "ZaloPay", ct);

            var returnCode = Number(json, "return_code");
            var url = Text(json, "order_url");

            if (Psp.ZaloPaid(returnCode) && url.Length > 0) return new PspStart(true, url);

            log.LogWarning("ZaloPay từ chối mở đơn {Ref}: {Code} {Message}.",
                transId, returnCode, Text(json, "return_message"));

            return new PspStart(false, Error: Payments.Message(DeclineReason.GatewayError));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Không gọi được ZaloPay cho đơn {Ref}.", transId);
            return new PspStart(false, Error: Payments.Message(DeclineReason.GatewayError));
        }
    }

    /// <summary>
    /// The server-to-server callback. Its body is <c>{data, mac}</c>, and the
    /// data is a JSON string that must be hashed exactly as received — parsing
    /// and re-serialising it would change a space and break the check.
    /// </summary>
    public PspVerdict Read(IReadOnlyDictionary<string, string> payload)
    {
        var data = payload.GetValueOrDefault("data") ?? "";
        var mac = payload.GetValueOrDefault("mac") ?? "";

        if (data.Length == 0 || !Psp.ZaloCallbackValid(Cfg.Key2, data, mac))
        {
            log.LogWarning("ZaloPay callback failed its signature check.");
            return PspVerdict.Forged;
        }

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(data);

            return new PspVerdict(
                PaymentSessionStatus.Paid,
                Number(json, "amount"),
                Text(json, "zp_trans_id"),
                "1");
        }
        catch (JsonException)
        {
            return PspVerdict.Forged;
        }
    }

    /// <summary>
    /// docs/07 §5 — the question that makes the redirect safe. ZaloPay's own
    /// documentation says to treat this, not the browser, as the answer.
    /// </summary>
    public async Task<PspVerdict> QueryAsync(string orderRef, DateTime createdAtUtc, CancellationToken ct)
    {
        if (!IsConfigured) return PspVerdict.Unknown;

        // The date prefix has to be the one the order was created under, not
        // today's: a payment opened at 23:58 is queried the next morning.
        var transId = Psp.ZaloTransId(orderRef, createdAtUtc);
        var mac = Psp.ZaloQueryMac(Cfg.Key1, Cfg.AppId, transId);

        var form = new Dictionary<string, string>
        {
            ["app_id"] = Cfg.AppId,
            ["app_trans_id"] = transId,
            ["mac"] = mac
        };

        try
        {
            using var client = http.CreateClient("psp");
            var res = await client.PostAsync($"{Cfg.Endpoint}/query", new FormUrlEncodedContent(form), ct);
            var json = await GatewayReply.JsonAsync(res, "ZaloPay", ct);

            var returnCode = Number(json, "return_code");
            var amount = Number(json, "amount");
            var txn = Text(json, "zp_trans_id");

            if (Psp.ZaloPaid(returnCode))
                return new PspVerdict(PaymentSessionStatus.Paid, amount, txn, returnCode.ToString());

            if (Psp.ZaloPending(returnCode)) return PspVerdict.Unknown;

            return new PspVerdict(PaymentSessionStatus.Failed, amount, txn, returnCode.ToString(),
                DeclineReason.BankRefused);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Không hỏi được ZaloPay về đơn {Ref}.", transId);
            return PspVerdict.Unknown;
        }
    }

    /// <summary>
    /// docs/07 §10 — ZaloPay sends it back to the wallet it came from.
    ///
    /// Their refund is the one of the three that is genuinely asynchronous:
    /// <c>return_code</c> 3 means "still working on it", which is neither a
    /// refusal nor a completion and must not be read as either.
    /// </summary>
    public async Task<PspRefundResult> RefundAsync(PspRefund refund, CancellationToken ct)
    {
        if (!IsConfigured) return new PspRefundResult(Psp.RefundOutcome.Unknown);
        if (string.IsNullOrWhiteSpace(refund.ProviderTxnId))
            return new PspRefundResult(Psp.RefundOutcome.Unknown);

        var amount = (long)Math.Round(refund.Amount, MidpointRounding.AwayFromZero);
        var now = DateTime.UtcNow;
        var refundId = Psp.ZaloRefundId(Cfg.AppId, refund.OrderRef, now);
        var stamp = Psp.ZaloTime(now);

        var form = new Dictionary<string, string>
        {
            ["app_id"] = Cfg.AppId,
            ["m_refund_id"] = refundId,
            ["zp_trans_id"] = refund.ProviderTxnId!,
            ["amount"] = amount.ToString(),
            ["timestamp"] = stamp.ToString(),
            ["description"] = refund.Reason,
            ["mac"] = Psp.ZaloRefundMac(Cfg.Key1, Cfg.AppId, refund.ProviderTxnId!, amount,
                refund.Reason, stamp)
        };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var client = http.CreateClient("psp");
                var res = await client.PostAsync($"{Cfg.Endpoint}/refund",
                    new FormUrlEncodedContent(form), ct);
                var json = await GatewayReply.JsonAsync(res, "ZaloPay", ct);
                var code = Number(json, "return_code");

                log.LogInformation("ZaloPay hoàn {Amount} cho {Ref}: mã {Code} {Message}.",
                    refund.Amount, refund.OrderRef, code, Text(json, "return_message"));

                // 1 done, 3 still processing — both mean the money is coming back.
                if (code is 1 or 3)
                    return new PspRefundResult(Psp.RefundOutcome.Accepted, refundId, code.ToString());
                if (code == 2)
                    return new PspRefundResult(Psp.RefundOutcome.Refused, Code: code.ToString());
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Không gọi được ZaloPay để hoàn tiền {Ref} (lần {Attempt}).",
                    refund.OrderRef, attempt);
            }

            if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
        }

        log.LogError("Không biết ZaloPay đã hoàn {Amount} cho {Ref} hay chưa. Mã hoàn {RefundId}.",
            refund.Amount, refund.OrderRef, refundId);

        return new PspRefundResult(Psp.RefundOutcome.Unknown, Code: refundId);
    }

    private static string Text(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.Number ? v.ToString() : v.GetString() ?? ""
            : "";

    private static int Number(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? (int)n
            : -1;
}
