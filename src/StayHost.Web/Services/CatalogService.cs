using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;

namespace StayHost.Web.Services;

/// <summary>Everything the browse / detail pages need, in one place.</summary>
public class CatalogService(StayHostDbContext db, LoyaltyService loyalty)
{
    public static readonly (PlaceType Type, string Key, string Label, string Icon)[] Categories =
    [
        (0,                  "all",       "Tất cả",        "◈"),
        (PlaceType.Villa,     "villa",     "Villa",         "⌂"),
        (PlaceType.Apartment, "apartment", "Căn hộ",        "▤"),
        (PlaceType.Homestay,  "homestay",  "Homestay",      "✿"),
        (PlaceType.House,     "house",     "Nhà nguyên căn", "⬡"),
        (PlaceType.Cabin,     "cabin",     "Cabin gỗ",      "⛰"),
        (PlaceType.Boutique,  "boutique",  "Boutique",      "✧"),
        (PlaceType.Hotel,     "hotel",     "Khách sạn",     "▣")
    ];

    private static readonly RoomTypeDto[] RoomTypes =
    [
        new("any", "Bất kỳ", "Mọi loại chỗ ở"),
        new("entire", "Nguyên căn", "Bạn dùng trọn chỗ nghỉ"),
        new("private", "Phòng riêng", "Phòng riêng, chia sẻ khu vực chung"),
        new("shared", "Phòng chung", "Ngủ chung phòng với khách khác")
    ];

    /// <summary>
    /// docs/01 QT-06/TC-12 — the live list is exchange_rates in the database,
    /// operator-editable; this array is only the fallback for a database that
    /// has not migrated yet, because an empty currency picker is worse than a
    /// stale one. Rates here stopped being maintained on 05/09/2026.
    /// </summary>
    private static readonly CurrencyDto[] DefaultCurrencies =
    [
        new("VND", "Việt Nam Đồng", "₫", 1m),
        new("USD", "US Dollar", "$", 0.0000392m),
        new("EUR", "Euro", "€", 0.0000362m),
        new("JPY", "Japanese Yen", "¥", 0.00596m),
        new("KRW", "South Korean Won", "₩", 0.0535m),
        new("SGD", "Singapore Dollar", "S$", 0.0000508m),
        new("AUD", "Australian Dollar", "A$", 0.0000602m),
        new("GBP", "British Pound", "£", 0.0000309m)
    ];

    private static readonly LanguageDto[] Languages =
    [
        new("vi", "Tiếng Việt", "Việt Nam"),
        new("en", "English", "United States"),
        new("ja", "日本語", "日本"),
        new("ko", "한국어", "대한민국"),
        new("zh", "中文 (简体)", "中国"),
        new("fr", "Français", "France"),
        new("de", "Deutsch", "Deutschland"),
        new("es", "Español", "España")
    ];

    public static string CategoryKey(PlaceType t) =>
        Categories.FirstOrDefault(c => c.Type == t).Key ?? "all";

    public static string CategoryLabel(PlaceType t) =>
        Categories.FirstOrDefault(c => c.Type == t).Label ?? t.ToString();

    public static string RoomTypeLabel(RoomType r) => r switch
    {
        RoomType.EntirePlace => "Nguyên căn",
        RoomType.PrivateRoom => "Phòng riêng",
        _ => "Phòng chung"
    };

    public async Task<MetaDto> GetMetaAsync(CancellationToken ct)
    {
        // docs/01 QT-06/TC-12 — rates come from the database so an operator can
        // change one without a deploy. An eight-row indexed read; /api/meta must
        // never gain an outbound HTTP call, since it is on every first paint.
        var currencies = await db.ExchangeRates
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder)
            .Select(r => new CurrencyDto(r.Code, r.Label, r.Symbol, r.RateFromVnd))
            .ToListAsync(ct);
        if (currencies.Count == 0) currencies = DefaultCurrencies.ToList();

