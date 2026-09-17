namespace StayHost.Domain;

/// <summary>
/// docs/01 MR-08 — one property with several kinds of room, each with a number
/// of them. A hotel is an ordinary listing with these attached, so search,
/// pricing, the booking lifecycle and the ledger all work on it unchanged.
/// </summary>
public class RoomTypeOption
{
    public int Id { get; set; }

    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";

    /// <summary>How many rooms of this kind the property has.</summary>
    public int Inventory { get; set; } = 1;

    public int MaxGuests { get; set; } = 2;
    public int Beds { get; set; } = 1;
    public double SizeSqm { get; set; }

    /// <summary>
    /// The nightly rate for this kind of room. The listing's own price is the
    /// cheapest room, which is what the search card shows.
    /// </summary>
    public decimal PricePerNight { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>One per line: what this room has that the others do not.</summary>
    public string Features { get; set; } = "";

    public int SortOrder { get; set; }

    /* ------------------------------------------------ rate plans (Booking.com) */

    /// <summary>
    /// A cheaper rate the guest may take in exchange for no refund at all.
    /// 0 means this room is not sold that way.
    /// </summary>
    public int NonRefundableDiscountPercent { get; set; }

    /// <summary>Breakfast, per counted guest per night. 0 means not offered.</summary>
    public decimal BreakfastPricePerGuest { get; set; }

    public IReadOnlyList<string> FeatureList =>
        Features.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// docs/01 MR-10 — a guest who finds the same room cheaper somewhere else gets
/// the difference back as balance rather than cash.
/// </summary>
public enum PriceMatchStatus
{
    Submitted = 0,
    Approved = 1,
    Rejected = 2
}

public class PriceMatchClaim
{
    public int Id { get; set; }

    public int BookingId { get; set; }
    public Booking? Booking { get; set; }

    public int GuestUserId { get; set; }
    public User? GuestUser { get; set; }

    /// <summary>Where the guest saw it cheaper, and for how much a night.</summary>
    public string CompetitorUrl { get; set; } = "";
    public decimal CompetitorNightlyRate { get; set; }

    /// <summary>What they paid a night here, frozen so a later price change cannot rewrite it.</summary>
    public decimal OurNightlyRate { get; set; }

    /// <summary>What the guest would get, worked out when the claim was made.</summary>
    public decimal Difference { get; set; }

    public PriceMatchStatus Status { get; set; } = PriceMatchStatus.Submitted;
    public string? Decision { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
}

public static class HotelRules
{
    /// <summary>docs/01 MR-10 — how long after booking a price match may be raised.</summary>
    public static readonly TimeSpan PriceMatchWindow = TimeSpan.FromHours(24);

    /// <summary>Anything below this is not worth a claim on either side.</summary>
    public const decimal MinimumDifference = 10_000m;

    public enum Refusal
    {
        None = 0,
        NotAHotel,
        UnknownRoomType,
        SoldOut,
        TooManyGuests
    }

    public readonly record struct Check(bool Ok, Refusal Reason, string Message)
    {
        public static Check Pass => new(true, Refusal.None, "");
        public static Check Fail(Refusal reason, string message) => new(false, reason, message);
    }

    /// <summary>
    /// docs/01 MR-08 — a hotel sells rooms of a kind, so what matters is how
    /// many of that kind are taken on the busiest night of the stay, not
    /// whether the property is booked at all.
    /// </summary>
    public static Check CanBook(RoomTypeOption? room, int guests, int takenOnBusiestNight)
    {
        if (room is null)
            return Check.Fail(Refusal.UnknownRoomType, "Chọn một loại phòng trước khi đặt.");

        if (guests > room.MaxGuests)
            return Check.Fail(Refusal.TooManyGuests, $"Phòng {room.Name} nhận tối đa {room.MaxGuests} khách.");

        return takenOnBusiestNight < room.Inventory
            ? Check.Pass
            : Check.Fail(Refusal.SoldOut, $"Phòng {room.Name} đã hết cho những ngày này.");
    }

    /// <summary>
    /// The most rooms of one kind occupied on any single night of a stay. A
    /// booking that leaves before another arrives does not stack.
    /// </summary>
    public static int PeakOccupancy(
        DateOnly checkIn, DateOnly checkOut, IReadOnlyCollection<(DateOnly From, DateOnly To)> taken)
    {
        var peak = 0;
        for (var night = checkIn; night < checkOut; night = night.AddDays(1))
        {
            var d = night;
            peak = Math.Max(peak, taken.Count(t => t.From <= d && d < t.To));
        }
        return peak;
    }

    /// <summary>
    /// What a price match is worth: the nightly gap across every night of the
    /// stay, and nothing when the competitor is not actually cheaper.
    /// </summary>
    public static decimal MatchValue(decimal ourNightly, decimal theirNightly, int nights)
    {
        var gap = ourNightly - theirNightly;
        return gap < MinimumDifference ? 0m : Math.Round(gap * Math.Max(1, nights));
    }

    public static bool WithinWindow(DateTime bookedAt, DateTime now) =>
        now - bookedAt <= PriceMatchWindow;

    public static string StatusLabel(PriceMatchStatus status) => status switch
    {
        PriceMatchStatus.Approved => "Đã chấp nhận",
        PriceMatchStatus.Rejected => "Đã từ chối",
        _ => "Đang xem xét"
    };
}

/// <summary>
/// What the guest picked on top of a hotel room: the non-refundable rate, and
/// breakfast. Resolved against the room's own offer before pricing, so
/// <see cref="Pricing"/> only ever sees numbers the host set.
/// </summary>
public sealed record RatePlan(int NonRefundableDiscountPercent, decimal BreakfastPerGuestPerNight)
{
    public const int MaxNonRefundablePercent = 50;

    public static readonly RatePlan None = new(0, 0);

    /// <summary>
    /// The plan for a room, or the reason the choice cannot be sold. Asking for
    /// something the room does not offer is refused by name, not quietly
    /// dropped — a guest who ticked breakfast must not arrive to find none.
    /// </summary>
    public static (RatePlan? Plan, string? Error) Resolve(RoomTypeOption? room, bool nonRefundable, bool breakfast)
    {
        if (!nonRefundable && !breakfast) return (None, null);
        if (room is null) return (null, "Gói giá chỉ áp dụng khi chọn loại phòng khách sạn.");
        if (nonRefundable && room.NonRefundableDiscountPercent <= 0)
            return (null, "Loại phòng này không bán giá không hoàn tiền.");
        if (breakfast && room.BreakfastPricePerGuest <= 0)
            return (null, "Loại phòng này không có bữa sáng.");
        return (new RatePlan(
            nonRefundable ? Math.Min(room.NonRefundableDiscountPercent, MaxNonRefundablePercent) : 0,
            breakfast ? room.BreakfastPricePerGuest : 0), null);
    }

    /// <summary>A non-refundable plan replaces whatever tier the listing has.</summary>
    public CancellationTier TierFor(CancellationTier listingTier) =>
        NonRefundableDiscountPercent > 0 ? CancellationTier.NonRefundable : listingTier;
}
