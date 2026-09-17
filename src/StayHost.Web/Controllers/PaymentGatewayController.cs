using Microsoft.AspNetCore.Mvc;
using StayHost.Domain;
using StayHost.Web.Services.Gateways;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/07 §13 — where a licensed gateway talks back.
///
/// Two kinds of caller land here and they are not the same thing. The *return*
/// is a browser: it proves nothing on its own, it is only how the guest gets
/// back to a page. The *IPN* is the gateway's own server and is signed. docs/07
/// §5 is emphatic that the first must never be believed, so every route below
/// checks a signature or asks the gateway before it settles anything — and the
/// return routes settle only as a convenience, because <see cref="PspSweeper"/>
/// would have done it a minute later anyway.
///
/// None of these routes needs a session. The guest coming back may have lost
/// their cookie on a redirect chain through a bank, and the gateway's server has
/// never had one.
/// </summary>
[ApiController]
[Route("api/payments")]
public class PaymentGatewayController(
    PspCheckout checkout, PspRouter router, ILogger<PaymentGatewayController> log)
    : ControllerBase
{
    /* ------------------------------------------------------------------ VNPay */

    [HttpGet("vnpay/return")]
    public async Task<IActionResult> VnPayReturn(CancellationToken ct)
    {
        var query = Query();
        var session = await checkout.FindAsync(query.GetValueOrDefault("vnp_TxnRef"), ct);
        if (session is null) return Redirect(Outcome(null, "unknown"));

        var provider = router.ByKey(session.Provider);
        var status = provider is null
            ? session.Status
            : await checkout.SettleAsync(session, provider.Read(query), "return", ct);

        return Redirect(Outcome(session, Word(status)));
    }

    /// <summary>
    /// VNPay's reply, spelled the way VNPay reads it.
    ///
    /// It cannot be an anonymous object: this application serialises with the
    /// camelCase policy, so <c>RspCode</c> goes out as <c>rspCode</c> and VNPay,
    /// which looks for the exact name, sees no answer at all. Their documented
    /// behaviour then is to retry the IPN ten times at five-minute intervals —
    /// for every successful payment, quietly, with the booking already confirmed
    /// and nothing on this side looking wrong.
    /// </summary>
    public sealed record VnPayReply(
        [property: System.Text.Json.Serialization.JsonPropertyName("RspCode")] string RspCode,
        [property: System.Text.Json.Serialization.JsonPropertyName("Message")] string Message);

    /// <summary>
    /// VNPay's server-to-server confirmation. It expects a small JSON body with
    /// its own reason codes, and it will keep retrying until it gets one — so
    /// every branch answers, including the ones that refuse.
    ///
    /// Their table: 00 and 02 mean "recorded, stop"; 01, 04, 97 and 99 mean
    /// "try again". So a signature this side cannot verify asks for a retry
    /// rather than closing the matter, which is the right way round — the
    /// alternative is telling VNPay to stop asking about a payment nobody
    /// understood.
    /// </summary>
    [HttpGet("vnpay/ipn")]
    public async Task<IActionResult> VnPayIpn(CancellationToken ct)
    {
        var query = Query();
        var session = await checkout.FindAsync(query.GetValueOrDefault("vnp_TxnRef"), ct);

        if (session is null) return Ok(new VnPayReply("01", "Order not found"));

        var provider = router.ByKey(session.Provider);
        if (provider is null) return Ok(new VnPayReply("99", "Unknown error"));

        var verdict = provider.Read(query);
        if (verdict.Code == PspVerdict.Signature) return Ok(new VnPayReply("97", "Invalid signature"));

        if (verdict.Amount > 0 && !Psp.AmountMatches(session.Amount, verdict.Amount))
            return Ok(new VnPayReply("04", "Invalid amount"));

        if (session.Status != PaymentSessionStatus.Pending)
            return Ok(new VnPayReply("02", "Order already confirmed"));

        await checkout.SettleAsync(session, verdict, "ipn", ct);
        return Ok(new VnPayReply("00", "Confirm Success"));
    }

    /// <summary>
    /// docs/07 §4 — where a guest comes back from VNPay's token pages, whether
    /// they were saving a card or paying with one they saved.
    ///
    /// A separate route from the ordinary return only because VNPay's token API
    /// spells every parameter differently; everything after that is the same
    /// path, the same signature check and the same settlement.
    /// </summary>
    [HttpGet("vnpay/token-return")]
    public async Task<IActionResult> VnPayTokenReturn(CancellationToken ct)
    {
        var query = Query();
        var session = await checkout.FindAsync(query.GetValueOrDefault("vnp_txn_ref"), ct);
        if (session is null) return Redirect(Outcome(null, "unknown"));

        var provider = router.ByKey(session.Provider);
        var status = provider is null
            ? session.Status
            : await checkout.SettleAsync(session, provider.Read(query), "return", ct);

        return Redirect(Outcome(session, Word(status)));
    }

    /* ----------------------------------------------------------------- OnePay */

    /// <summary>
    /// docs/07 §13 — where OnePay sends the guest back.
    ///
    /// OnePay has no separate server-to-server IPN in this integration: the
    /// signed return is the only thing they push, and it arrives on the guest's
    /// browser. That makes docs/07 §5's self-check load-bearing rather than a
    /// safety net — a guest who closes the tab on their page is settled by
    /// <see cref="PspSweeper"/> asking, and by nothing else. Which is also why
    /// the signature is checked here exactly as it would be on an IPN: this is
    /// the one message OnePay signs.
    /// </summary>
    [HttpGet("onepay/return")]
    public async Task<IActionResult> OnePayReturn(CancellationToken ct)
    {
        var query = Query();
        var session = await checkout.FindAsync(query.GetValueOrDefault("vpc_MerchTxnRef"), ct);
        if (session is null) return Redirect(Outcome(null, "unknown"));

        var provider = router.ByKey(session.Provider);
        var status = provider is null
            ? session.Status
            : await checkout.SettleAsync(session, provider.Read(query), "return", ct);

        return Redirect(Outcome(session, Word(status)));
    }

    /* ------------------------------------------------------------------- MoMo */

    [HttpGet("momo/return")]
    public async Task<IActionResult> MoMoReturn(CancellationToken ct)
    {
        var query = Query();
        var session = await checkout.FindAsync(query.GetValueOrDefault("orderId"), ct);
        if (session is null) return Redirect(Outcome(null, "unknown"));

        var provider = router.ByKey(session.Provider);
        var status = provider is null
            ? session.Status
            : await checkout.SettleAsync(session, provider.Read(query), "return", ct);

        return Redirect(Outcome(session, Word(status)));
    }

    /// <summary>
    /// MoMo posts JSON and wants a 204 back. Anything else and it retries, which
    /// is fine — settling twice is a no-op — but noisy.
    /// </summary>
    [HttpPost("momo/ipn")]
    public async Task<IActionResult> MoMoIpn([FromBody] Dictionary<string, object?> body, CancellationToken ct)
    {
        var payload = body.ToDictionary(p => p.Key, p => p.Value?.ToString() ?? "");
        var session = await checkout.FindAsync(payload.GetValueOrDefault("orderId"), ct);

        if (session is null)
        {
            log.LogWarning("MoMo IPN cho đơn lạ {Ref}.", payload.GetValueOrDefault("orderId"));
            return NoContent();
        }

        var provider = router.ByKey(session.Provider);
        if (provider is not null) await checkout.SettleAsync(session, provider.Read(payload), "ipn", ct);

        return NoContent();
    }

    /* ---------------------------------------------------------------- ZaloPay */

    /// <summary>
    /// ZaloPay's redirect carries a checksum of its own, but the app can also
    /// finish the payment on a phone while this tab sits open — so rather than
    /// read the redirect, this asks ZaloPay directly. Slower by one call and
    /// right in every case.
    /// </summary>
    [HttpGet("zalopay/return")]
    public async Task<IActionResult> ZaloReturn([FromQuery(Name = "ref")] string? reference, CancellationToken ct)
    {
        var session = await checkout.FindAsync(
            reference ?? Query().GetValueOrDefault("apptransid"), ct);

        if (session is null) return Redirect(Outcome(null, "unknown"));

        var provider = router.ByKey(session.Provider);
        if (provider is null) return Redirect(Outcome(session, Word(session.Status)));

        var verdict = await provider.QueryAsync(session.OrderRef, session.CreatedAt, ct);
        var status = await checkout.SettleAsync(session, verdict, "return", ct);

        return Redirect(Outcome(session, Word(status)));
    }

    [HttpPost("zalopay/callback")]
    public async Task<IActionResult> ZaloCallback([FromBody] ZaloCallbackBody body, CancellationToken ct)
    {
        // The order reference is inside the signed payload, so it is read only
        // after the signature is checked — which the provider does. Finding the
        // session first needs the id, so it is pulled out unverified here and the
        // verdict is what decides anything.
        var appTransId = TransIdOf(body.Data);
        var session = await checkout.FindAsync(appTransId, ct);

        if (session is null) return Ok(new { return_code = 0, return_message = "order not found" });

        var provider = router.ByKey(session.Provider);
        if (provider is null) return Ok(new { return_code = 0, return_message = "provider off" });

        var verdict = provider.Read(new Dictionary<string, string>
        {
            ["data"] = body.Data ?? "",
            ["mac"] = body.Mac ?? ""
        });

        if (verdict.Code == PspVerdict.Signature)
            return Ok(new { return_code = -1, return_message = "mac not equal" });

        await checkout.SettleAsync(session, verdict, "ipn", ct);
        return Ok(new { return_code = 1, return_message = "success" });
    }

    public record ZaloCallbackBody(string? Data, string? Mac);

    /* ------------------------------------------------------------------ shared */

    private Dictionary<string, string> Query() =>
        Request.Query.ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal);

    private static string Word(PaymentSessionStatus status) => status switch
    {
        PaymentSessionStatus.Paid => "ok",
        PaymentSessionStatus.Cancelled => "cancelled",
        PaymentSessionStatus.Pending => "pending",
        _ => "failed"
    };

    /// <summary>
    /// Where the guest lands. The page reads the booking itself rather than
    /// trusting these two words — they only decide which sentence it opens with.
    /// </summary>
    private static string Outcome(PaymentSession? session, string word)
    {
        if (session is null) return $"/thanh-toan/ket-qua?ket-qua={word}";

        // A share's payer has no account and no trip page: their link is the
        // only page they know, and it already shows the share's state.
        if (session.BillShare is { } share)
            return $"/split/{share.Token}?ket-qua={word}";

        var head = $"/thanh-toan/ket-qua?ket-qua={word}&ma={Uri.EscapeDataString(session.OrderRef)}";

        if (session.ExperienceBookingId is { } xp) return $"{head}&loai=xp&ve={xp}";
        if (session.ServiceBookingId is { } svc) return $"{head}&loai=svc&ve={svc}";
        if (session.IsBalance) return $"{head}&loai=balance&don={session.BookingId}";

        return $"{head}&don={session.BookingId}";
    }

    /// <summary>
    /// <c>app_trans_id</c> out of the raw callback string, without parsing the
    /// JSON into anything that could be re-serialised — the signature is over the
    /// exact bytes and nothing here may disturb them.
    /// </summary>
    private static string? TransIdOf(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(data);
            return doc.RootElement.TryGetProperty("app_trans_id", out var v) ? v.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