        var counts = await db.Listings
            .GroupBy(l => l.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var total = counts.Sum(c => c.Count);

        var categories = Categories.Select(c => new CategoryDto(
            c.Key, c.Label, c.Icon,
            c.Key == "all" ? total : counts.FirstOrDefault(x => x.Type == c.Type)?.Count ?? 0)).ToList();

        var amenities = await db.Amenities
            .Where(a => a.IsFilterable)
            .OrderBy(a => a.SortOrder)
            .Select(a => new AmenityDto(a.Key, a.Label, a.Icon, a.Group))
            .ToListAsync(ct);

        var cities = await db.Listings.Select(l => l.City).Distinct().OrderBy(c => c).ToListAsync(ct);

        // docs/01 TM-26 — only cities the public can actually open. CitiesController
        // returns 404 for a city whose listings are all hidden, so linking one from
        // the footer would advertise a dead page to every crawler on every page.
        var visible = await db.Listings
            .Where(l => l.IsPublished && l.ReviewStatus == ListingReviewStatus.Approved)
            .Select(l => l.City)
            .ToListAsync(ct);

        var cityLinks = visible
            .GroupBy(Cities.Key)
            .Where(g => g.Key.Length > 0)
            .Select(g => new CityLinkDto(g.First(), g.Key.Replace(' ', '-'), g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Name)
            .ToList();
        var prices = await db.Listings.Select(l => l.PricePerNight).ToListAsync(ct);

        var min = prices.Count == 0 ? 0 : prices.Min();
        var max = prices.Count == 0 ? 6_000_000m : prices.Max();
        var histogram = BuildHistogram(prices, min, max, 26);

        return new MetaDto(
            categories,
            amenities,
            amenities.Take(6).ToList(),
            cities,
            RoomTypes,
            Math.Floor(min / 100_000m) * 100_000m,
            Math.Ceiling(max / 100_000m) * 100_000m,
            histogram,
            currencies,
            Languages,
            new FeesDto(
                PricingSettings.Current.GuestServiceFeeRate,
                PricingSettings.Current.HostServiceFeeRate,
                PricingSettings.Current.MaxDiscountPercent,
                PricingSettings.Current.DefaultCleaningFee),
            cityLinks);
    }

    private static List<int> BuildHistogram(List<decimal> prices, decimal min, decimal max, int buckets)
    {
        var result = Enumerable.Repeat(0, buckets).ToList();
        if (prices.Count == 0 || max <= min) return result;
        foreach (var p in prices)
        {
            var idx = (int)((p - min) / (max - min) * (buckets - 1));
            result[Math.Clamp(idx, 0, buckets - 1)]++;
        }
        return result;
    }

    /// <summary>
    /// The landing page mirrors Airbnb's discovery layout: horizontal carousels grouped by
    /// destination, then a few themed rows, then an inspiration link grid.
    /// </summary>
    public async Task<HomeDto> GetHomeAsync(
        string sessionId, DateOnly? checkIn, DateOnly? checkOut, int guests, CancellationToken ct,
        int infantCount = 0, int petCount = 0)
    {
        var all = await db.Listings
            // docs/01 AT-01 — only published *and* approved places reach the public.
            .Where(l => l.IsPublished && l.ReviewStatus == ListingReviewStatus.Approved)
            .Include(l => l.Images)
            .Include(l => l.Amenities).ThenInclude(la => la.Amenity)
            .ToListAsync(ct);

        var favIds = await FavoriteIdsAsync(sessionId, ct);
        var pricer = await BuildPricerAsync(
            all, checkIn, checkOut,
            PartySize.Of(Math.Max(1, guests)) with { Infants = infantCount, Pets = petCount }, ct);
        var cards = all.Select(l => ToCard(l, favIds, pricer)).ToList();

        var sections = new List<HomeSectionDto>();

        foreach (var group in cards.GroupBy(c => c.City)
                     .Where(g => g.Count() >= 3)
                     .OrderByDescending(g => g.Count())
                     .ThenByDescending(g => g.Max(c => c.Rating))
                     .Take(5))
        {
            sections.Add(new HomeSectionDto(
                $"city-{group.Key}",
                $"Chỗ nghỉ được yêu thích ở {group.Key}",
                null,
                $"/?q={Uri.EscapeDataString(group.Key)}",
                group.OrderByDescending(c => c.Rating).ToList()));
        }

        // A rail is a horizontal scroller, not an index — keep it to a browsable length.
        const int railSize = 12;

        var pools = cards.Where(c => c.AmenityKeys.Contains("pool")).OrderByDescending(c => c.Rating).Take(railSize).ToList();
        if (pools.Count > 0)
            sections.Add(new HomeSectionDto("theme-pool", "Chỗ nghỉ có hồ bơi riêng",
                "Bơi lúc nào cũng được, không phải chia sẻ với ai", "/?amenities=pool", pools));

        var budget = cards.Where(c => c.PricePerNight <= 1_200_000m).OrderBy(c => c.PricePerNight).Take(railSize).ToList();
        if (budget.Count > 0)
            sections.Add(new HomeSectionDto("theme-budget", "Dưới 1,2 triệu mỗi đêm",
                "Tiết kiệm mà vẫn được đánh giá cao", "/?maxPrice=1200000", budget));

        var pets = cards.Where(c => c.AmenityKeys.Contains("pet")).OrderByDescending(c => c.Rating).Take(railSize).ToList();
        if (pets.Count > 0)
            sections.Add(new HomeSectionDto("theme-pet", "Cho mang theo thú cưng",
                "Đi đâu cũng có bạn bốn chân đi cùng", "/?amenities=pet", pets));

        var cities = cards.Select(c => c.City).Distinct().OrderBy(c => c).ToList();

        var inspiration = new List<InspirationGroupDto>
        {
            new("Phổ biến", cities.Take(8)
                .Select(c => new InspirationLinkDto(c, "Chỗ nghỉ nguyên căn", $"/?q={Uri.EscapeDataString(c)}")).ToList()),
            new("Ven biển", cards.Where(c => c.AmenityKeys.Contains("beach")).Select(c => c.City).Distinct().Take(8)
                .Select(c => new InspirationLinkDto(c, "Chỗ nghỉ sát biển", $"/?q={Uri.EscapeDataString(c)}")).ToList()),
            new("Vùng núi", cities.Where(c => c is "Đà Lạt" or "Sa Pa" or "Tam Đảo" or "Ninh Bình")
                .Select(c => new InspirationLinkDto(c, "Cabin & homestay vùng cao", $"/?q={Uri.EscapeDataString(c)}")).ToList()),
            new("Loại chỗ ở", Categories.Where(c => c.Key != "all")
                .Select(c => new InspirationLinkDto(c.Label, "Trên khắp Việt Nam", $"/?category={c.Key}")).ToList())
        };

        return new HomeDto(sections, inspiration.Where(g => g.Links.Count > 0).ToList());
    }

    public record SuggestionDto(string Label, string Sub, string Kind, string Value, int Count);

    /// <summary>Destination autocomplete: cities first, then matching listings.</summary>
    public async Task<IReadOnlyList<SuggestionDto>> SuggestAsync(string? term, CancellationToken ct)
    {
        var published = db.Listings.Where(l => l.IsPublished && l.ReviewStatus == ListingReviewStatus.Approved);

        var cities = await published
            .GroupBy(l => l.City)
            .Select(g => new { City = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var query = (term ?? "").Trim();

        // No input yet: show the biggest destinations, like Airbnb's "recent/nearby" panel.
        if (query.Length == 0)
        {
            return cities
                .OrderByDescending(c => c.Count)
                .Take(6)
                .Select(c => new SuggestionDto(c.City, $"{c.Count} chỗ nghỉ", "city", c.City, c.Count))
                .ToList();
        }

        var matches = cities
            .Where(c => c.City.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Count)
            .Take(5)
            .Select(c => new SuggestionDto(c.City, $"{c.Count} chỗ nghỉ", "city", c.City, c.Count))
            .ToList();

        var listings = await published
            .Where(l => EF.Functions.ILike(l.Title, $"%{query}%"))
            .OrderByDescending(l => l.Rating)
            .Take(4)
            .Select(l => new SuggestionDto(l.Title, l.City, "listing", l.Slug, 0))
            .ToListAsync(ct);

        return matches.Concat(listings).ToList();
    }

    public record SearchQuery(
        string? Q,
        string? Category,
        decimal? MinPrice,
        decimal? MaxPrice,
        int Guests,
        string[] Amenities,
        string? Sort,
        string? RoomType,
        int? Bedrooms,
        int? Beds,
        int? Bathrooms,
        bool SuperhostOnly,
        bool GuestFavoriteOnly,
        bool InstantBookOnly,
        bool FreeCancellationOnly,
        int Page,
        int PageSize,
        DateOnly? CheckIn = null,
        DateOnly? CheckOut = null,
        MapBounds? Bounds = null,
        /// <summary>docs/01 TM-06 and TM-07 — a length and a tolerance instead of two firm dates.</summary>
        FlexibleRequest? Flex = null,
        /// <summary>
        /// Listings with nothing free in the searched window. Filled in by
        /// <see cref="ResolveDatesAsync"/> so counting, paging and the no-results
        /// explanation all see the same set.
        /// </summary>
        IReadOnlySet<int>? Unavailable = null,
        /// <summary>The stay each listing was matched on, when the dates were flexible.</summary>
        IReadOnlyDictionary<int, StayWindow>? Matched = null,
        /// <summary>
        /// The rest of the party. docs/00 §6.8 wants a card priced by the same
        /// engine checkout uses, and the engine charges for a pet
        /// (<see cref="Pricing"/>). Carrying only a head count left every card
        /// and every detail header quoting a stay without the pet fee, while
        /// the booking panel - which asks /api/quote with the full party -
        /// quoted it with. Infants ride along because they are part of the
        /// party even though they are free.
        /// </summary>
        int Infants = 0,
        int Pets = 0,
        /// <summary>docs/01 TM-18 — language codes the host must speak at least one of.</summary>
        IReadOnlyList<string>? HostLanguages = null,
        /// <summary>docs/01 TM-24 — a hand-drawn search area, as lat/lng points.</summary>
        IReadOnlyList<GeoPolygon.Point>? Polygon = null,
        /// <summary>Listing ids inside the drawn area, resolved by <see cref="ResolveAreaAsync"/>.</summary>
        IReadOnlySet<int>? InArea = null,
        /// <summary>Only places with reviews averaging at least this (5-point scale).</summary>
        double? MinRating = null,
        /// <summary>Only places where the guest can pay on arrival — "không cần trả trước".</summary>
        bool PayAtPropertyOnly = false,
        /// <summary>Only places within this many km of their city's centre.</summary>
        double? MaxCentreKm = null,
        /// <summary>Listing ids within <see cref="MaxCentreKm"/>, resolved by <see cref="ResolveAreaAsync"/>.</summary>
        IReadOnlySet<int>? NearCentre = null,
        /// <summary>Hotel star classes wanted; a place matches if it has any of them.</summary>
        IReadOnlyList<int>? Stars = null,
        /// <summary>Breakfast available: the free-breakfast amenity, or a hotel room that sells it.</summary>
        bool BreakfastOnly = false,
        /// <summary>Something off the price right now — a promotion, an early-bird, last-minute or loyalty rate.</summary>
        bool DealsOnly = false);

    /// <summary>The visible map rectangle, when the guest is searching by moving it.</summary>
    public readonly record struct MapBounds(double South, double West, double North, double East);

    /// <summary>
    /// Every filter of a search, with no ordering or paging. Shared so the
    /// no-results explanation (docs/01 TM-22) can re-run the same query with one
    /// filter dropped and count what comes back.
    /// </summary>
    private IQueryable<Listing> BaseQuery(SearchQuery q)
    {
        IQueryable<Listing> query = db.Listings.Where(
            l => l.IsPublished && l.ReviewStatus == ListingReviewStatus.Approved);

        // docs/03 §6: "da lat" must find Đà Lạt and "hcm" must find Thành phố Hồ
        // Chí Minh, so the match runs against the normalised column and every
        // typed word has to appear somewhere in it.
        foreach (var term in SearchText.Terms(q.Q))
        {
            var t = term;
            query = query.Where(l => EF.Functions.Like(l.SearchText, $"%{t}%"));
        }

        // docs/01 TM-12 — searching again as the map moves.
        if (q.Bounds is { } b)
        {
            query = query.Where(l =>
                l.Latitude >= b.South && l.Latitude <= b.North &&
                l.Longitude >= b.West && l.Longitude <= b.East);
        }

        if (!string.IsNullOrWhiteSpace(q.Category) && q.Category != "all")
        {
            var match = Categories.FirstOrDefault(c => c.Key == q.Category);
            if (match.Key is not null && match.Key != "all")
                query = query.Where(l => l.Type == match.Type);
        }

        if (q.MinPrice is > 0) query = query.Where(l => l.PricePerNight >= q.MinPrice);
        if (q.MaxPrice is > 0) query = query.Where(l => l.PricePerNight <= q.MaxPrice);
        if (q.Guests > 0) query = query.Where(l => l.MaxGuests >= q.Guests);
        if (q.Bedrooms is > 0) query = query.Where(l => l.Bedrooms >= q.Bedrooms);
        if (q.Beds is > 0) query = query.Where(l => l.Beds >= q.Beds);
        if (q.Bathrooms is > 0) query = query.Where(l => l.Bathrooms >= q.Bathrooms);
        if (q.SuperhostOnly) query = query.Where(l => l.IsSuperhost);
        if (q.GuestFavoriteOnly) query = query.Where(l => l.IsGuestFavorite);
        if (q.InstantBookOnly) query = query.Where(l => l.InstantBook);
        // docs/03 §4 — the same two tiers Cancellation.HasFreeCancellation names.
        // Spelled out rather than called because this has to translate to SQL,
        // so the two must be kept in step; CancellationTests holds them to it.
        if (q.FreeCancellationOnly)
            query = query.Where(l =>
                l.CancellationTier == CancellationTier.Flexible || l.CancellationTier == CancellationTier.Moderate);

        if (!string.IsNullOrWhiteSpace(q.RoomType) && q.RoomType != "any")
        {
            var rt = q.RoomType switch
            {
                "entire" => RoomType.EntirePlace,
                "private" => RoomType.PrivateRoom,
                "shared" => RoomType.SharedRoom,
                _ => (RoomType?)null
            };
            if (rt is not null) query = query.Where(l => l.RoomType == rt);
        }

        foreach (var key in q.Amenities.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct())
        {
            var k = key;
            query = query.Where(l => l.Amenities.Any(la => la.Amenity!.Key == k));
        }

        // docs/01 TM-18 — filter by a language the host speaks. The host's spoken
        // languages come from their own profile (TĐ-14); a listing matches when the
        // host speaks at least one of the chosen codes. Built as a UNION so each
        // Contains stays a SQL predicate rather than an untranslatable Any-lambda.
        if (q.HostLanguages is { Count: > 0 } hostLangs)
        {
            var wanted = hostLangs.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct().ToList();
            if (wanted.Count == 1)
            {
                var code = wanted[0];
                query = query.Where(l => l.Host!.User!.SpokenLanguages.Contains(code));
            }
            else if (wanted.Count > 1)
            {
                IQueryable<Listing>? combined = null;
                foreach (var w in wanted)
                {
                    var code = w;
                    var part = query.Where(l => l.Host!.User!.SpokenLanguages.Contains(code));
                    combined = combined is null ? part : combined.Union(part);
                }
                query = combined!;
            }
        }

        // docs/01 TM-24 — inside the hand-drawn area. The exact point-in-polygon
        // test ran in ResolveAreaAsync; here it is just an id-set membership so the
        // count and paging see the same places.
        if (q.InArea is { } area)
            query = query.Where(l => area.Contains(l.Id));

        // A score needs reviews behind it: a new place's 0 is not "below 4".
        if (q.MinRating is > 0 and var minRating)
            query = query.Where(l => l.ReviewCount > 0 && l.Rating >= minRating);
        if (q.PayAtPropertyOnly) query = query.Where(l => l.AcceptsPayAtProperty);
        if (q.Stars is { Count: > 0 } stars)
            query = query.Where(l => stars.Contains(l.HotelStars));
        if (q.BreakfastOnly)
            query = query.Where(l => l.Amenities.Any(a => a.Amenity!.Key == "breakfast")
                                     || db.RoomTypes.Any(r => r.ListingId == l.Id && r.BreakfastPricePerGuest > 0));
        if (q.DealsOnly)
            query = query.Where(l => l.DiscountPercent > 0 || l.EarlyBirdPercent > 0
                                     || l.LastMinutePercent > 0 || l.LoyaltyDiscountPercent > 0);
        if (q.NearCentre is { } near)
            query = query.Where(l => near.Contains(l.Id));

        // Dates are a filter like any other: a place with someone already in it
        // has no business on the results page (docs/01 TM-05, TM-06).
        if (q.Unavailable is { Count: > 0 } taken)
            query = query.Where(l => !taken.Contains(l.Id));

        return query;
    }

    /// <summary>
    /// Works out which stays the guest would accept and which listings can host
    /// none of them. One pass over the bookings and blocks inside the searched
    /// span answers it for the whole catalogue, so counting and paging stay cheap.
    /// </summary>
    public async Task<SearchQuery> ResolveDatesAsync(SearchQuery q, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = q.Flex ?? new FlexibleRequest { CheckIn = q.CheckIn, CheckOut = q.CheckOut };
        var windows = FlexibleDates.Windows(request, today);
        if (windows.Count == 0) return q with { Unavailable = null, Matched = null };

        var from = windows.Min(w => w.CheckIn);
        var to = windows.Max(w => w.CheckOut);

        var stays = await db.Bookings
            .Where(b => BookingLifecycle.BlocksDates.Contains(b.Status) && b.CheckIn < to && from < b.CheckOut)
            .Select(b => new { b.ListingId, From = b.CheckIn, To = b.CheckOut })
            .ToListAsync(ct);

        // A block names its last blocked day; the rest of the code works in
        // half-open ranges, so push the end out by one.
        var blocks = (await db.CalendarBlocks
                .Where(b => b.From < to && from <= b.To)
                .Select(b => new { b.ListingId, b.From, b.To })
                .ToListAsync(ct))
            .Select(b => new { b.ListingId, b.From, To = b.To.AddDays(1) });

        var occupied = stays.Concat(blocks)
            .GroupBy(x => x.ListingId)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.From, x.To)).ToList());

        var unavailable = new HashSet<int>();
        var matched = new Dictionary<int, StayWindow>();

        foreach (var (listingId, ranges) in occupied)
        {
            var free = FlexibleDates.FirstFree(windows, ranges);
            if (free is null) unavailable.Add(listingId);
            else if (free != windows[0]) matched[listingId] = free.Value;
        }

        return q with
        {
            CheckIn = windows[0].CheckIn,
            CheckOut = windows[0].CheckOut,
            Unavailable = unavailable,
            Matched = matched
        };
    }

