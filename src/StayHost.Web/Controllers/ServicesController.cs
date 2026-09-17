using Microsoft.AspNetCore.Mvc;
using StayHost.Domain;
using StayHost.Web.Contracts;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/01 MR-05 → MR-07 — services booked by the time slot, at an address the
/// guest gives, from a host or from a partner the platform takes a cut of.
/// </summary>
[ApiController]
[Route("api/services")]
public class ServicesController(
    AuthService auth, ServiceMarketService market,
    Services.Gateways.PspRouter psp, Services.Gateways.PspCheckout pspCheckout) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServiceCardDto>>> Browse(
        [FromQuery] string? q, [FromQuery] string? category, [FromQuery] string? city,
        CancellationToken ct = default) =>
        Ok(await market.BrowseAsync(q, category, city, ct));

    /* ------------------------------------------------ MR-S-01, the provider */

    /// <summary>docs/09 §3.2 — the services this provider lists.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<ServiceDetailDto>>> Mine(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        return Ok(await market.MineAsync(user.Id, ct));
    }

    /// <summary>docs/09 §3.2 — create or edit one, certificate and all.</summary>
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveServiceRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var (id, error) = await market.SaveOfferingAsync(user, req, ct);
        return id is null ? BadRequest(new { message = error }) : Ok(new { id });
    }

    [HttpGet("bookings")]
    public async Task<ActionResult<IReadOnlyList<ServiceBookingDto>>> MyBookings(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        return Ok(await market.MyBookingsAsync(user.Id, ct));
    }

    [HttpPost("bookings/{id:int}/cancel")]
    public async Task<ActionResult<ServiceBookingDto>> Cancel(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var error = await market.CancelAsync(user.Id, id, ct);
        if (error is not null) return BadRequest(new { message = error });

        return Ok(await market.BookingDtoAsync(id, ct));
    }

    /// <summary>
    /// docs/09 §3.5 — the jobs this provider has been booked for, newest first.
    /// Above the catch-all detail route so "jobs" is not read as a slug.
    /// </summary>
    [HttpGet("jobs")]
    public async Task<ActionResult<IReadOnlyList<ProviderJobDto>>> Jobs(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        return Ok(await market.JobsAsync(user.Id, ct));
    }

    /// <summary>
    /// docs/09 §3.6 (DV-D) — the provider reports that the site was not what the
    /// guest declared. Half the order goes back to the guest and half stays to
    /// pay for the wasted trip, so the person who travelled is not left with
    /// nothing.
    /// </summary>
    /// <summary>docs/09 §3.5 — the provider accepts or declines a job that waits on them.</summary>
    [HttpPost("jobs/{id:int}/accept")]
    public Task<IActionResult> Accept(int id, CancellationToken ct) => DecideAsync(id, true, null, ct);

    [HttpPost("jobs/{id:int}/decline")]
    public Task<IActionResult> Decline(int id, [FromBody] ProviderJobDecisionRequest? req, CancellationToken ct) =>
        DecideAsync(id, false, req?.Reason, ct);

    private async Task<IActionResult> DecideAsync(int id, bool accept, string? reason, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var error = await market.RespondAsync(user.Id, id, accept, reason, ct);
        return error is null ? NoContent() : BadRequest(new { message = error });
    }

    /// <summary>docs/09 §3.6 — the provider pulls out: full refund, balance for the guest, a fine.</summary>
    [HttpPost("jobs/{id:int}/cancel")]
    public async Task<IActionResult> ProviderCancel(
        int id, [FromBody] ProviderJobDecisionRequest? req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var error = await market.ProviderCancelAsync(user.Id, id, req?.Reason, ct);
        return error is null ? NoContent() : BadRequest(new { message = error });
    }

    [HttpPost("bookings/{id:int}/misdeclared")]
    public async Task<ActionResult<ServiceBookingDto>> Misdeclared(
        int id, [FromBody] MisdeclaredConditionsRequest? req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var error = await market.ReportMisdeclaredAsync(user.Id, id, req?.Note, ct);
        if (error is not null) return BadRequest(new { message = error });

        return Ok(await market.BookingDtoAsync(id, ct));
    }

    /// <summary>
    /// docs/09 §5 — the four service headings, written once the job is over.
    /// Placed above the catch-all detail route only for readability; the two
    /// segments make it the more specific match either way.
    /// </summary>
    [HttpPost("bookings/{id:int}/review")]
    public async Task<IActionResult> Review(
        int id, [FromBody] SubmitServiceReviewRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var error = await market.WriteReviewAsync(user, id, req, ct);
        return error is null ? Ok(new { ok = true }) : BadRequest(new { message = error });
    }

    [HttpGet("{id:int}/reviews")]
    public async Task<ActionResult<IReadOnlyList<ServiceReviewDto>>> Reviews(int id, CancellationToken ct) =>
        Ok(await market.ReviewsAsync(id, ct));

    [HttpGet("{idOrSlug}")]
    public async Task<ActionResult<ServiceDetailDto>> Detail(string idOrSlug, CancellationToken ct)
    {
        var detail = await market.DetailAsync(idOrSlug, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>
    /// What a job would cost and whether it can be taken at all — the address
    /// matters here, because a provider only travels so far (docs/01 MR-05).
    /// </summary>
    [HttpPost("{id:int}/quote")]
    public async Task<ActionResult<ServiceQuoteDto>> Quote(
        int id, [FromBody] QuoteServiceRequest req, CancellationToken ct)
    {
        var quote = await market.QuoteAsync(id, req, ct);
        return quote is null ? NotFound() : Ok(quote);
    }

    [HttpPost("{id:int}/book")]
    public async Task<ActionResult<ServiceBookingDto>> Book(
        int id, [FromBody] BookServiceRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        // docs/08 §5.2 — "không được đặt đơn mới" covers every kind of booking.
        if (Restrictions.Has(user.RestrictionMask, RestrictionKind.NoNewBookings))
            return StatusCode(403, new { message = Restrictions.Message(RestrictionKind.NoNewBookings) });

        var (booking, error) = await market.BookAsync(user, id, req, ct);
        if (booking is null) return BadRequest(new { message = error });

        var dto = await market.BookingDtoAsync(booking.Id, ct);

        // docs/07 §13 — the job waits while the guest pays on the gateway's page.
        var method = ProductCheckout.Normalise(req.PaymentMethod);
        if (booking.Status == ServiceBookingStatus.AwaitingPayment && psp.IsLive(method))
        {
            var started = await pspCheckout.StartForAsync(
                new PaymentSession
                {
                    ServiceBookingId = booking.Id, Method = method, Amount = booking.Total,
                    AttemptKey = $"svc-{booking.Id}"
                },
                booking.Id, $"Staylio {booking.Reference}", user.Id,
                Psp.ClientIp(HttpContext.Connection.RemoteIpAddress?.ToString()), ct);

            if (!started.Ok || started.PayUrl is null)
            {
                await market.CancelAsync(user.Id, booking.Id, ct);
                return BadRequest(new { message = started.Error, retryable = true });
            }

            return Ok(dto! with { GatewayRedirectUrl = started.PayUrl, GatewayOrderRef = started.OrderRef });
        }

        return Ok(dto);
    }
}
