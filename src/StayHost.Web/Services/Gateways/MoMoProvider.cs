using System.Text.Json;
using Microsoft.Extensions.Options;
using StayHost.Domain;

namespace StayHost.Web.Services.Gateways;

/// <summary>
/// docs/07 §13 phương án A — MoMo, the "Ví MoMo" row of the checkout.
///
/// Unlike VNPay this one is a server-to-server call first: the platform asks
/// MoMo to open an order and gets back the address to send the guest to. That
/// means a failure is known here, before the guest is redirected anywhere,
/// rather than as a page they land on.
/// </summary>
public class MoMoProvider(
    IOptions<PspSettings> options, IHttpClientFactory http, ILogger<MoMoProvider> log)
    : IPspProvider
{
    private readonly PspSettings _psp = options.Value;
    private PspSettings.MoMoOptions Cfg => _psp.Momo;

    public string Key => Psp.MoMo;
    public bool IsConfigured => Cfg.IsConfigured;

    public async Task<PspStart> StartAsync(PspOrder order, CancellationToken ct)
    {
        if (!IsConfigured) return new PspStart(false, Error: "MoMo chưa được cấu hình.");

        var amount = (long)Math.Round(order.Amount, MidpointRounding.AwayFromZero);
        var requestId = order.OrderRef;
        var redirect = $"{_psp.PublicUrl}/api/payments/momo/return";
        var ipn = $"{_psp.PublicUrl}/api/payments/momo/ipn";
        const string requestType = "captureWallet";

        var signature = Psp.MoMoCreateSign(
            Cfg.AccessKey, Cfg.SecretKey, amount, "", ipn, order.OrderRef,
            order.Description, Cfg.PartnerCode, redirect, requestId, requestType);

        var body = new Dictionary<string, object>
        {
            ["partnerCode"] = Cfg.PartnerCode,
            ["partnerName"] = "Staylio",
            ["storeId"] = "Staylio",
            ["requestId"] = requestId,
            ["amount"] = amount,
            ["orderId"] = order.OrderRef,
            ["orderInfo"] = order.Description,
            ["redirectUrl"] = redirect,
            ["ipnUrl"] = ipn,
            ["lang"] = "vi",
            ["extraData"] = "",
            ["requestType"] = requestType,
            ["autoCapture"] = true,
            ["signature"] = signature
        };

        try
        {
            using var client = http.CreateClient("psp");
            var res = await client.PostAsJsonAsync($"{Cfg.Endpoint}/create", body, ct);
            var json = await GatewayReply.JsonAsync(res, "MoMo", ct);

            var resultCode = Number(json, "resultCode");
            var payUrl = Text(json, "payUrl");

            if (resultCode == 0 && payUrl.Length > 0) return new PspStart(true, payUrl);

            log.LogWarning("MoMo từ chối mở đơn {Ref}: {Code} {Message}.",
                order.OrderRef, resultCode, Text(json, "message"));

            // The guest is told the module's own wording for the reason, never
            // MoMo's code — docs/07 §8, last line.
            return new PspStart(false, Error: Payments.Message(Psp.MoMoDecline(resultCode)));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Không gọi được MoMo cho đơn {Ref}.", order.OrderRef);
            return new PspStart(false, Error: Payments.Message(DeclineReason.GatewayError));
        }
    }

    public PspVerdict Read(IReadOnlyDictionary<string, string> payload)
    {
        var expected = Psp.MoMoResultSign(
            Cfg.AccessKey, Cfg.SecretKey,
            long.TryParse(payload.GetValueOrDefault("amount"), out var amt) ? amt : 0,
            payload.GetValueOrDefault("extraData") ?? "",
            payload.GetValueOrDefault("message") ?? "",
            payload.GetValueOrDefault("orderId") ?? "",
            payload.GetValueOrDefault("orderInfo") ?? "",
            payload.GetValueOrDefault("orderType") ?? "",
            payload.GetValueOrDefault("partnerCode") ?? "",
            payload.GetValueOrDefault("payType") ?? "",
            payload.GetValueOrDefault("requestId") ?? "",
            payload.GetValueOrDefault("responseTime") ?? "",
            payload.GetValueOrDefault("resultCode") ?? "",
            payload.GetValueOrDefault("transId") ?? "");

        if (!string.Equals(expected, payload.GetValueOrDefault("signature"), StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning("MoMo callback for {Ref} failed its signature check.",
                payload.GetValueOrDefault("orderId"));
            return PspVerdict.Forged;
        }

        var code = int.TryParse(payload.GetValueOrDefault("resultCode"), out var rc) ? rc : -1;
        var txn = payload.GetValueOrDefault("transId");

        if (Psp.MoMoPaid(code)) return new PspVerdict(PaymentSessionStatus.Paid, amt, txn, code.ToString());
        if (Psp.MoMoPending(code)) return PspVerdict.Unknown;
        if (Psp.MoMoCancelled(code)) return new PspVerdict(PaymentSessionStatus.Cancelled, amt, txn, code.ToString());

        return new PspVerdict(PaymentSessionStatus.Failed, amt, txn, code.ToString(), Psp.MoMoDecline(code));
    }

    /// <summary>docs/07 §5 — MoMo's own answer, which outranks the browser's.</summary>
    public async Task<PspVerdict> QueryAsync(string orderRef, DateTime createdAtUtc, CancellationToken ct)
    {
        if (!IsConfigured) return PspVerdict.Unknown;

        var requestId = $"q{Psp.Vn(DateTime.UtcNow):yyMMddHHmmssfff}";
        var signature = Psp.MoMoQuerySign(Cfg.AccessKey, Cfg.SecretKey, orderRef, Cfg.PartnerCode, requestId);

        var body = new Dictionary<string, object>
        {
            ["partnerCode"] = Cfg.PartnerCode,
            ["requestId"] = requestId,
            ["orderId"] = orderRef,
            ["lang"] = "vi",
            ["signature"] = signature
        };

        try
        {
            using var client = http.CreateClient("psp");
            var res = await client.PostAsJsonAsync($"{Cfg.Endpoint}/query", body, ct);
            var json = await GatewayReply.JsonAsync(res, "MoMo", ct);

            var code = Number(json, "resultCode");
            var amount = Number(json, "amount");
            var txn = Text(json, "transId");

            if (Psp.MoMoPaid(code))
                return new PspVerdict(PaymentSessionStatus.Paid, amount, txn, code.ToString());

            if (Psp.MoMoPending(code)) return PspVerdict.Unknown;

            if (Psp.MoMoCancelled(code))
                return new PspVerdict(PaymentSessionStatus.Cancelled, amount, txn, code.ToString());

            return new PspVerdict(PaymentSessionStatus.Failed, amount, txn, code.ToString(), Psp.MoMoDecline(code));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Không hỏi được MoMo về đơn {Ref}.", orderRef);
            return PspVerdict.Unknown;
        }
    }

    /// <summary>
    /// docs/07 §10 — MoMo sends it back to the wallet it came from.
    ///
    /// The refund carries a new <c>orderId</c> of its own but reuses the original
    /// <c>transId</c>, and the id is derived rather than random so a retry after
    /// a lost reply is the same refund rather than a second one.
    /// </summary>
    public async Task<PspRefundResult> RefundAsync(PspRefund refund, CancellationToken ct)
    {
        if (!IsConfigured) return new PspRefundResult(Psp.RefundOutcome.Unknown);
        if (string.IsNullOrWhiteSpace(refund.ProviderTxnId))
            return new PspRefundResult(Psp.RefundOutcome.Unknown);

        var amount = (long)Math.Round(refund.Amount, MidpointRounding.AwayFromZero);
        var refundId = Psp.RefundRequestId(refund.OrderRef, refund.Amount, DateTime.UtcNow);
        var transId = long.TryParse(refund.ProviderTxnId, out var t) ? t : 0;

        var signature = Psp.MoMoRefundSign(Cfg.AccessKey, Cfg.SecretKey, amount, refund.Reason,
            refundId, Cfg.PartnerCode, refundId, transId.ToString());

        var body = new Dictionary<string, object>
        {
            ["partnerCode"] = Cfg.PartnerCode,
            ["orderId"] = refundId,
            ["requestId"] = refundId,
            ["amount"] = amount,
            ["transId"] = transId,
            ["lang"] = "vi",
            ["description"] = refund.Reason,
            ["signature"] = signature
        };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var client = http.CreateClient("psp");
                var res = await client.PostAsJsonAsync($"{Cfg.Endpoint}/refund", body, ct);
                var json = await GatewayReply.JsonAsync(res, "MoMo", ct);
                var code = Number(json, "resultCode");

                log.LogInformation("MoMo hoàn {Amount} cho {Ref}: mã {Code} {Message}.",
                    refund.Amount, refund.OrderRef, code, Text(json, "message"));

                // 0 done. 1002/1004/1080 are refusals that will not improve.
                if (code == 0)
                    return new PspRefundResult(Psp.RefundOutcome.Accepted, Text(json, "transId"), code.ToString());
                if (code is 1002 or 1004 or 1080 or 1081)
                    return new PspRefundResult(Psp.RefundOutcome.Refused, Code: code.ToString());
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Không gọi được MoMo để hoàn tiền {Ref} (lần {Attempt}).",
                    refund.OrderRef, attempt);
            }

            if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
        }

        log.LogError("Không biết MoMo đã hoàn {Amount} cho {Ref} hay chưa. Mã yêu cầu {RefundId}.",
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