    /// <summary>
    /// docs/01 TM-24 — turns a drawn polygon into the set of listing ids inside it.
    /// The polygon's bounding box does the heavy lifting in SQL (indexed lat/lng);
    /// the exact point-in-polygon test then runs in memory over that small set, so
    /// counting and paging all see the same places.
    /// </summary>
    public async Task<SearchQuery> ResolveAreaAsync(SearchQuery q, CancellationToken ct)
    {
        // Distance to the centre is worked out per listing in memory — the
        // centres are a list in code, not a table SQL could join.
        if (q.MaxCentreKm is > 0 and var maxKm && q.NearCentre is null)
        {
            var located = await db.Listings
                .Where(l => l.IsPublished && l.ReviewStatus == ListingReviewStatus.Approved)
                .Select(l => new { l.Id, l.City, l.Latitude, l.Longitude })
                .ToListAsync(ct);
            q = q with
            {
                NearCentre = located
                    .Where(l => Landmarks.FromCentreKm(l.City, l.Latitude, l.Longitude) is { } km && km <= maxKm)
                    .Select(l => l.Id).ToHashSet()
            };
        }

        if (q.Polygon is not { Count: >= 3 } polygon) return q;

        var (south, west, north, east) = GeoPolygon.Bounds(polygon);
        var candidates = await db.Listings
            .Where(l => l.IsPublished && l.ReviewStatus == ListingReviewStatus.Approved
                        && l.Latitude >= south && l.Latitude <= north
                        && l.Longitude >= west && l.Longitude <= east)
            .Select(l => new { l.Id, l.Latitude, l.Longitude })
            .ToListAsync(ct);

        var inArea = candidates
            .Where(c => GeoPolygon.Contains(polygon, c.Latitude, c.Longitude))
            .Select(c => c.Id)
            .ToHashSet();

        return q with { InArea = inArea };
    }

    public async Task<int> CountAsync(SearchQuery q, CancellationToken ct) =>
        await BaseQuery(await ResolveAreaAsync(q, ct)).CountAsync(ct);

    /// <summary>
    /// docs/01 YT-07 — the cards for a handful of listings the guest picked to
    /// compare side by side. Capped at five, returned in the order asked for, and
    /// only places the public can actually see.
    /// </summary>
    public async Task<IReadOnlyList<ListingCardDto>> CompareAsync(
        IReadOnlyList<int> ids, string sessionId, CancellationToken ct)
    {
        var wanted = ids.Distinct().Take(5).ToList();
        if (wanted.Count == 0) return [];

        var listings = await db.Listings
            .Where(l => wanted.Contains(l.Id)
                        && l.IsPublished && l.ReviewStatus == ListingReviewStatus.Approved)
            .Include(l => l.Images)
            .Include(l => l.Amenities).ThenInclude(a => a.Amenity)
            .ToListAsync(ct);

        var favIds = await FavoriteIdsAsync(sessionId, ct);
        var byId = listings.ToDictionary(l => l.Id);
        return wanted.Where(byId.ContainsKey).Select(id => ToCard(byId[id], favIds)).ToList();
    }

