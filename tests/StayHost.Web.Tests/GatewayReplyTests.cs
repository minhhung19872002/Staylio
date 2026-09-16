using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StayHost.Domain;
using StayHost.Web.Services.Gateways;

namespace StayHost.Web.Tests;

/// <summary>
/// docs/07 §5 — what a gateway that did not answer about the money counts as.
///
/// Every provider already degraded to "don't know" when the reply would not
/// parse. The hole was that an HTTP error whose body happens to be valid JSON
/// ({"message":"Not Found"} is a common one) parsed cleanly, left every field
/// empty, and fell out of the bottom of QueryAsync as Failed. Measured before
/// the fix: a 404 with a JSON body and a 502 from a proxy both read as Failed,
/// while the two HTML cases escaped only because the parser threw.
///
/// Failed is not a neutral word here. PspSweeper is one of the three ways a
/// payment is settled, so it decides between "ask again next tick" and "this
/// payment failed" — for a guest who may well have paid.
/// </summary>
public class GatewayReplyTests
{
    private sealed class Stub(HttpStatusCode code, string body, string type) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, type)
            });
    }

    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://stub.local") };
    }

    private static ZaloPayProvider Provider(HttpStatusCode code, string body, string type)
    {
        var settings = new PspSettings();
        settings.Zalopay.AppId = "2553";
        settings.Zalopay.Key1 = "key-one";
        settings.Zalopay.Key2 = "key-two";
        settings.Zalopay.Endpoint = "http://stub.local/v2";

        return new ZaloPayProvider(Options.Create(settings),
            new OneClient(new Stub(code, body, type)),
            NullLogger<ZaloPayProvider>.Instance);
    }

    [Theory]
    // The one that used to slip through: an error page that happens to be JSON.
    [InlineData(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}", "application/json")]
    [InlineData(HttpStatusCode.BadGateway, "{\"error\":\"upstream\"}", "application/json")]
    // These two always worked, because the parser threw before anything read them.
    [InlineData(HttpStatusCode.NotFound, "<html><title>404</title></html>", "text/html")]
    [InlineData(HttpStatusCode.OK, "<html>maintenance</html>", "text/html")]
    public async Task A_reply_that_is_not_about_the_money_is_not_a_refusal(
        HttpStatusCode code, string body, string type)
    {
        var verdict = await Provider(code, body, type)
            .QueryAsync("SHTEST01", DateTime.UtcNow, default);

        Assert.Equal(PspVerdict.Unknown.Status, verdict.Status);
    }

    /// <summary>
    /// The control. A fix that answered "don't know" to everything would hide
    /// the one thing QueryAsync exists to find out.
    /// </summary>
    [Fact]
    public async Task A_gateway_that_says_the_payment_failed_is_still_believed()
    {
        var verdict = await Provider(HttpStatusCode.OK,
                "{\"return_code\":2,\"return_message\":\"giao dich that bai\"}", "application/json")
            .QueryAsync("SHTEST01", DateTime.UtcNow, default);

        Assert.Equal(PaymentSessionStatus.Failed, verdict.Status);
    }

    /// <summary>
    /// The second half of the same fix. ReadFromJsonAsync on an error page says
    /// "'&lt;' is an invalid start of a value", which names neither the status
    /// nor the gateway — the shape that made the VNPay 403-without-a-User-Agent
    /// take a day to find. The message has to carry both.
    /// </summary>
    [Fact]
    public async Task The_message_names_the_status_and_what_came_back()
    {
        var res = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("<html>\n<head><title>404 Not Found</title></head>\n</html>")
        };

        var ex = await Assert.ThrowsAsync<GatewayCallException>(
            () => GatewayReply.JsonAsync(res, "ZaloPay", default));

        Assert.Contains("404", ex.Message);
        Assert.Contains("ZaloPay", ex.Message);
        Assert.Contains("404 Not Found", ex.Message);
        Assert.DoesNotContain("\n", ex.Message);   // one line, so a log stays readable
    }

    [Fact]
    public void An_empty_body_says_so_rather_than_printing_nothing() =>
        Assert.Equal("(thân rỗng)", GatewayReply.Snippet("   \n  "));
}
