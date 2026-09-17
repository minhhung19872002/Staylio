using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/01 MR-01 → MR-04 — activities a local runs, sold by the seat. The money
/// engine is the same one stays use; only the thing being counted changes.
/// </summary>
[ApiController]
[Route("api/experiences")]
public class ExperiencesController(
    StayHostDbContext db, AuthService auth, ExperienceService experiences, AdminAudit audit,
    Services.Gateways.PspRouter psp, Services.Gateways.PspCheckout pspCheckout) : ControllerBase
{
    /// <summary>
    /// docs/09 §2.7 (MR-E-06) — takes the seats off the session for ten minutes
    /// while the guest pays, so nobody buys them from under them.
    /// </summary>
    [HttpPost("slots/{slotId:int}/hold")]
    public async Task<ActionResult<ExperienceHoldDto>> Hold(
        int slotId, [FromBody] HoldSeatsRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var (hold, error) = await experiences.HoldAsync(user, slotId, req.Seats, req.Private, ct);
        return hold is null
            ? BadRequest(new { message = error })
            : Ok(new ExperienceHoldDto(hold.Id, hold.SlotId, hold.Seats, hold.IsPrivate, hold.ExpiresAt));
    }

    /* ---------------------------------------------- MR-E-09, the day itself */

    /// <summary>docs/09 §2.9 — the host's register for one session.</summary>
    [HttpGet("slots/{slotId:int}/roster")]
    public async Task<ActionResult<SessionRosterDto>> Roster(int slotId, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var (roster, error) = await experiences.RosterAsync(user, slotId, ct);
        return roster is null ? BadRequest(new { message = error }) : Ok(roster);
    }

    /// <summary>docs/09 §2.9 — mark one guest present or absent.</summary>
    [HttpPost("bookings/{id:int}/attendance")]
    public async Task<IActionResult> MarkAttendance(
        int id, [FromBody] MarkAttendanceRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var error = await experiences.MarkAttendanceAsync(user, id, req.Attended, ct);
        return error is null ? Ok(new { ok = true }) : BadRequest(new { message = error });
    }

    /* -------------------------------------------------- MR-E-11, the review */

    [HttpPost("bookings/{id:int}/review")]
    public async Task<IActionResult> WriteReview(
        int id, [FromBody] SubmitExperienceReviewRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var error = await experiences.WriteReviewAsync(user, id, req, ct);
        return error is null ? Ok(new { ok = true }) : BadRequest(new { message = error });
    }

    [HttpGet("{id:int}/reviews")]
    public async Task<ActionResult<IReadOnlyList<ExperienceReviewDto>>> Reviews(int id, CancellationToken ct) =>
        Ok(await experiences.ReviewsAsync(id, ct));

    /* ------------------------------------------------- MR-E-03, moderation */

    /// <summary>docs/09 §2.2 — what is waiting for a reviewer, oldest first.</summary>
    [HttpGet("review-queue")]
    public async Task<ActionResult<IReadOnlyList<PendingExperienceDto>>> ReviewQueue(CancellationToken ct)
    {
        if (await audit.RequireAsync(AdminScope.Moderation, ct) is null)
            return this.Denied("Chỉ kiểm duyệt viên mới xem được hàng chờ này.");

        return Ok(await experiences.ReviewQueueAsync(ct));
    }

    /// <summary>docs/09 §2.2 — approve, ask for changes, or refuse with a reason.</summary>
    [HttpPost("{id:int}/review")]
    public async Task<IActionResult> Review(
        int id, [FromBody] ReviewExperienceRequest req, CancellationToken ct)
    {
        var admin = await audit.RequireAsync(AdminScope.Moderation, ct);
        if (admin is null) return this.Denied("Chỉ kiểm duyệt viên mới xét được trải nghiệm.");

        var error = await experiences.ReviewAsync(admin, id, req.Decision ?? "", req.Note, ct);
        if (error is not null) return BadRequest(new { message = error });

        audit.Record(admin, $"experience-{(req.Decision ?? "").Trim().ToLowerInvariant()}",
            $"experience:{id}", null, null, req.Note);
        await db.SaveChangesAsync(ct);

        return Ok(new { ok = true });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExperienceCardDto>>> Browse(
        [FromQuery] string? q, [FromQuery] string? city, [FromQuery] DateOnly? on,
        CancellationToken ct = default)
    {
        var query = db.Experiences.Where(x => x.IsPublished);

        foreach (var term in SearchText.Terms(q))
        {
            var t = term;
            query = query.Where(x => EF.Functions.Like(x.SearchText, $"%{t}%"));
        }

        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(x => x.City == city);

        // "On this day" means a session that day with at least one seat going.
        if (on is { } day)
        {
            var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var to = from.AddDays(1);
            query = query.Where(x => x.Slots.Any(s =>
                s.Status == SlotStatus.Open && s.StartsAt >= from && s.StartsAt < to && s.SeatsTaken < s.Capacity));
        }

        var now = DateTime.UtcNow;

        return Ok(await query
            .OrderByDescending(x => x.Rating).ThenBy(x => x.Id)
            .Select(x => new ExperienceCardDto(
                x.Id, x.Slug, x.Title, x.City, x.Summary,
                x.DurationMinutes, x.MaxGroup, x.PricePerPerson,
                x.Rating, x.ReviewCount,
                x.Host!.Name,
                x.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).ToList(),
                x.Slots.Count(s => s.Status == SlotStatus.Open && s.StartsAt > now && s.SeatsTaken < s.Capacity)))
            .ToListAsync(ct));
    }

    [HttpGet("{idOrSlug}")]
    public async Task<ActionResult<ExperienceDetailDto>> Detail(string idOrSlug, CancellationToken ct)
    {
        var detail = await experiences.DetailAsync(idOrSlug, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>What a seat costs, before anyone commits to anything.</summary>
    [HttpGet("slots/{slotId:int}/quote")]
    public async Task<ActionResult<ExperienceQuoteDto>> Quote(
        int slotId, [FromQuery] int seats = 1, [FromQuery] bool priv = false, CancellationToken ct = default)
    {
        var quote = await experiences.QuoteAsync(slotId, seats, priv, ct);
        return quote is null ? NotFound() : Ok(quote);
    }

    [HttpPost("slots/{slotId:int}/book")]
    public async Task<ActionResult<ExperienceBookingDto>> Book(
        int slotId, [FromBody] BookExperienceRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        // docs/08 §5.2 — "không được đặt đơn mới" covers every kind of booking,
        // or the restriction is a door with the window left open.
        if (Restrictions.Has(user.RestrictionMask, RestrictionKind.NoNewBookings))
            return StatusCode(403, new { message = Restrictions.Message(RestrictionKind.NoNewBookings) });

        var (booking, error) = await experiences.BookAsync(user, slotId, req, ct);
        if (booking is null) return BadRequest(new { message = error });

        var dto = await experiences.BookingDtoAsync(booking.Id, ct);

        // docs/07 §13 — the ticket waits while the guest pays on the gateway's page.
        var method = ProductCheckout.Normalise(req.PaymentMethod);
        if (booking.Status == ExperienceBookingStatus.AwaitingPayment && psp.IsLive(method))
        {
            var started = await pspCheckout.StartForAsync(
                new PaymentSession
                {
                    ExperienceBookingId = booking.Id, Method = method, Amount = booking.Total,
                    AttemptKey = $"xp-{booking.Id}"
                },
                booking.Id, $"Staylio {booking.Reference}", user.Id,
                Psp.ClientIp(HttpContext.Connection.RemoteIpAddress?.ToString()), ct);

            if (!started.Ok || started.PayUrl is null)
            {
                await experiences.CancelAsync(user.Id, booking.Id, ct);
                return BadRequest(new { message = started.Error, retryable = true });
            }

            return Ok(dto! with { GatewayRedirectUrl = started.PayUrl, GatewayOrderRef = started.OrderRef });
        }

        return Ok(dto);
    }

    [HttpGet("bookings")]
    public async Task<ActionResult<IReadOnlyList<ExperienceBookingDto>>> MyBookings(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        return Ok(await experiences.MyBookingsAsync(user.Id, ct));
    }

    [HttpPost("bookings/{id:int}/cancel")]
    public async Task<ActionResult<ExperienceBookingDto>> Cancel(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var error = await experiences.CancelAsync(user.Id, id, ct);
        if (error is not null) return BadRequest(new { message = error });

        return Ok(await experiences.BookingDtoAsync(id, ct));
    }

    /* ---------------------------------------------------------- the host */

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<ExperienceDetailDto>>> Mine(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        return Ok(await experiences.MineAsync(user.Id, ct));
    }

    [HttpPost]
    public async Task<ActionResult<ExperienceDetailDto>> Save(
        [FromBody] SaveExperienceRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        // docs/08 §5.2 — an experience is a listing by another name.
        if (Restrictions.Has(user.RestrictionMask, RestrictionKind.NoNewListings))
            return StatusCode(403, new { message = Restrictions.Message(RestrictionKind.NoNewListings) });

        var (id, error) = await experiences.SaveAsync(user, req, ct);
        if (id is null) return BadRequest(new { message = error });

        return Ok(await experiences.DetailAsync(id.Value.ToString(), ct));
    }

    /// <summary>docs/01 MR-02 — the sessions on offer, one row per start time.</summary>
    [HttpPost("{id:int}/slots")]
    public async Task<ActionResult<ExperienceDetailDto>> AddSlots(
        int id, [FromBody] AddSlotsRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var error = await experiences.AddSlotsAsync(user, id, req, ct);
        if (error is not null) return this.Denied(error);

        return Ok(await experiences.DetailAsync(id.ToString(), ct));
    }

    [HttpDelete("slots/{slotId:int}")]
    public async Task<IActionResult> CancelSlot(
        int slotId, [FromQuery] string? reason, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var error = await experiences.CancelSlotAsync(
            user, slotId, reason ?? "Chủ trải nghiệm đã huỷ suất này.", ct);

        return error is null ? NoContent() : this.Denied(error);
    }
}
