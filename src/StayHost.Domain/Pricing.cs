namespace StayHost.Domain;

/// <summary>
/// The eleven steps of docs/03 §1, in order, and nothing else. Search results,
/// the room page and checkout all call <see cref="Quote"/>, which is what makes
/// them agree on the number (docs/00 §6.8).
/// </summary>
public static class Pricing
{
    /// <summary>
    /// Everything a stay needs to be priced. Only <c>Listing</c>, <c>CheckIn</c>
    /// and <c>CheckOut</c> are required; the rest default to "nothing applies".
    /// </summary>
    public sealed record Request
    {
        public required Listing Listing { get; init; }
        public required DateOnly CheckIn { get; init; }
        public required DateOnly CheckOut { get; init; }
        public PartySize Party { get; init; } = new(1);

        public IReadOnlyCollection<PriceRule> PriceRules { get; init; } = [];
        public IReadOnlyCollection<TaxRule> TaxRules { get; init; } = [];

        /// <summary>When the guest is booking — drives early-bird and last-minute.</summary>
        public DateOnly BookedOn { get; init; } = DateOnly.FromDateTime(DateTime.UtcNow);

        /// <summary>Stays already sold for this listing, for the new-listing discount.</summary>
        public int ListingBookingCount { get; init; } = int.MaxValue;

        /// <summary>The guest's own balance applied last, on its own line (step 9).</summary>
        public decimal PromotionAmount { get; init; }
        public string PromotionLabel { get; init; } = "Số dư Staylio";

        /// <summary>
        /// docs/01 ĐP-09, TC-09 — a promo code's discount. Its own step-9 line,
        /// separate from the guest's balance (docs/03 §1 step 9, docs/07 §3): the
        /// two are different money and a receipt shows them apart. Applied before
        /// the balance so a code spares the balance rather than stacking under it.
        /// </summary>
        public decimal CouponAmount { get; init; }
        public string CouponLabel { get; init; } = "Mã giảm giá";

        /// <summary>
        /// docs/01 MR-09 — the nightly rate of the room type the guest picked.
        /// A hotel sells rooms at different prices under one listing; the
        /// listing's own price is the cheapest of them, which is what search
        /// shows. Null for an ordinary place.
        /// </summary>
        public decimal? NightlyRateOverride { get; init; }

        /// <summary>Hotel rooms of the same kind booked together. Everything nightly scales with them.</summary>
        public int Rooms { get; init; } = 1;

        /// <summary>A hotel room's non-refundable rate and breakfast, already resolved.</summary>
        public RatePlan Plan { get; init; } = RatePlan.None;

        /// <summary>The "Staylio Thân thiết" discount this guest gets here, already resolved (Loyalty.PercentFor).</summary>
        public int LoyaltyPercent { get; init; }

        public PricingSettings Settings { get; init; } = PricingSettings.Current;
    }

    /// <summary>
    /// A priced stay. <see cref="Lines"/> is exactly what the UI renders, and
    /// <see cref="Total"/> is the sum of those lines — no separate rounding pass.
    /// </summary>
    public sealed record Breakdown
    {
        public required int Nights { get; init; }
        /// <summary>Average nightly rate of one room before discounts, for "x ₫ × n đêm".</summary>
        public required decimal NightlyRate { get; init; }
        public required decimal RoomBeforeDiscount { get; init; }
        public required decimal RoomDiscount { get; init; }
        public required int DiscountPercent { get; init; }
        public required IReadOnlyList<DiscountPart> DiscountParts { get; init; }

        public required decimal ExtraGuestFee { get; init; }
        public required decimal PetFee { get; init; }
        public required decimal CleaningFee { get; init; }
        public decimal BreakfastFee { get; init; }

        /// <summary>Step 6 — what both service fees are calculated from.</summary>
        public required decimal Subtotal { get; init; }
        public required decimal GuestServiceFee { get; init; }
        public required decimal Tax { get; init; }
        public required IReadOnlyList<PriceLine> TaxLines { get; init; }
        public required decimal Coupon { get; init; }
        public required decimal Promotion { get; init; }
        public required decimal Total { get; init; }

        public required decimal HostServiceFee { get; init; }
        public required decimal HostPayout { get; init; }

        public required IReadOnlyList<PriceLine> Lines { get; init; }
        public required IReadOnlyList<NightRate> Nightly { get; init; }