    /// <summary>
    /// docs/01 TM-23 — listings that match a saved search and were created after a
    /// high-water mark, so the alert sweep only ever sees genuinely new places.
    /// Dates are ignored: a new listing's calendar is open and the alert is about
    /// the place matching, not a particular week.
    /// </summary>
    public Task<List<Listing>> MatchNewAsync(SearchQuery q, int afterListingId, int take, CancellationToken ct) =>
        BaseQuery(q)
            .Where(l => l.Id > afterListingId)
            .OrderBy(l => l.Id)
            .Take(take)
            .ToListAsync(ct);

    public async Task<SearchResultDto> SearchAsync(SearchQuery q, string sessionId, CancellationToken ct)
    {
        q = await ResolveAreaAsync(await ResolveDatesAsync(q, ct), ct);

        var pageSize = Math.Clamp(q.PageSize, 1, 60);
        var page = Math.Max(1, q.Page);

        // docs/03 §6 — the default order is a weighted score, not a column, so
        // it is worked out over the whole filtered set rather than by the
        // database. The named sorts stay in SQL, where they belong.
        List<Listing> items;
        int total;

        if (q.Sort is "low" or "high" or "rating" or "reviews")
        {
            var ordered = q.Sort switch
            {
                "low" => BaseQuery(q).OrderBy(l => l.PricePerNight).ThenBy(l => l.Id),
                "high" => BaseQuery(q).OrderByDescending(l => l.PricePerNight).ThenBy(l => l.Id),
                "rating" => BaseQuery(q).OrderByDescending(l => l.Rating).ThenByDescending(l => l.ReviewCount),
                _ => BaseQuery(q).OrderByDescending(l => l.ReviewCount).ThenByDescending(l => l.Rating)
            };

            total = await ordered.CountAsync(ct);
            items = await ordered
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Include(l => l.Images)
                .Include(l => l.Amenities).ThenInclude(la => la.Amenity)
                .AsSplitQuery()
                .ToListAsync(ct);
        }
        else if (q.Sort == "distance")
        {
            // Closest to its own city's centre first; places whose centre is
            // unknown go last rather than being dropped.
            var located = await BaseQuery(q)
                .Select(l => new { l.Id, l.City, l.Latitude, l.Longitude })
                .ToListAsync(ct);
            total = located.Count;
            var pageIds = located
                .OrderBy(l => Landmarks.FromCentreKm(l.City, l.Latitude, l.Longitude) ?? double.MaxValue)
                .ThenBy(l => l.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(l => l.Id).ToList();
            var loaded = await db.Listings
                .Where(l => pageIds.Contains(l.Id))
                .Include(l => l.Images)
                .Include(l => l.Amenities).ThenInclude(la => la.Amenity)
                .AsSplitQuery()
                .ToDictionaryAsync(l => l.Id, ct);
            items = pageIds.Where(loaded.ContainsKey).Select(id => loaded[id]).ToList();
        }
        else
        {
            var ranked = await RankAsync(q, ct);
            total = ranked.Count;
            items = ranked.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }
        var favIds = await FavoriteIdsAsync(sessionId, ct);
        var pricer = await BuildPricerAsync(
            items, q.CheckIn, q.CheckOut,
            PartySize.Of(Math.Max(1, q.Guests)) with { Infants = q.Infants, Pets = q.Pets },
            ct, q.Matched);

        return new SearchResultDto(
            total, page, pageSize,
            items.Select(l => ToCard(l, favIds, pricer)).ToList(),
            total == 0 ? await ExplainEmptyAsync(q, ct) : null,
            DescribeDates(q));
    }

    /// <summary>
    /// docs/03 §6 — one detail-page view. Upserted so the table stays at one row
    /// per listing per day rather than one per visit.
    /// </summary>
    public async Task RecordViewAsync(int listingId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            var updated = await db.ListingViews
                .Where(v => v.ListingId == listingId && v.Day == today)
                .ExecuteUpdateAsync(u => u.SetProperty(v => v.Views, v => v.Views + 1), ct);

            if (updated > 0) return;

            db.ListingViews.Add(new ListingView { ListingId = listingId, Day = today, Views = 1 });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first views of the day at once: the unique index rejects the
            // loser, whose count is then simply added to the row that won.
            db.ChangeTracker.Clear();
            await db.ListingViews
                .Where(v => v.ListingId == listingId && v.Day == today)
                .ExecuteUpdateAsync(u => u.SetProperty(v => v.Views, v => v.Views + 1), ct);
        }
    }

