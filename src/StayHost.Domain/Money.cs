namespace StayHost.Domain;

/// <summary>
/// Platform-wide money knobs. docs/00 §6.8 says every money rule is defined once;
/// these are the numbers that rule depends on, kept in one settable place so the
/// admin console (QT-06) can change them without a deploy.
/// </summary>
public sealed record PricingSettings
{
    /// <summary>docs/03 §1 step 7 — charged to the guest on the subtotal, before tax.</summary>
    public decimal GuestServiceFeeRate { get; init; } = 0.14m;

    /// <summary>docs/03 §1 step 11 — withheld from the host's share of the subtotal.</summary>
    public decimal HostServiceFeeRate { get; init; } = 0.03m;

    /// <summary>
    /// docs/09 §3.3 (DV-E/DV-F, locked 10/08/2026) — services price on their own
    /// fees, not the stay rates: the guest pays no separate service fee and the
    /// platform withholds 15% from the provider. Experiences deliberately keep
    /// the stay rates above (the customer chose 14%/3% over the §2.6 0%/20%).
    /// </summary>
    public decimal ServiceGuestFeeRate { get; init; } = 0.00m;
    public decimal ServiceProviderFeeRate { get; init; } = 0.15m;

    /// <summary>docs/03 §1 — every discount added together may not exceed this.</summary>
    public int MaxDiscountPercent { get; init; } = 60;

    /// <summary>docs/03 §1 step 4 — a listing's first N stays carry the new-listing discount.</summary>
    public int NewListingBookingCount { get; init; } = 3;
    public int NewListingDiscountPercent { get; init; } = 20;

    /// <summary>Fallback cleaning fee used when a host leaves the field empty.</summary>
    public decimal DefaultCleaningFee { get; init; } = 350_000m;

    /// <summary>Replaced at startup from configuration; the defaults match the spec.</summary>
    public static PricingSettings Current { get; set; } = new();
}

/// <summary>
/// Who is staying. docs/03 §1 step 5 and §2 rule 2 both hinge on the fact that
/// infants count for neither capacity nor the extra-guest surcharge.
/// </summary>
public readonly record struct PartySize(int Adults, int Children = 0, int Infants = 0, int Pets = 0)
{
    /// <summary>Adults plus children — the number that capacity and surcharges use.</summary>
    public int Counted => Adults + Children;

    public static PartySize Of(int guests) => new(Math.Max(1, guests));
}

/// <summary>One displayed money row. Amounts are already rounded; negatives are reductions.</summary>
public sealed record PriceLine(string Key, string Label, decimal Amount);

/// <summary>
/// The one way money is written for a Vietnamese reader: a dot every three
/// digits and the đồng sign. Invariant formatting puts commas there, which is
/// wrong in this language and looks like a bug to anyone reading it.
/// </summary>
public static class Vnd
{
    /// <summary>
    /// Spelled out rather than taken from the "vi-VN" culture: the Alpine image
    /// ships English-only ICU data, where "vi-VN" quietly falls back to commas —
    /// so every price line on production read "980,000₫" while every developer
    /// machine showed "980.000₫".
    /// </summary>
    private static readonly System.Globalization.NumberFormatInfo Dots = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NegativeSign = "-"
    };

    public static string Format(decimal amount) =>
        Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("#,##0", Dots) + "₫";
}

public enum TaxMethod
{
    /// <summary>A percentage of the configured base.</summary>
    Percentage = 0,
    /// <summary>A flat amount for each night of the stay.</summary>
    PerNight = 1,
    /// <summary>A flat amount for each counted guest, each night.</summary>
    PerGuestPerNight = 2,
    /// <summary>A flat amount charged once for the whole stay.</summary>
    PerStay = 3
}

/// <summary>What a percentage tax is calculated on.</summary>
public enum TaxBase
{
    /// <summary>Room after discounts plus surcharges and the cleaning fee.</summary>
    Subtotal = 0,
    /// <summary>The subtotal plus the guest service fee, which step 7 computes first.</summary>
    SubtotalPlusGuestFee = 1,
    /// <summary>Room charges only; lodging levies often exclude cleaning.</summary>
    RoomOnly = 2
}