        /// <summary>Room charges after discount — the tax base that excludes cleaning.</summary>
        public decimal RoomAfterDiscount => RoomBeforeDiscount - RoomDiscount;
    }

    public readonly record struct DiscountPart(string Key, string Label, int Percent);

    public readonly record struct NightRate(DateOnly Night, decimal Rate, string Source);

    /// <summary>Round every displayed line, never the total (docs/03 §1, rounding rule).</summary>
    private static decimal Round(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);

    /* --------------------------------------------------------------- step 1 */

    /// <summary>
    /// Rate for one night, taking the first tier that applies:
    /// host's price for that exact day → seasonal rule → weekend rate → base.
    /// The weekend uplift is its own tier, not an addition on top of a season.
    /// </summary>
    public static NightRate RateFor(
        Listing listing, DateOnly night, IReadOnlyCollection<PriceRule> rules, decimal? rateOverride = null)
    {
        // A chosen room type replaces the base rate, but seasons and day
        // overrides still win over it — a hotel raises prices at Tết too.
        var basePrice = rateOverride ?? listing.PricePerNight;

        var day = rules.FirstOrDefault(r => r.Kind == PriceRuleKind.DayOverride && r.From <= night && night <= r.To);
        if (day is not null) return new(night, day.NightlyRate, "day");

        var season = rules.FirstOrDefault(r => r.Kind == PriceRuleKind.Season && r.From <= night && night <= r.To);
        if (season is not null) return new(night, season.NightlyRate, "season");

        if (night.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday && listing.WeekendSurchargeRate > 0)
            return new(night, Round(basePrice * (1 + listing.WeekendSurchargeRate)), "weekend");

        return new(night, basePrice, "base");
    }

    /* ------------------------------------------------------------ steps 2–4 */

    /// <summary>
    /// The discount percentages that apply, already de-duplicated by the
    /// "pick one" rules and capped at the platform maximum. Percentages add,
    /// they do not compound: 10% + 15% is 25%, not 23.5%.
    /// </summary>
    private static (int Percent, List<DiscountPart> Parts) DiscountsFor(Request req, int nights)
    {
        var l = req.Listing;
        var parts = new List<DiscountPart>();

        // Step 2 — length of stay. One tier only; the longer one wins.
        if (nights >= 28 && l.MonthlyDiscountPercent > 0)
            parts.Add(new("length-month", $"Giảm giá ở theo tháng ({l.MonthlyDiscountPercent}%)", l.MonthlyDiscountPercent));
        else if (nights >= 7 && l.WeeklyDiscountPercent > 0)
            parts.Add(new("length-week", $"Giảm giá ở theo tuần ({l.WeeklyDiscountPercent}%)", l.WeeklyDiscountPercent));

        // Step 3 — how far ahead they booked. One only; the bigger one wins.
        var leadDays = req.CheckIn.DayNumber - req.BookedOn.DayNumber;
        var early = leadDays >= l.EarlyBirdDays && l.EarlyBirdDays > 0 ? l.EarlyBirdPercent : 0;
        var lastMinute = leadDays <= l.LastMinuteDays && l.LastMinuteDays > 0 ? l.LastMinutePercent : 0;

        if (early > 0 || lastMinute > 0)
        {
            parts.Add(early >= lastMinute
                ? new DiscountPart("early-bird", $"Giảm giá đặt sớm ({early}%)", early)
                : new DiscountPart("last-minute", $"Giảm giá phút chót ({lastMinute}%)", lastMinute));
        }

        // The non-refundable rate is a discount on the room like the others,
        // and so counts toward the same cap.
        if (req.Plan.NonRefundableDiscountPercent > 0)
        {
            parts.Add(new("non-refundable",
                $"Giá không hoàn tiền ({req.Plan.NonRefundableDiscountPercent}%)",
                req.Plan.NonRefundableDiscountPercent));
        }

        // The host's discount for returning guests — theirs to give, so it sits
        // with the other room discounts under the same cap.
        if (req.LoyaltyPercent > 0)
        {
            parts.Add(new("loyalty",
                $"Ưu đãi khách thân thiết ({req.LoyaltyPercent}%)", req.LoyaltyPercent));
        }

        // Step 4 — a brand-new listing's first few stays.
        if (req.ListingBookingCount < req.Settings.NewListingBookingCount)
        {
            parts.Add(new("new-listing",
                $"Ưu đãi tin mới ({req.Settings.NewListingDiscountPercent}%)",
                req.Settings.NewListingDiscountPercent));
        }

        var total = parts.Sum(p => p.Percent);
        if (total <= req.Settings.MaxDiscountPercent) return (total, parts);

        // Over the cap: keep the parts for display but state the capped figure.
        parts.Add(new("cap", $"Giới hạn tổng giảm còn {req.Settings.MaxDiscountPercent}%", 0));
        return (req.Settings.MaxDiscountPercent, parts);
    }