    /// <summary>
    /// docs/03 §6 — the whole filtered set, scored and ordered, then thinned so
    /// one host cannot hold the first page.
    ///
    /// Scoring happens in memory because the formula weighs seven things the
    /// database cannot join in one pass. That is affordable while a search
    /// matches thousands of rows rather than millions; past that this wants a
    /// precomputed score column and a nightly job, and the rule in
    /// <see cref="Ranking"/> would not have to change for it.
    /// </summary>
    private async Task<List<Listing>> RankAsync(SearchQuery q, CancellationToken ct)
    {
        var pool = await BaseQuery(q)
            .Include(l => l.Images)
            .Include(l => l.Amenities).ThenInclude(la => la.Amenity)
            .Include(l => l.Host)
            .AsSplitQuery()
            .ToListAsync(ct);

        if (pool.Count <= 1) return pool;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ids = pool.Select(l => l.Id).ToList();

        // "Gần trung tâm khu vực tìm": the middle of the map rectangle when the
        // guest is searching by moving it, otherwise the middle of what they
        // matched — which is the area they are looking at, by definition.
        var (centreLat, centreLng) = q.Bounds is { } b
            ? ((b.South + b.North) / 2, (b.West + b.East) / 2)
            : (pool.Average(l => l.Latitude), pool.Average(l => l.Longitude));

        var distances = pool.ToDictionary(
            l => l.Id, l => Ranking.DistanceKm(centreLat, centreLng, l.Latitude, l.Longitude));

        // The edge of the area is the furthest thing in it, with a floor so a
        // single-city search does not turn a 200m gap into the whole scale.
        var radiusKm = Math.Max(5, distances.Values.DefaultIfEmpty(0).Max());

        var median = Median(pool.Select(l => l.PricePerNight).ToList());

        var since = today.AddDays(-Ranking.FreshDays);

        var views = await db.ListingViews
            .Where(v => ids.Contains(v.ListingId) && v.Day >= since)
            .GroupBy(v => v.ListingId)
            .Select(g => new { ListingId = g.Key, Views = g.Sum(v => v.Views) })
            .ToDictionaryAsync(x => x.ListingId, x => x.Views, ct);

        var bookedSince = DateTime.UtcNow.AddDays(-Ranking.FreshDays);
        var bookings = await db.Bookings
            .Where(bk => ids.Contains(bk.ListingId) && bk.CreatedAt >= bookedSince)
            .GroupBy(bk => bk.ListingId)
            .Select(g => new { ListingId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ListingId, x => x.Count, ct);

        var hostIds = pool.Select(l => l.HostId).Distinct().ToList();
        var yearAgo = today.AddYears(-1);

        var hostOrders = await db.Bookings
            .Where(bk => hostIds.Contains(bk.Listing!.HostId) && bk.CheckIn >= yearAgo)
            .GroupBy(bk => bk.Listing!.HostId)
            .Select(g => new
            {
                HostId = g.Key,
                Total = g.Count(),
                Cancelled = g.Count(bk => bk.Status == BookingStatus.CancelledByHost
                                          && bk.CancelledBy == CancelledBy.Host)
            })
            .ToListAsync(ct);

        var cancelRate = hostOrders.ToDictionary(
            h => h.HostId, h => h.Total == 0 ? 0 : h.Cancelled * 100.0 / h.Total);

        var scored = pool
            .Select(l => new
            {
                Listing = l,
                Score = Ranking.Score(new Ranking.Candidate(
                    l.Id,
                    l.HostId,
                    distances[l.Id],
                    radiusKm,
                    l.Rating,
                    l.ReviewCount,
                    views.GetValueOrDefault(l.Id),
                    bookings.GetValueOrDefault(l.Id),
                    l.PricePerNight,
                    median,
                    BadgeService.ParsePercent(l.Host?.ResponseRate),
                    l.InstantBook,
                    l.Images.Count,
                    (today.DayNumber - DateOnly.FromDateTime(l.CreatedAt).DayNumber),
                    cancelRate.GetValueOrDefault(l.HostId),
                    l.IsComplete))
            })
            // Id last so the order is stable across identical scores; without it
            // paging can show the same listing twice.
            .OrderByDescending(x => x.Score).ThenBy(x => x.Listing.Id)
            .Select(x => x.Listing)
            .ToList();

        return Ranking.Diversify(scored, l => l.HostId);
    }

    private static decimal Median(List<decimal> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
    }

    private static FlexibleDatesDto? DescribeDates(SearchQuery q)
    {
        if (q.Flex is not { IsFlexible: true } flex || q.CheckIn is not { } checkIn || q.CheckOut is not { } checkOut)
            return null;

        var options = FlexibleDates.Windows(flex, DateOnly.FromDateTime(DateTime.UtcNow)).Count;
        return new FlexibleDatesDto(
            flex.Length.ToString().ToLowerInvariant(),
            FlexibleDates.Label(flex.Length),
            checkOut.DayNumber - checkIn.DayNumber,
            checkIn, checkOut, options);
    }

    /// <summary>
    /// docs/01 TM-22. Re-runs the search with one filter removed at a time; a
    /// filter that unlocks results is the one worth telling the guest about.
    /// Then offers the nearest cities that do have something.
    /// </summary>
    private async Task<NoResultsDto> ExplainEmptyAsync(SearchQuery q, CancellationToken ct)
    {
        // Ordered from most likely to be the culprit to least, which is also the
        // order the second pass peels them off in.
        var candidates = new List<(string Key, string Label)>();

        if (q.Unavailable is { Count: > 0 }) candidates.Add(("dates", "Ngày đã chọn"));
        if (q.MinPrice is > 0 || q.MaxPrice is > 0) candidates.Add(("price", "Khoảng giá"));
        if (q.Amenities.Length > 0) candidates.Add(("amenities", $"{q.Amenities.Length} tiện nghi đã chọn"));
        if (q.SuperhostOnly) candidates.Add(("superhost", "Chỉ Siêu chủ nhà"));
        if (q.GuestFavoriteOnly) candidates.Add(("guestFavorite", "Chỉ Khách yêu thích"));
        if (q.InstantBookOnly) candidates.Add(("instantBook", "Chỉ Đặt ngay"));
        if (q.FreeCancellationOnly) candidates.Add(("freeCancellation", "Chỉ Huỷ miễn phí"));
        if (q.MinRating is > 0) candidates.Add(("minRating", "Điểm đánh giá"));
        if (q.PayAtPropertyOnly) candidates.Add(("payAtProperty", "Không cần trả trước"));
        if (q.MaxCentreKm is > 0) candidates.Add(("maxCentreKm", "Khoảng cách tới trung tâm"));
        if (q.Stars is { Count: > 0 }) candidates.Add(("stars", "Hạng sao khách sạn"));
        if (q.BreakfastOnly) candidates.Add(("breakfast", "Có bữa sáng"));
        if (q.DealsOnly) candidates.Add(("deals", "Đang có ưu đãi"));
        if (q.Bedrooms is > 0 || q.Beds is > 0 || q.Bathrooms is > 0) candidates.Add(("rooms", "Số phòng và giường"));
        if (!string.IsNullOrWhiteSpace(q.RoomType) && q.RoomType != "any") candidates.Add(("roomType", "Loại nơi ở"));
        if (!string.IsNullOrWhiteSpace(q.Category) && q.Category != "all") candidates.Add(("category", "Loại chỗ ở"));
        if (q.HostLanguages is { Count: > 0 }) candidates.Add(("hostLanguages", "Ngôn ngữ chủ nhà"));
        if (q.Guests > 0) candidates.Add(("guests", $"Sức chứa {q.Guests} khách"));

        // First pass: which single filter, dropped on its own, unlocks results.
        var blocking = new List<BlockingFilterDto>();
        foreach (var (key, label) in candidates)
        {
            var count = await CountAsync(Relax(q, key), ct);
            if (count > 0) blocking.Add(new BlockingFilterDto(key, label, count));
        }

        // Second pass: sometimes no single filter is the culprit. Peel them off
        // one at a time until something appears, so the guest still gets a way
        // forward instead of a dead end.
        var loosened = q;
        if (blocking.Count == 0)
        {
            foreach (var (key, label) in candidates)
            {
                loosened = Relax(loosened, key);
                var count = await CountAsync(loosened, ct);
                blocking.Add(new BlockingFilterDto(key, label, count));
                if (count > 0) break;
            }

            // Only worth showing if the peeling actually got somewhere.
            if (blocking.Count > 0 && blocking[^1].Count == 0) blocking.Clear();
        }

        // Nearby drops the destination as well as every filter that was in the
        // way, otherwise it would come back as empty as the search itself.
        var wide = loosened with { Q = null, Bounds = null };
        foreach (var f in blocking) wide = Relax(wide, f.Key);

        // Grouped into an anonymous shape because EF cannot project a positional
        // record out of a GroupBy.
        var elsewhere = await BaseQuery(wide)
            .GroupBy(l => l.City)
            .Select(g => new { City = g.Key, Count = g.Count(), FromPrice = g.Min(l => l.PricePerNight) })
            .OrderByDescending(a => a.Count)
            .Take(6)
            .ToListAsync(ct);

        return new NoResultsDto(
            blocking.OrderByDescending(b => b.Count).ToList(),
            elsewhere.Select(a => new NearbyAreaDto(a.City, a.Count, a.FromPrice)).ToList());
    }

    private static SearchQuery Relax(SearchQuery q, string key) => key switch
    {
        "dates" => q with { Unavailable = null },
        "price" => q with { MinPrice = null, MaxPrice = null },
        "amenities" => q with { Amenities = [] },
        "roomType" => q with { RoomType = "any" },
        "rooms" => q with { Bedrooms = null, Beds = null, Bathrooms = null },
        "guests" => q with { Guests = 0 },
        "superhost" => q with { SuperhostOnly = false },
        "guestFavorite" => q with { GuestFavoriteOnly = false },
        "instantBook" => q with { InstantBookOnly = false },
        "freeCancellation" => q with { FreeCancellationOnly = false },
        "category" => q with { Category = "all" },
        "hostLanguages" => q with { HostLanguages = null },
        "minRating" => q with { MinRating = null },
        "payAtProperty" => q with { PayAtPropertyOnly = false },
        "maxCentreKm" => q with { MaxCentreKm = null, NearCentre = null },
        "stars" => q with { Stars = null },
        "breakfast" => q with { BreakfastOnly = false },
        "deals" => q with { DealsOnly = false },
        _ => q
    };

    public async Task<HashSet<int>> FavoriteIdsAsync(string sessionId, CancellationToken ct) =>
        (await db.Favorites.Where(f => f.SessionId == sessionId).Select(f => f.ListingId).ToListAsync(ct)).ToHashSet();

    /// <summary>
    /// Prices a page of cards with the same engine checkout uses, so acceptance
    /// scenario 1 of docs/04 holds: the number on the card, the room page and the
    /// payment page are the same number.
    /// </summary>
    public sealed class StayPricer
    {
        private readonly Dictionary<int, List<PriceRule>> _rulesByListing;
        private readonly Dictionary<int, int> _soldStaysByListing;
        private readonly IReadOnlyCollection<TaxRule> _taxRules;
        private readonly IReadOnlyDictionary<int, StayWindow> _matched;
        private readonly StayWindow _default;
        private readonly PartySize _party;

        /// <summary>The viewer's Staylio Thân thiết level; a card is priced for who is reading it.</summary>
        public int LoyaltyLevel { get; }

        public StayPricer(
            DateOnly checkIn, DateOnly checkOut, PartySize party,
            IEnumerable<PriceRule> rules, IReadOnlyCollection<TaxRule> taxRules,
            Dictionary<int, int> soldStaysByListing,
            IReadOnlyDictionary<int, StayWindow>? matched = null, int loyaltyLevel = 0)
        {
            LoyaltyLevel = loyaltyLevel;
            _default = new StayWindow(checkIn, checkOut);
            _party = party;
            _taxRules = taxRules;
            _soldStaysByListing = soldStaysByListing;
            _matched = matched ?? new Dictionary<int, StayWindow>();
            _rulesByListing = rules.GroupBy(r => r.ListingId).ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// A flexible search matches different listings on different dates, so a
        /// card must be priced for the stay it was actually matched on.
        /// </summary>
        public StayWindow Window(Listing l) => _matched.TryGetValue(l.Id, out var w) ? w : _default;

        public decimal Total(Listing l)
        {
            var window = Window(l);
            return Pricing.Quote(new Pricing.Request
            {
                Listing = l,
                CheckIn = window.CheckIn,
                CheckOut = window.CheckOut,
                Party = _party,
                PriceRules = _rulesByListing.GetValueOrDefault(l.Id, []),
                TaxRules = _taxRules,
                ListingBookingCount = _soldStaysByListing.GetValueOrDefault(l.Id, 0),
                LoyaltyPercent = Loyalty.PercentFor(LoyaltyLevel, l.LoyaltyDiscountPercent)
            }).Total;
        }
    }

    /// <summary>Builds a pricer for the listings on one page; null dates mean no stay total.</summary>
    public async Task<StayPricer?> BuildPricerAsync(
        IReadOnlyCollection<Listing> listings, DateOnly? checkIn, DateOnly? checkOut, PartySize party,
        CancellationToken ct, IReadOnlyDictionary<int, StayWindow>? matched = null)
    {
        if (checkIn is null || checkOut is null || checkOut <= checkIn || listings.Count == 0) return null;

        var ids = listings.Select(l => l.Id).ToList();
        var mine = matched?.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        // Widen the rule window to cover every stay on the page, flexible or not.
        var ruleFrom = mine is { Count: > 0 } ? Min(checkIn.Value, mine.Values.Min(w => w.CheckIn)) : checkIn.Value;
        var ruleTo = mine is { Count: > 0 } ? Max(checkOut.Value, mine.Values.Max(w => w.CheckOut)) : checkOut.Value;

        var rules = await db.PriceRules
            .Where(r => ids.Contains(r.ListingId) && r.From <= ruleTo && ruleFrom <= r.To)
            .ToListAsync(ct);
        var taxRules = await ActiveTaxRulesAsync(ct);

        // The new-listing discount is part of the price, so a card that ignored it
        // would quote a different number to the room page (docs/00 §6.8).
        var soldStays = await db.Bookings
            .Where(b => ids.Contains(b.ListingId) && BookingLifecycle.BlocksDates.Contains(b.Status))
            .GroupBy(b => b.ListingId)
            .Select(g => new { ListingId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ListingId, x => x.Count, ct);

        return new StayPricer(checkIn.Value, checkOut.Value, party, rules, taxRules, soldStays, mine,
            await loyalty.ViewerLevelAsync(ct));
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    /// <summary>Every live tax rule; the set is tiny and shared by every quote on a page.</summary>
    public async Task<List<TaxRule>> ActiveTaxRulesAsync(CancellationToken ct) =>
        await db.TaxRules.Where(r => r.IsActive).OrderBy(r => r.SortOrder).ToListAsync(ct);

    public static ListingCardDto ToCard(Listing l, HashSet<int> favIds, StayPricer? pricer = null) => new(
        l.Id,
        l.Slug,
        l.Title,
        l.City,
        l.Country,
        CategoryKey(l.Type),
        CategoryLabel(l.Type),
        RoomTypeLabel(l.RoomType),
        l.Bedrooms,
        l.Beds,
        l.Bathrooms,
        l.MaxGuests,
        l.PricePerNight,
        l.DiscountPercent > 0
            ? Math.Round(l.PricePerNight * 100m / (100 - l.DiscountPercent), 0, MidpointRounding.AwayFromZero)
            : null,
        l.DiscountPercent,
        l.InstantBook,
        Math.Round(l.Rating, 2),
        l.ReviewCount,
        l.IsSuperhost,
        l.IsGuestFavorite,
        l.Latitude,
        l.Longitude,
        l.SpaceHighlight,
        l.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).ToList(),
        l.Amenities.Select(a => a.Amenity!.Key).ToList(),
        favIds.Contains(l.Id),
        l.CleaningFee,
        pricer?.Total(l),
        pricer?.Window(l).CheckIn,
        pricer?.Window(l).CheckOut,
        // docs/03 §4 — both come off the listing's own tier, never a constant.
        Cancellation.HasFreeCancellation(l.CancellationTier),
        Cancellation.Headline(l.CancellationTier))
    {
        FromCentreKm = Landmarks.FromCentreKm(l.City, l.Latitude, l.Longitude) is { } km ? Math.Round(km, 1) : null,
        FromCentreLabel = Landmarks.FromCentreKm(l.City, l.Latitude, l.Longitude) is { } d
            ? $"Cách trung tâm {Landmarks.DistanceLabel(d)}" : null,
        PayAtProperty = l.AcceptsPayAtProperty,
        LoyaltyOffer = l.LoyaltyDiscountPercent,
        HotelStars = l.HotelStars,
        LoyaltyPercent = Loyalty.PercentFor(pricer?.LoyaltyLevel ?? 0, l.LoyaltyDiscountPercent)
    };

    public async Task<HashSet<int>> HelpfulVotesAsync(string voterKey, List<int> reviewIds, CancellationToken ct) =>
        (await db.ReviewHelpfulVotes
            .Where(v => v.VoterKey == voterKey && reviewIds.Contains(v.ReviewId))
            .Select(v => v.ReviewId).ToListAsync(ct)).ToHashSet();

    /// <summary>Flips this reader's vote; the author cannot vote on their own review.</summary>
    public async Task<object?> ToggleHelpfulAsync(int reviewId, int userId, string voterKey, CancellationToken ct)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId && r.PublishedAt != null, ct);
        if (review is null || review.AuthorUserId == userId) return null;

        var existing = await db.ReviewHelpfulVotes
            .FirstOrDefaultAsync(v => v.ReviewId == reviewId && v.VoterKey == voterKey, ct);
        if (existing is null)
            db.ReviewHelpfulVotes.Add(new ReviewHelpfulVote { ReviewId = reviewId, VoterKey = voterKey });
        else
            db.ReviewHelpfulVotes.Remove(existing);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); } // a double click raced the unique index

