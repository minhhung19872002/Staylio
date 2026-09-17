using Microsoft.AspNetCore.Mvc;
using StayHost.Domain;
using StayHost.Web.Contracts;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

[ApiController]
[Route("api")]
public class ListingsController(
    CatalogService catalog, BookingService bookings, AuthService auth, CouponService coupons) : ControllerBase
{
    /// <summary>
    /// Nightly rates and availability for the date picker (docs/01 TM-05), plus
    /// the next free windows when the guest's dates are gone (TĐ-09).
    /// </summary>
    [HttpGet("listings/{id:int}/calendar")]
    public async Task<ActionResult<ListingCalendarDto>> Calendar(
        int id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var calendar = await bookings.CalendarAsync(id, start, to ?? start.AddMonths(4), ct);
        return calendar is null ? NotFound() : Ok(calendar);
    }

    [HttpGet("meta")]
    public async Task<ActionResult<MetaDto>> Meta(CancellationToken ct) =>
        Ok(await catalog.GetMetaAsync(ct));

    /// <summary>
    /// Dates are optional but, when present, every card comes back priced for
    /// them — that is what keeps the card, the room page and checkout in step
    /// (docs/00 §6.8).
    /// </summary>
    [HttpGet("home")]
    public async Task<ActionResult<HomeDto>> Home(
        [FromQuery] DateOnly? checkIn,
        [FromQuery] DateOnly? checkOut,
        [FromQuery] int guests = 1,
        [FromQuery] int infants = 0,
        [FromQuery] int pets = 0,
        CancellationToken ct = default) =>
        Ok(await catalog.GetHomeAsync(
            HttpContext.SessionId(), checkIn, checkOut, guests, ct, infants, pets));

    [HttpGet("suggest")]
    public async Task<ActionResult<IReadOnlyList<CatalogService.SuggestionDto>>> Suggest(
        [FromQuery] string? q, CancellationToken ct) =>
        Ok(await catalog.SuggestAsync(q, ct));

    [HttpGet("listings")]
    public async Task<ActionResult<SearchResultDto>> Search(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] int guests = 0,
        [FromQuery] string? amenities = null,
        [FromQuery] string? sort = "reco",
        [FromQuery] string? roomType = "any",
        [FromQuery] int? bedrooms = null,
        [FromQuery] int? beds = null,
        [FromQuery] int? bathrooms = null,
        [FromQuery] bool superhost = false,
        [FromQuery] bool guestFavorite = false,
        [FromQuery] bool instantBook = false,
        [FromQuery] bool freeCancellation = false,
        [FromQuery] DateOnly? checkIn = null,
        [FromQuery] DateOnly? checkOut = null,
        [FromQuery] string? stay = null,
        [FromQuery] int flex = 0,
        [FromQuery] int months = 0,
        [FromQuery] string? startMonths = null,
        [FromQuery] double? south = null,
        [FromQuery] double? west = null,
        [FromQuery] double? north = null,
        [FromQuery] double? east = null,
        [FromQuery] string? hostLanguages = null,
        [FromQuery] string? polygon = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24,
        // docs/00 §6.8 — a card has to be priced by the engine checkout uses, and
        // that engine charges for a pet. `guests` alone left every card quoting a
        // stay the guest could not book at that price.
        [FromQuery] int infants = 0,
        [FromQuery] int pets = 0,
        [FromQuery] double? minRating = null,
        [FromQuery] bool payAtProperty = false,
        [FromQuery] double? maxCentreKm = null,
        CancellationToken ct = default)
    {
        var query = BuildQuery(
            q, category, minPrice, maxPrice, guests, amenities, sort, roomType,
            bedrooms, beds, bathrooms, superhost, guestFavorite, instantBook, freeCancellation,
            checkIn, checkOut, south, west, north, east, page, pageSize,
            Flexible(stay, flex, months, startMonths, checkIn, checkOut), hostLanguages, polygon,
            infants, pets)
            with { MinRating = minRating, PayAtPropertyOnly = payAtProperty, MaxCentreKm = maxCentreKm };

        return Ok(await catalog.SearchAsync(query, HttpContext.SessionId(), ct));
    }

    /// <summary>
    /// docs/01 TM-19 — the filter panel needs the number of matches as the guest
    /// changes a filter, without paying for a page of listings.
    /// </summary>
    [HttpGet("listings/count")]
    public async Task<ActionResult<object>> Count(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] int guests = 0,
        [FromQuery] string? amenities = null,
        [FromQuery] string? roomType = "any",
        [FromQuery] int? bedrooms = null,
        [FromQuery] int? beds = null,
        [FromQuery] int? bathrooms = null,
        [FromQuery] bool superhost = false,
        [FromQuery] bool guestFavorite = false,
        [FromQuery] bool instantBook = false,
        [FromQuery] bool freeCancellation = false,
        [FromQuery] string? hostLanguages = null,
        [FromQuery] double? minRating = null,
        [FromQuery] bool payAtProperty = false,
        [FromQuery] double? maxCentreKm = null,
        CancellationToken ct = default)
    {
        var query = BuildQuery(
            q, category, minPrice, maxPrice, guests, amenities, "reco", roomType,
            bedrooms, beds, bathrooms, superhost, guestFavorite, instantBook, freeCancellation,
            null, null, null, null, null, null, 1, 1, null, hostLanguages)
            with { MinRating = minRating, PayAtPropertyOnly = payAtProperty, MaxCentreKm = maxCentreKm };

        return Ok(new { total = await catalog.CountAsync(query, ct) });
    }

    private static CatalogService.SearchQuery BuildQuery(
        string? q, string? category, decimal? minPrice, decimal? maxPrice, int guests,
        string? amenities, string? sort, string? roomType,
        int? bedrooms, int? beds, int? bathrooms,
        bool superhost, bool guestFavorite, bool instantBook, bool freeCancellation,
        DateOnly? checkIn, DateOnly? checkOut,
        double? south, double? west, double? north, double? east,
        int page, int pageSize, FlexibleRequest? flex = null, string? hostLanguages = null, string? polygon = null,
        int infants = 0, int pets = 0)
    {
        var keys = (amenities ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hostLangs = (hostLanguages ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var area = GeoPolygon.Parse(polygon);

        var bounds = south is not null && west is not null && north is not null && east is not null
            ? new CatalogService.MapBounds(south.Value, west.Value, north.Value, east.Value)
            : (CatalogService.MapBounds?)null;

        return new CatalogService.SearchQuery(
            q, category, minPrice, maxPrice, guests, keys, sort, roomType,
            bedrooms, beds, bathrooms, superhost, guestFavorite, instantBook, freeCancellation,
            page, pageSize, checkIn, checkOut, bounds, flex,
            HostLanguages: hostLangs,
            Polygon: area.Count >= 3 ? area : null,
            Infants: infants,
            Pets: pets);
    }

    /// <summary>
    /// docs/01 TM-06 and TM-07. `stay` names the length (weekend / week / month
    /// / months), `flex` is the ± 1–7 days, and for whole-month stays
    /// `startMonths` lists the months it may begin in as yyyy-MM.
    /// </summary>
    private static FlexibleRequest? Flexible(
        string? stay, int flex, int months, string? startMonths, DateOnly? checkIn, DateOnly? checkOut)
    {
        var length = (stay ?? "").Trim().ToLowerInvariant() switch
        {
            "weekend" => StayLength.Weekend,
            "week" => StayLength.Week,
            "month" => StayLength.Month,
            "months" => StayLength.Months,
            _ => StayLength.Exact
        };

        if (length == StayLength.Exact && flex <= 0) return null;

        var starts = (startMonths ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => DateOnly.TryParse($"{m}-01", out var d) ? d : (DateOnly?)null)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToList();

        return new FlexibleRequest
        {
            Length = length,
            CheckIn = checkIn,
            CheckOut = checkOut,
            FlexDays = Math.Clamp(flex, 0, FlexibleDates.MaxShift),
            Months = months,
            StartMonths = starts
        };
    }

    /// <summary>docs/01 YT-07 — cards for 2–5 listings the guest wants side by side.</summary>
    [HttpGet("listings/compare")]
    public async Task<ActionResult<IReadOnlyList<ListingCardDto>>> Compare(
        [FromQuery] string? ids, CancellationToken ct = default)
    {
        var wanted = (ids ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n > 0)
            .ToList();
        return Ok(await catalog.CompareAsync(wanted, HttpContext.SessionId(), ct));
    }

    [HttpGet("listings/{idOrSlug}")]
    public async Task<ActionResult<ListingDetailDto>> Detail(
        string idOrSlug,
        [FromQuery] DateOnly? checkIn,
        [FromQuery] DateOnly? checkOut,
        [FromQuery] int guests = 1,
        [FromQuery] int infants = 0,
        [FromQuery] int pets = 0,
        CancellationToken ct = default)
    {
        var detail = await catalog.GetDetailAsync(
            idOrSlug, HttpContext.SessionId(), checkIn, checkOut, guests, ct, infants, pets);
        if (detail is null) return NotFound();

        // docs/03 §6 — the view half of "tỉ lệ xem→đặt". Counted after the page
        // was actually served, and never allowed to fail the request: a ranking
        // signal is not worth a 500 on the page somebody came to read.
        await catalog.RecordViewAsync(detail.Card.Id, ct);

        return Ok(detail);
    }

    [HttpGet("quote")]
    public async Task<ActionResult<QuoteDto>> Quote(
        [FromQuery] int listingId,
        [FromQuery] DateOnly checkIn,
        [FromQuery] DateOnly checkOut,
        [FromQuery] int guests = 1,
        [FromQuery] int? adults = null,
        [FromQuery] int children = 0,
        [FromQuery] int infants = 0,
        [FromQuery] int pets = 0,
        [FromQuery] int? roomTypeId = null,
        [FromQuery] string? couponCode = null,
        CancellationToken ct = default)
    {
        // `guests` is the legacy single number; adults/children win when supplied.
        var party = adults is null
            ? PartySize.Of(guests) with { Infants = infants, Pets = pets }
            : new PartySize(Math.Max(1, adults.Value), children, infants, pets);

        var quote = await catalog.QuoteAsync(listingId, checkIn, checkOut, party, ct, roomTypeId);
        if (quote is null) return NotFound();

        // docs/01 ĐP-09 — the quote is where a code is checked, so the guest sees
        // the discount before committing. A signed-out visitor cannot, since the
        // per-guest limit needs to know who they are.
        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            var user = await auth.CurrentUserAsync(ct);
            if (user is null)
                return Ok(quote with { CouponError = "Đăng nhập để dùng mã giảm giá." });

            var gross = quote.Subtotal + quote.ServiceFee + quote.Tax;
            var check = await coupons.EvaluateAsync(couponCode, user.Id, gross, DateTime.UtcNow, ct: ct);

            quote = check.Ok
                ? await catalog.QuoteAsync(listingId, checkIn, checkOut, party, ct, roomTypeId,
                    check.Discount, check.Label)
                : quote with { CouponError = check.Error };
        }

        return Ok(quote);
    }
}