    /* ------------------------------------------------------------- the quote */

    public static Breakdown Quote(Request req)
    {
        var l = req.Listing;
        var s = req.Settings;
        var nights = Math.Max(1, req.CheckOut.DayNumber - req.CheckIn.DayNumber);

        // Step 1 — room charge, night by night.
        var nightly = new List<NightRate>(nights);
        for (var i = 0; i < nights; i++)
            nightly.Add(RateFor(l, req.CheckIn.AddDays(i), req.PriceRules, req.NightlyRateOverride));
        var rooms = Math.Max(1, req.Rooms);
        var roomBeforeDiscount = nightly.Sum(n => n.Rate) * rooms;

        // Steps 2–4 — discounts, applied to the room charge only.
        var (discountPercent, discountParts) = DiscountsFor(req, nights);
        var roomDiscount = Round(roomBeforeDiscount * discountPercent / 100m);
        var roomAfterDiscount = roomBeforeDiscount - roomDiscount;

        // Step 5 — surcharges. Infants are free; the cleaning fee is once per stay.
        // Each room includes its own guests; only the ones beyond all of them pay extra.
        var extraGuests = Math.Max(0, req.Party.Counted - l.FreeGuestThreshold * rooms);
        var extraGuestFee = Round(extraGuests * l.ExtraGuestFee * nights);
        var petFee = req.Party.Pets > 0
            ? Round(l.PetFeePerNight ? l.PetFee * nights : l.PetFee)
            : 0m;
        var cleaningFee = Round(l.CleaningFee);
        // Breakfast is for everyone counted, like the extra-guest fee; infants eat free.
        var breakfastGuests = Math.Max(1, req.Party.Counted);
        var breakfastFee = Round(req.Plan.BreakfastPerGuestPerNight * breakfastGuests * nights);

        // Step 6.
        var subtotal = roomAfterDiscount + extraGuestFee + petFee + cleaningFee + breakfastFee;

        // Step 7 — guest service fee, before tax.
        var guestServiceFee = Round(subtotal * s.GuestServiceFeeRate);

        // Step 8 — regional taxes, which may stack.
        var taxLines = TaxLinesFor(req, nights, roomAfterDiscount, subtotal, guestServiceFee);
        var tax = taxLines.Sum(t => t.Amount);

        // Step 9 — reductions, never below zero overall. A promo code goes first,
        // then the guest's balance covers what is left: taking the code off first
        // means a code spends none of the balance the guest is keeping.
        var gross = subtotal + guestServiceFee + tax;
        var coupon = Math.Min(Round(req.CouponAmount), gross);
        var promotion = Math.Min(Round(req.PromotionAmount), gross - coupon);

        // Step 10.
        var total = gross - coupon - promotion;

        // Step 11 — the host's side of the same subtotal.
        var hostServiceFee = Round(subtotal * s.HostServiceFeeRate);
        var hostPayout = subtotal - hostServiceFee;

        var lines = new List<PriceLine>
        {
            new("room", rooms == 1
                ? $"{FormatVnd(roomBeforeDiscount / nights)} × {nights} đêm"
                : $"{FormatVnd(roomBeforeDiscount / nights / rooms)} × {rooms} phòng × {nights} đêm",
                roomBeforeDiscount)
        };

        // The named parts explain the percentage; one row carries the money.
        if (roomDiscount > 0)
        {
            var named = discountParts.Where(p => p.Percent > 0).Select(p => p.Label).ToList();
            lines.Add(new PriceLine("discount",
                named.Count == 1 ? named[0] : $"Giảm giá {discountPercent}% ({string.Join(" + ", named)})",
                -roomDiscount));
        }

        if (extraGuestFee > 0) lines.Add(new("extra-guests", $"Phụ thu {extraGuests} khách thêm × {nights} đêm", extraGuestFee));
        if (petFee > 0) lines.Add(new("pet", l.PetFeePerNight ? $"Phí thú cưng × {nights} đêm" : "Phí thú cưng", petFee));
        if (breakfastFee > 0)
            lines.Add(new("breakfast", $"Bữa sáng × {breakfastGuests} khách × {nights} đêm", breakfastFee));
        if (cleaningFee > 0) lines.Add(new("cleaning", "Phí dọn dẹp", cleaningFee));

        lines.Add(new("guest-service-fee", "Phí dịch vụ Staylio", guestServiceFee));
        lines.AddRange(taxLines);
        if (coupon > 0) lines.Add(new("coupon", req.CouponLabel, -coupon));
        if (promotion > 0) lines.Add(new("promotion", req.PromotionLabel, -promotion));

        return new Breakdown
        {
            Nights = nights,
            NightlyRate = Round(roomBeforeDiscount / nights / rooms),
            RoomBeforeDiscount = roomBeforeDiscount,
            RoomDiscount = roomDiscount,
            DiscountPercent = discountPercent,
            DiscountParts = discountParts,
            ExtraGuestFee = extraGuestFee,
            PetFee = petFee,
            CleaningFee = cleaningFee,
            BreakfastFee = breakfastFee,
            Subtotal = subtotal,
            GuestServiceFee = guestServiceFee,
            Tax = tax,
            TaxLines = taxLines,
            Coupon = coupon,
            Promotion = promotion,
            Total = total,
            HostServiceFee = hostServiceFee,
            HostPayout = hostPayout,
            Lines = lines,
            Nightly = nightly
        };
    }