        var count = await db.ReviewHelpfulVotes.CountAsync(v => v.ReviewId == reviewId, ct);
        var voted = await db.ReviewHelpfulVotes.AnyAsync(v => v.ReviewId == reviewId && v.VoterKey == voterKey, ct);
        return new { helpfulCount = count, votedHelpful = voted };
    }

    public async Task<ListingDetailDto?> GetDetailAsync(
        string idOrSlug, string sessionId, DateOnly? checkIn, DateOnly? checkOut, int guests,
        CancellationToken ct, int infants = 0, int pets = 0)
    {
        var query = db.Listings
            .Include(l => l.Images)
            .Include(l => l.Amenities).ThenInclude(la => la.Amenity)
            .Include(l => l.Reviews).ThenInclude(r => r.Booking!).ThenInclude(b => b.RoomType)
            .Include(l => l.Host)
            .Include(l => l.Guidebook)
            .AsSplitQuery();

        var listing = int.TryParse(idOrSlug, out var id)
            ? await query.FirstOrDefaultAsync(l => l.Id == id, ct)
            : await query.FirstOrDefaultAsync(l => l.Slug == idOrSlug, ct);

        if (listing is null) return null;

        var favIds = await FavoriteIdsAsync(sessionId, ct);

        var groups = listing.Amenities
            .Select(a => a.Amenity!)
            .OrderBy(a => a.SortOrder)
            .GroupBy(a => a.Group)
            .Select(g => new AmenityGroupDto(g.Key,
                g.Select(a => new AmenityDto(a.Key, a.Label, a.Icon, a.Group)).ToList()))
            .ToList();

        // docs/03 §7 — a review that has not been published yet is invisible to
        // everyone, including the person it is about.
        var reviewIds = listing.Reviews.Select(r => r.Id).ToList();
        var helpful = await db.ReviewHelpfulVotes
            .Where(v => reviewIds.Contains(v.ReviewId))
            .GroupBy(v => v.ReviewId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var reviews = listing.Reviews
            .Where(r => r.PublishedAt != null)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r =>
            {
                var type = r.Booking is { } bk
                    ? TravellerTypes.Of(bk.Adults, bk.Children, bk.Infants, bk.IsBusinessTrip)
                    : null;
                return new ReviewDto(
                    r.Id, r.AuthorName, r.AuthorInitials, r.AuthorLocation, r.When, r.Text,
                    Math.Round(r.Rating, 1), r.HostReply, r.HostRepliedAt, r.AuthorUserId,
                    // docs/01 TĐ-11 — what the language filter reads.
                    ReviewInsights.LanguageOf(r.Language, r.Text))
                {
                    TravellerType = type,
                    TravellerLabel = type is null ? null : TravellerTypes.Labels[type],
                    Nights = r.Booking?.Nights,
                    RoomTypeName = r.Booking?.RoomType?.Name,
                    HelpfulCount = helpful.GetValueOrDefault(r.Id)
                };
            })
            .ToList();

        // docs/01 TĐ-10 — the distribution, five stars first.
        var visible = listing.Reviews.Where(r => r.PublishedAt != null).ToList();
        var stars = Enumerable.Range(1, 5)
            .Select(n => visible.Count(r => (int)Math.Round(r.Rating) == n))
            .Reverse()
            .ToList();

        var rb = visible.Count == 0
            ? new RatingBreakdownDto(5, 5, 5, 5, 5, 5, stars)
            : new RatingBreakdownDto(
                Math.Round(visible.Average(r => r.Cleanliness), 1),
                Math.Round(visible.Average(r => r.Accuracy), 1),
                Math.Round(visible.Average(r => r.CheckIn), 1),
                Math.Round(visible.Average(r => r.Communication), 1),
                Math.Round(visible.Average(r => r.Location), 1),
                Math.Round(visible.Average(r => r.Value), 1),
                stars);

        // docs/01 TĐ-21 — what the reviews keep coming back to, worked out from
        // the reviews themselves rather than from anything the host wrote.
        var themes = ReviewInsights
            .Themes(visible.Select(r => (r.Text, r.Rating)))
            .Select(t => new ReviewThemeDto(t.Key, t.Label, t.Mentions, t.Rating))
            .ToList();

        // docs/01 TĐ-04 — show every filterable amenity, striking through the
        // ones this place does not have, so absence is visible rather than implied.
        var owned = listing.Amenities.Select(a => a.Amenity!.Key).ToHashSet();
        var allAmenities = await db.Amenities
            .Where(a => a.IsFilterable)
            .OrderBy(a => a.SortOrder)
            .Select(a => new { a.Key, a.Label, a.Icon, a.Group })
            .ToListAsync(ct);

        var amenityAvailability = allAmenities
            .Select(a => new AmenityAvailabilityDto(a.Key, a.Label, a.Icon, a.Group, owned.Contains(a.Key)))
            .ToList();

        var hostListings = await db.Listings.Where(l => l.HostId == listing.HostId)
            .Select(l => new { l.Rating, l.ReviewCount }).ToListAsync(ct);

        var host = listing.Host!;
        // docs/01 TĐ-14 — the languages come from the host's own profile
        // (docs/01 TK-04); a seeded host with no account simply lists none.
        var hostLanguages = host.UserId is { } hostUserId
            ? Profiles.UnpackLanguages(
                    await db.Users.Where(u => u.Id == hostUserId)
                        .Select(u => u.SpokenLanguages).FirstOrDefaultAsync(ct))
                .Select(Profiles.LanguageLabel).ToList()
            : [];

        // docs/01 QL-19 — co-hosts who accepted, for this listing or for everything.
        var coHosts = host.UserId is { } ownerId
            ? await db.CoHosts
                .Where(c => c.OwnerUserId == ownerId
                            && c.Status == CoHostStatus.Active
                            && (c.ListingId == null || c.ListingId == listing.Id))
                .Select(c => c.CoHostUser!.DisplayName ?? c.CoHostUser.FullName ?? c.Email)
                .ToListAsync(ct)
            : [];

        var hostDto = new HostDto(
            host.Id, host.Name, host.Initials, host.IsSuperhost, host.YearsHosting, host.Bio,
            host.ResponseRate, host.ResponseTime,
            $"Tham gia Staylio tháng {host.JoinedAt.Month}, {host.JoinedAt.Year}",
            hostListings.Count,
            Profiles.OverallRating(hostListings.Select(h => (h.Rating, h.ReviewCount))) ?? 5,
            hostListings.Sum(h => h.ReviewCount),
            host.UserId,
            host.AvatarUrl,
            hostLanguages,
            coHosts);

        var similar = await db.Listings
            .Include(l => l.Images)
            .Include(l => l.Amenities).ThenInclude(la => la.Amenity)
            .Where(l => l.Id != listing.Id && (l.City == listing.City || l.Type == listing.Type))
            .OrderByDescending(l => l.Rating)
            .Take(4)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var liveBookings = await db.Bookings
            .Where(b => b.ListingId == listing.Id && BookingLifecycle.BlocksDates.Contains(b.Status) && b.CheckOut >= today)
            .Select(b => new { b.CheckIn, b.CheckOut })
            .ToListAsync(ct);

        var hostBlocks = await db.CalendarBlocks
            .Where(b => b.ListingId == listing.Id && b.To >= today)
            .Select(b => new { From = b.From, To = b.To, Imported = b.FeedId != null })
            .ToListAsync(ct);

        var closedByHost = hostBlocks.Where(b => !b.Imported)
            .SelectMany(b => Enumerable
                .Range(0, Math.Max(0, b.To.DayNumber - b.From.DayNumber + 1))
                .Select(offset => b.From.AddDays(offset)))
            .ToList();

        // A stay blocks every night from check-in up to (but excluding) check-out;
        // a host block covers both of its endpoints.
        var unavailable = liveBookings
            .SelectMany(b => Enumerable
                .Range(0, Math.Max(0, b.CheckOut.DayNumber - b.CheckIn.DayNumber))
                .Select(offset => b.CheckIn.AddDays(offset)))
            .Concat(hostBlocks.SelectMany(b => Enumerable
                .Range(0, Math.Max(0, b.To.DayNumber - b.From.DayNumber + 1))
                .Select(offset => b.From.AddDays(offset))))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var pricer = await BuildPricerAsync(
            [listing, .. similar], checkIn, checkOut,
            PartySize.Of(Math.Max(1, guests)) with { Infants = infants, Pets = pets }, ct);

        return new ListingDetailDto(
            ToCard(listing, favIds, pricer),
            listing.Description,
            Cancellation.Summary(listing.CancellationTier),
            Split(listing.HouseRules),
            Split(listing.SafetyInfo),
            groups,
            amenityAvailability,
            BedLayout(listing),
            reviews,
            rb,
            hostDto,
            similar.Select(l => ToCard(l, favIds, pricer)).ToList(),
            unavailable,
            await RoomTypesAsync(listing, checkIn, checkOut, ct),
            CheckInGuide.WindowLabel(listing.CheckInFrom, listing.CheckInTo, listing.CheckOutBefore),
            // docs/01 TĐ-13 — how far the places a guest came for actually are.
            Landmarks.Near(listing.City, listing.Latitude, listing.Longitude)
                .Select(l => new LandmarkDto(l.Name, Landmarks.DistanceLabel(l.DistanceKm)))
                .ToList(),
            // docs/01 ĐG-12 — host cancellations still worth showing, newest first.
            await db.ListingCancellationNotes
                .Where(n => n.ListingId == listing.Id
                            && n.CreatedAt >= DateTime.UtcNow - Domain.CancellationNotes.Visibility)
                .OrderByDescending(n => n.CreatedAt)
                .Take(5)
                .Select(n => n.Note)
                .ToListAsync(ct),
            // docs/01 TĐ-22 — the host's own recommendations, grouped for reading.
            GuidebookGroups(listing),
            // docs/01 TĐ-23 — "Hiếm có", from the same calendar the picker greys out.
            RareFind(unavailable, closedByHost, today),
            themes)
        {
            ChildPolicy = Domain.ChildPolicy.Lines(listing)
        };
    }

    /// <summary>
    /// docs/01 TĐ-22 — the guidebook as the page reads it: display order, not
    /// enum order, and empty headings dropped rather than shown blank.
    /// </summary>
    private static List<GuidebookGroupDto>? GuidebookGroups(Listing listing)
    {
        if (listing.Guidebook.Count == 0) return null;

        var groups = Domain.Guidebooks.DisplayOrder
            .Select(c => new GuidebookGroupDto(
                c.ToString(),
                Domain.Guidebooks.Label(c),
                listing.Guidebook
                    .Where(p => p.Category == c)
                    .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
                    .Select(p => ToGuidebookDto(p, listing))
                    .ToList()))
            .Where(g => g.Places.Count > 0)
            .ToList();

        return groups.Count == 0 ? null : groups;
    }

    public static GuidebookPlaceDto ToGuidebookDto(GuidebookPlace p, Listing listing)
    {
        var km = Domain.Guidebooks.DistanceKm(
            listing.Latitude, listing.Longitude, p.Latitude, p.Longitude);

        return new GuidebookPlaceDto(
            p.Id, p.Category.ToString(), p.Name, p.Note, p.Address,
            // A pin only travels to the client whole; see Guidebooks.HasPin.
            Domain.Guidebooks.HasPin(p.Latitude, p.Longitude) ? p.Latitude : null,
            Domain.Guidebooks.HasPin(p.Latitude, p.Longitude) ? p.Longitude : null,
            km is { } d ? Landmarks.DistanceLabel(d) : null,
            p.SortOrder);
    }

    /// <summary>
    /// docs/01 TĐ-23 — count the free nights in the scarcity window off the same
    /// unavailable-date list the calendar renders, so the badge and the greyed
    /// days can never tell a guest two different stories.
    /// </summary>
    private static RareFindDto? RareFind(
        IReadOnlyCollection<DateOnly> unavailable, IReadOnlyCollection<DateOnly> closedByHost, DateOnly today)
    {
        var reading = Scarcity.ReadingOf(unavailable, closedByHost, today);

        return Scarcity.IsRareFind(reading)
            ? new RareFindDto(
                Scarcity.RareFindLabel, Scarcity.RareFindReason(reading),
                reading.FreeNights, reading.TotalNights)
            : null;
    }

    /// <summary>
    /// docs/01 MR-08 — the kinds of room a hotel has, with how many of each are
    /// still free on the busiest night of the dates the guest is looking at.
    /// </summary>
    private async Task<List<HotelRoomDto>?> RoomTypesAsync(
        Listing listing, DateOnly? checkIn, DateOnly? checkOut, CancellationToken ct)
    {
        if (!listing.IsHotel) return null;

        var rooms = await db.RoomTypes
            .Where(r => r.ListingId == listing.Id)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync(ct);
        if (rooms.Count == 0) return null;

        var from = checkIn ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var to = checkOut ?? from.AddDays(1);

        var taken = await db.Bookings
            .Where(b => b.ListingId == listing.Id && b.RoomTypeId != null
                        && BookingLifecycle.BlocksDates.Contains(b.Status)
                        && b.CheckIn < to && from < b.CheckOut)
            .Select(b => new { b.RoomTypeId, b.CheckIn, b.CheckOut })
            .ToListAsync(ct);

        return rooms.Select(r =>
        {
            var mine = taken.Where(t => t.RoomTypeId == r.Id).Select(t => (t.CheckIn, t.CheckOut)).ToList();
            var peak = HotelRules.PeakOccupancy(from, to, mine);

            return new HotelRoomDto(
                r.Id, r.Name, r.Summary, r.Inventory, Math.Max(0, r.Inventory - peak),
                r.MaxGuests, r.Beds, r.SizeSqm, r.PricePerNight, r.ImageUrl, r.FeatureList,
                r.NonRefundableDiscountPercent, r.BreakfastPricePerGuest);
        }).ToList();
    }

    /// <summary>
    /// docs/01 TĐ-05. Hosts who have filled in a layout get theirs; the rest
    /// fall back to spreading the bed count evenly across the bedrooms.
    /// </summary>
    private static List<BedroomDto> BedLayout(Listing listing)
    {
        try
        {
            var stored = System.Text.Json.JsonSerializer.Deserialize<List<BedroomDto>>(
                listing.BedLayoutJson, LayoutJson);
            if (stored is { Count: > 0 }) return stored;
        }
        catch (System.Text.Json.JsonException)
        {
            // A malformed layout is not worth failing the page over.
        }

        var rooms = Math.Max(1, listing.Bedrooms);
        var perRoom = Math.Max(1, listing.Beds / rooms);
        var leftover = Math.Max(0, listing.Beds - perRoom * rooms);

        return Enumerable.Range(0, rooms).Select(i =>
        {
            var beds = perRoom + (i == 0 ? leftover : 0);
            return new BedroomDto($"Phòng ngủ {i + 1}",
                Enumerable.Repeat("Giường đôi", Math.Max(1, beds)).ToList());
        }).ToList();
    }

    private static readonly System.Text.Json.JsonSerializerOptions LayoutJson =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private static string[] Split(string s) =>
        s.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Builds the priced request for a stay. Booking and quoting share this so a
    /// guest is never charged something other than what they were shown.
    /// </summary>
    /// <param name="excludeBookingId">
    /// The booking being priced. Creating a hold already counts as a sold stay,
    /// so without this a booking pushes the listing past the new-listing
    /// threshold and then fails its own price check at payment.
    /// </param>
    public async Task<Pricing.Request?> BuildQuoteRequestAsync(
        int listingId, DateOnly checkIn, DateOnly checkOut, PartySize party, CancellationToken ct,
        int? excludeBookingId = null, int? roomTypeId = null, decimal? nightlyOverride = null,
        RatePlan? plan = null, int loyaltyPercent = 0)
    {
        var l = await db.Listings.FirstOrDefaultAsync(x => x.Id == listingId, ct);
        if (l is null) return null;

        // docs/01 ĐP-17 — a host's private offer sets the nightly rate directly,
        // which wins over the room's own price: the offer is the price now. The
        // discount is the host's, so it flows through the subtotal and the payout
        // shrinks with it rather than landing on the platform.
        var roomRate = nightlyOverride
            ?? (roomTypeId is { } id
                ? await db.RoomTypes.Where(r => r.Id == id && r.ListingId == listingId)
                    .Select(r => (decimal?)r.PricePerNight).FirstOrDefaultAsync(ct)
                : null);

        var rules = await db.PriceRules
            .Where(r => r.ListingId == listingId && r.From <= checkOut && checkIn <= r.To)
            .ToListAsync(ct);

        // The new-listing discount only looks at stays that actually went ahead.
        var soldStays = await db.Bookings
            .CountAsync(b => b.ListingId == listingId
                             && b.Id != (excludeBookingId ?? 0)
                             && BookingLifecycle.BlocksDates.Contains(b.Status), ct);

        return new Pricing.Request
        {
            Listing = l,
            CheckIn = checkIn,
            CheckOut = checkOut,
            Party = party,
            PriceRules = rules,
            TaxRules = await ActiveTaxRulesAsync(ct),
            ListingBookingCount = soldStays,
            NightlyRateOverride = roomRate,
            Plan = plan ?? RatePlan.None,
            LoyaltyPercent = loyaltyPercent
        };
    }

    /// <summary>The plan a guest asked for, checked against what the room sells.</summary>
    public async Task<(RatePlan? Plan, string? Error)> ResolvePlanAsync(
        int listingId, int? roomTypeId, bool nonRefundable, bool breakfast, CancellationToken ct)
    {
        if (!nonRefundable && !breakfast) return (RatePlan.None, null);
        var room = roomTypeId is { } id
            ? await db.RoomTypes.FirstOrDefaultAsync(r => r.Id == id && r.ListingId == listingId, ct)
            : null;
        return RatePlan.Resolve(room, nonRefundable, breakfast);
    }

    public async Task<QuoteDto?> QuoteAsync(
        int listingId, DateOnly checkIn, DateOnly checkOut, PartySize party, CancellationToken ct,
        int? roomTypeId = null, decimal couponAmount = 0, string? couponLabel = null, string? couponError = null,
        RatePlan? plan = null)
    {
        var request = await BuildQuoteRequestAsync(
            listingId, checkIn, checkOut, party, ct, roomTypeId: roomTypeId, plan: plan);
        if (request is null) return null;

        // The guest asking is the guest who will book: price it for their level.
        request = request with
        {
            LoyaltyPercent = Loyalty.PercentFor(await loyalty.ViewerLevelAsync(ct), request.Listing.LoyaltyDiscountPercent)
        };

        if (couponAmount > 0)
            request = request with { CouponAmount = couponAmount, CouponLabel = couponLabel ?? "Mã giảm giá" };

        var l = request.Listing;
        var b = Pricing.Quote(request);

        return new QuoteDto(
            l.Id, b.Nights, party.Counted, b.NightlyRate,
            b.RoomBeforeDiscount, b.RoomDiscount, b.DiscountPercent,
            b.ExtraGuestFee, b.PetFee, b.CleaningFee,
            b.Subtotal, b.GuestServiceFee, b.Tax, b.Total,
            b.HostServiceFee, b.HostPayout,
            b.Lines.Select(x => new PriceLineDto(x.Key, x.Label, x.Amount)).ToList(),
            party.Counted > l.MaxGuests, l.MaxGuests,
            l.MinNights, b.Nights < l.MinNights,
            Cancellation.Label(request.Plan.TierFor(l.CancellationTier)),
            Cancellation.Summary(request.Plan.TierFor(l.CancellationTier)),
            CouponApplied: b.Coupon > 0,
            CouponDiscount: b.Coupon,
            CouponError: couponError)
        {
            BreakfastFee = b.BreakfastFee,
            NonRefundableRate = request.Plan.NonRefundableDiscountPercent > 0
        };
    }
}
