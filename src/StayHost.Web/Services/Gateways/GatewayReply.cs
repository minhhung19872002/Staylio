using System.Text.Json;

namespace StayHost.Web.Services.Gateways;

/// <summary>
/// A gateway answered, but not with an answer about the money: a 404 because the
/// endpoint moved, a WAF page, a 502 from something in front of it.
/// </summary>
public class GatewayCallException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Reading a payment gateway's reply.
///
/// Every provider here wraps its call in <c>catch (Exception)</c> and degrades to
/// "don't know" — which is the right answer for a gateway that did not respond.
/// The hole was that the check only ever fired on a <em>parse</em> failure, so an
/// HTTP error whose body happens to be valid JSON (<c>{"message":"Not Found"}</c>
/// is a common one) sailed through, left every field empty, and fell out of the
/// bottom of <c>QueryAsync</c> as <see cref="PaymentSessionStatus.Failed"/>.
///
/// That is the difference between "ask again next tick" and "this payment
/// failed", decided by whether an error page was JSON-shaped. docs/07 §5 and the
/// two entries in CLAUDE.md §4 say the same thing: not knowing is not a refusal.
///
/// The second thing it fixes is the message. <c>ReadFromJsonAsync</c> on an error
/// page says "'&lt;' is an invalid start of a value", which reads like a bug in
/// the parser and hides the status code entirely — the exact shape that made the
/// VNPay 403-without-a-User-Agent take a day to find.
/// </summary>
internal static class GatewayReply
{
    /// <summary>The gateway's JSON body, or a throw that names what came instead.</summary>
    public static async Task<JsonElement> JsonAsync(
        HttpResponseMessage res, string gateway, CancellationToken ct)
    {
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new GatewayCallException(
                $"{gateway} trả HTTP {(int)res.StatusCode} ({res.ReasonPhrase}): {Snippet(body)}");

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (JsonException ex)
        {
            throw new GatewayCallException(
                $"{gateway} trả HTTP {(int)res.StatusCode} nhưng thân không phải JSON: {Snippet(body)}", ex);
        }
    }

    /// <summary>Enough of the body to recognise it, on one line.</summary>
    public static string Snippet(string body)
    {
        var flat = string.Join(' ', (body ?? "").Split(
            new[] { '\n', '\r', '\t' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (flat.Length == 0) return "(thân rỗng)";
        return flat.Length <= 300 ? flat : flat[..300] + "…";
    }
}