    /// <summary>
    /// Step 8. Rules for the exact city come first, then country-wide ones; a
    /// region with no configured rule is simply untaxed.
    /// </summary>
    private static List<PriceLine> TaxLinesFor(
        Request req, int nights, decimal roomAfterDiscount, decimal subtotal, decimal guestServiceFee)
    {
        var applicable = req.TaxRules
            .Where(r => r.AppliesOn(req.CheckIn))
            .Where(r => string.Equals(r.Country, req.Listing.Country, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.City is null || string.Equals(r.City, req.Listing.City, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Id)
            .ToList();

        var lines = new List<PriceLine>();
        foreach (var rule in applicable)
        {
            var amount = rule.Method switch
            {
                TaxMethod.Percentage => Round(rule.Base switch
                {
                    TaxBase.RoomOnly => roomAfterDiscount,
                    TaxBase.Subtotal => subtotal,
                    _ => subtotal + guestServiceFee
                } * rule.Value),
                TaxMethod.PerNight => Round(rule.Value * nights),
                TaxMethod.PerGuestPerNight => Round(rule.Value * Math.Max(1, req.Party.Counted) * nights),
                _ => Round(rule.Value)
            };

            if (amount > 0) lines.Add(new PriceLine($"tax-{rule.Id}", rule.Name, amount));
        }
        return lines;
    }

    private static string FormatVnd(decimal amount) =>
        Vnd.Format(amount);

    /* --------------------------------------------------------- experiences */

    /// <summary>
    /// What a seat at a session costs, all in. docs/00 §6.8 says money is
    /// defined once, so an experience uses the same fee rates and the same tax
    /// rules as a stay — only the thing being counted changes.
    /// </summary>
    public sealed record ExperienceRequest
    {
        public required Experience Experience { get; init; }
        public required int Seats { get; init; }
        public bool Private { get; init; }
        public required DateTime StartsAt { get; init; }
        public IReadOnlyCollection<TaxRule> TaxRules { get; init; } = [];
    }

    public sealed record ExperienceBreakdown
    {
        public required int Seats { get; init; }
        public required decimal PerSeat { get; init; }
        public required decimal Subtotal { get; init; }
        public required decimal GuestServiceFee { get; init; }
        public required decimal Tax { get; init; }
        public required IReadOnlyList<PriceLine> TaxLines { get; init; }
        public required decimal Total { get; init; }
        public required decimal HostServiceFee { get; init; }
        public required decimal HostPayout { get; init; }
        public required IReadOnlyList<PriceLine> Lines { get; init; }
    }

    public static ExperienceBreakdown QuoteExperience(ExperienceRequest req)
    {
        var settings = PricingSettings.Current;
        var seats = Math.Max(1, req.Seats);

        // A private session is one price for the room, not a price each.
        var subtotal = req.Private
            ? Round(req.Experience.PrivateGroupPrice ?? req.Experience.PricePerPerson * seats)
            : Round(req.Experience.PricePerPerson * seats);

        var guestServiceFee = Round(subtotal * settings.GuestServiceFeeRate);
        var taxLines = ExperienceTaxLines(req, subtotal, guestServiceFee);
        var tax = taxLines.Sum(l => l.Amount);
        var total = subtotal + guestServiceFee + tax;

        var hostServiceFee = Round(subtotal * settings.HostServiceFeeRate);
        var hostPayout = subtotal - hostServiceFee;

        var lines = new List<PriceLine>
        {
            new("subtotal",
                req.Private ? "Thuê trọn nhóm riêng" : $"{FormatVnd(req.Experience.PricePerPerson)} × {seats} người",
                subtotal),
            new("service-fee", "Phí dịch vụ Staylio", guestServiceFee)
        };
        lines.AddRange(taxLines);

        return new ExperienceBreakdown
        {
            Seats = seats,
            PerSeat = req.Private ? subtotal : req.Experience.PricePerPerson,
            Subtotal = subtotal,
            GuestServiceFee = guestServiceFee,
            Tax = tax,
            TaxLines = taxLines,
            Total = total,
            HostServiceFee = hostServiceFee,
            HostPayout = hostPayout,
            Lines = lines.Where(l => l.Amount > 0).ToList()
        };
    }

    /// <summary>
    /// A session has no nights, so per-night and per-guest-per-night levies do
    /// not apply to it; percentage and flat ones do.
    /// </summary>
    private static List<PriceLine> ExperienceTaxLines(
        ExperienceRequest req, decimal subtotal, decimal guestServiceFee)
    {
        var on = DateOnly.FromDateTime(req.StartsAt);

        var applicable = req.TaxRules
            .Where(r => r.AppliesOn(on))
            .Where(r => string.Equals(r.Country, req.Experience.Country, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.City is null || string.Equals(r.City, req.Experience.City, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id);

        var lines = new List<PriceLine>();
        foreach (var rule in applicable)
        {
            var amount = rule.Method switch
            {
                TaxMethod.Percentage => Round(
                    (rule.Base == TaxBase.SubtotalPlusGuestFee ? subtotal + guestServiceFee : subtotal) * rule.Value),
                TaxMethod.PerStay => Round(rule.Value),
                _ => 0m
            };

            if (amount > 0) lines.Add(new PriceLine($"tax-{rule.Id}", rule.Name, amount));
        }
        return lines;
    }

    /* ------------------------------------------------------------ services */

    public sealed record ServiceRequest
    {
        public required ServiceOffering Offering { get; init; }
        public required int Quantity { get; init; }
        public required DateTime StartsAt { get; init; }
        public IReadOnlyCollection<TaxRule> TaxRules { get; init; } = [];

        /// <summary>docs/09 §3.3 (MR-S-03) — the extras chosen, priced separately.</summary>
        public IReadOnlyCollection<ServiceAddOn> AddOns { get; init; } = [];

        /// <summary>docs/09 §3.3 (MR-S-04) — how far past the free radius, if at all.</summary>
        public double DistanceKm { get; init; }
    }

    public sealed record ServiceBreakdown
    {
        public required int Quantity { get; init; }
        public required decimal Subtotal { get; init; }
        /// <summary>docs/09 §3.3 — what the extras and the journey came to.</summary>
        public decimal AddOnsTotal { get; init; }
        public decimal TravelFee { get; init; }
        public required decimal GuestServiceFee { get; init; }
        public required decimal Tax { get; init; }
        public required decimal Total { get; init; }
        /// <summary>
        /// What the platform keeps. A partner job (docs/01 MR-07) pays a
        /// commission instead of the host service fee, never both.
        /// </summary>
        public required decimal PlatformCut { get; init; }
        public required decimal ProviderPayout { get; init; }
        public required IReadOnlyList<PriceLine> Lines { get; init; }
    }

    public static ServiceBreakdown QuoteService(ServiceRequest req)
    {
        var settings = PricingSettings.Current;
        var o = req.Offering;
        var quantity = Math.Max(1, req.Quantity);

        // A flat-rate visit is one price no matter how many hours or people are
        // named; everything else multiplies.
        var basePrice = Round(o.Pricing == ServicePricing.PerSession
            ? o.BasePrice
            : o.BasePrice * quantity);

        // docs/09 §3.3 — extras and the journey are part of what is being sold,
        // so they sit in the subtotal: the platform's cut and the provider's
        // share both follow them rather than being computed off the base alone.
        var addOns = Round(req.AddOns.Where(a => a.IsActive).Sum(a => a.Price));
        var travelFee = ServiceRules.TravelFee(o, req.DistanceKm);
        var subtotal = basePrice + addOns + travelFee;

        // docs/09 §3.3 — services carry no guest service fee; the platform's cut
        // comes off the provider (15%, or the partner's negotiated commission).
        var guestServiceFee = Round(subtotal * settings.ServiceGuestFeeRate);
        var taxLines = ServiceTaxLines(req, subtotal, guestServiceFee);
        var tax = taxLines.Sum(l => l.Amount);
        var total = subtotal + guestServiceFee + tax;

        var platformCut = Round(subtotal * (o.IsPartner ? o.CommissionRate : settings.ServiceProviderFeeRate));
        var providerPayout = subtotal - platformCut;

        var lines = new List<PriceLine>
        {
            new("subtotal",
                o.Pricing == ServicePricing.PerSession
                    ? "Trọn buổi"
                    : $"{FormatVnd(o.BasePrice)} × {quantity} {ServiceRules.UnitLabel(o.Pricing)}",
                basePrice)
        };

        // Each extra is its own line: a guest who added a set menu should see the
        // set menu, not a subtotal that quietly grew.
        foreach (var a in req.AddOns.Where(a => a.IsActive && a.Price > 0))
            lines.Add(new($"add-on-{a.Id}", a.Name, a.Price));

        if (travelFee > 0)
            lines.Add(new("travel-fee",
                $"Phí di chuyển ngoài {o.ServiceRadiusKm} km", travelFee));

        lines.Add(new("service-fee", "Phí dịch vụ Staylio", guestServiceFee));
        lines.AddRange(taxLines);

        return new ServiceBreakdown
        {
            Quantity = quantity,
            Subtotal = subtotal,
            AddOnsTotal = addOns,
            TravelFee = travelFee,
            GuestServiceFee = guestServiceFee,
            Tax = tax,
            Total = total,
            PlatformCut = platformCut,
            ProviderPayout = providerPayout,
            Lines = lines.Where(l => l.Amount > 0).ToList()
        };
    }

    /// <summary>A service has no nights either, so only percentage and flat levies apply.</summary>
    private static List<PriceLine> ServiceTaxLines(
        ServiceRequest req, decimal subtotal, decimal guestServiceFee)
    {
        var on = DateOnly.FromDateTime(req.StartsAt);

        var applicable = req.TaxRules
            .Where(r => r.AppliesOn(on))
            .Where(r => string.Equals(r.Country, req.Offering.Country, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.City is null || string.Equals(r.City, req.Offering.City, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id);

        var lines = new List<PriceLine>();
        foreach (var rule in applicable)
        {
            var amount = rule.Method switch
            {
                TaxMethod.Percentage => Round(
                    (rule.Base == TaxBase.SubtotalPlusGuestFee ? subtotal + guestServiceFee : subtotal) * rule.Value),
                TaxMethod.PerStay => Round(rule.Value),
                _ => 0m
            };

            if (amount > 0) lines.Add(new PriceLine($"tax-{rule.Id}", rule.Name, amount));
        }
        return lines;
    }

    /* ------------------------------------------------ convenience overloads */

    /// <summary>Shorthand used by search cards, which only know dates and a head count.</summary>
    public static Breakdown Quote(
        Listing listing, DateOnly checkIn, DateOnly checkOut,
        IReadOnlyCollection<PriceRule>? rules = null,
        IReadOnlyCollection<TaxRule>? taxRules = null,
        int guests = 1) =>
        Quote(new Request
        {
            Listing = listing,
            CheckIn = checkIn,
            CheckOut = checkOut,
            Party = PartySize.Of(guests),
            PriceRules = rules ?? [],
            TaxRules = taxRules ?? []
        });
}