/// <summary>
/// A lodging tax for one region. docs/03 §1 step 8: a region can stack several,
/// and they are not all percentages.
/// </summary>
/// <summary>Where a rate last came from — an operator's hand, or (later) a feed.</summary>
public enum ExchangeRateSource
{
    /// <summary>
    /// First on purpose, so it is the database default. A refresh job updates
    /// only Feed rows; defaulting new rows to Manual would make that job a
    /// silent no-op — the WorkingDaysMask lesson pointing the other way.
    /// </summary>
    Feed = 0,
    /// <summary>An operator typed this rate. A feed must never overwrite it.</summary>
    Manual = 1
}

/// <summary>
/// docs/02 §J, docs/05 "Cấu hình hệ thống", docs/01 QT-06/TC-12 — one display
/// exchange rate, in the database where an operator can change it.
///
/// Until this existed the eight rates were compiled constants in
/// CatalogService, drifting from the day of each deploy with nothing anywhere
/// to say so. Display-only by construction: money is always charged in the
/// listing's own currency (docs/07 §6), so nothing here may ever reach
/// Pricing or the ledger.
/// </summary>
public class ExchangeRate
{
    public int Id { get; set; }

    /// <summary>ISO 4217, unique. "VND" is the base and its rate is exactly 1.</summary>
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public string Symbol { get; set; } = "";

    /// <summary>
    /// One đồng in this currency. Tiny by nature — USD is 0.0000392 — which is
    /// why the column carries (20,12) precision; TaxRule's (12,4) would round
    /// every western currency to zero.
    /// </summary>
    public decimal RateFromVnd { get; set; }

    public int SortOrder { get; set; }
    /// <summary>A row exists because the currency is offered; default true.</summary>
    public bool IsActive { get; set; } = true;

    public ExchangeRateSource Source { get; set; } = ExchangeRateSource.Feed;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByAdminId { get; set; }

    /// <summary>
    /// What a feed last said, kept even while an operator has pinned a manual
    /// rate — recording both is what makes drift visible on the admin screen
    /// instead of invisible in a redeploy. Null until a feed exists; the feed
    /// itself waits on the customer naming a source (docs/07 §6 wants at least
    /// six-hourly, and most free feeds publish VND daily at best).
    /// </summary>
    public decimal? FeedRate { get; set; }
    public DateTime? FeedFetchedAt { get; set; }
}

/// <summary>
/// docs/07 §6 — the display-conversion rules, deliberately NOT in Pricing.cs.
/// Money is charged in the listing's own currency and conversion is a courtesy
/// on the way to the eye; a rate that reached Pricing.Quote could put a
/// converted number into a price line, and price lines feed the ledger.
/// Pricing stays VND-only, and nothing in it calls this.
/// </summary>
public static class Fx
{
    public const string Base = "VND";

    /// <summary>
    /// Whether an operator-entered rate can be believed at all: positive, and
    /// the base currency is always exactly 1 — a VND row at any other value
    /// would rescale every price on the site in one keystroke.
    /// </summary>
    public static bool IsValidRate(string? code, decimal rate) =>
        rate > 0 && (!string.Equals((code ?? "").Trim(), Base, StringComparison.OrdinalIgnoreCase) || rate == 1m);

    /// <summary>
    /// docs/07 §6 — "ít nhất mỗi 6 giờ". A rate older than this is stale and
    /// the admin screen says so. The refresh job itself arrives with the feed
    /// the customer has yet to pick; staleness is worth showing meanwhile.
    /// </summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    public static bool Stale(DateTime updatedAtUtc, DateTime nowUtc) =>
        nowUtc - updatedAtUtc > RefreshInterval;
}

public class TaxRule
{
    public int Id { get; set; }

    public string Country { get; set; } = "Việt Nam";
    /// <summary>Null means the rule covers the whole country.</summary>
    public string? City { get; set; }

    public string Name { get; set; } = "";
    public TaxMethod Method { get; set; } = TaxMethod.Percentage;
    public TaxBase Base { get; set; } = TaxBase.SubtotalPlusGuestFee;
    /// <summary>A fraction for <see cref="TaxMethod.Percentage"/>, otherwise an amount.</summary>
    public decimal Value { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public bool AppliesOn(DateOnly night) =>
        IsActive
        && (EffectiveFrom is null || EffectiveFrom <= night)
        && (EffectiveTo is null || night <= EffectiveTo);
}
