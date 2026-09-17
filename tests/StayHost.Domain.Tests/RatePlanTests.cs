namespace StayHost.Domain.Tests;

public class RatePlanTests
{
    private static readonly DateOnly Monday = new(2026, 9, 7);

    private static Pricing.Request Stay(RatePlan plan, PartySize? party = null) => new()
    {
        Listing = new Listing
        {
            Id = 1, City = "Đà Lạt", Country = "Việt Nam", PricePerNight = 1_000_000m,
            CleaningFee = 0m, WeekendSurchargeRate = 0m, FreeGuestThreshold = 4, MaxGuests = 6
        },
        CheckIn = Monday,
        CheckOut = Monday.AddDays(2),
        Party = party ?? new PartySize(2),
        BookedOn = Monday.AddDays(-3),
        NightlyRateOverride = 1_000_000m,
        Plan = plan
    };

    private static readonly RoomTypeOption Room = new()
    {
        Id = 5, PricePerNight = 1_000_000m, NonRefundableDiscountPercent = 10, BreakfastPricePerGuest = 150_000m
    };

    [Fact]
    public void Non_refundable_is_a_room_discount_and_fees_follow_it()
    {
        var p = Pricing.Quote(Stay(new RatePlan(10, 0)));
        Assert.Equal(200_000m, p.RoomDiscount);
        Assert.Equal(1_800_000m, p.Subtotal);
        Assert.Contains(p.Lines, l => l.Key == "discount" && l.Label.Contains("không hoàn tiền"));
    }

    [Fact]
    public void Breakfast_is_per_counted_guest_per_night_and_part_of_the_subtotal()
    {
        // Two adults and an infant: the infant eats free.
        var p = Pricing.Quote(Stay(new RatePlan(0, 150_000m), new PartySize(2, 0, 1, 0)));
        Assert.Equal(600_000m, p.BreakfastFee);
        Assert.Equal(2_600_000m, p.Subtotal);
        Assert.Equal(p.Subtotal - p.HostServiceFee, p.HostPayout);
        Assert.Contains(p.Lines, l => l.Key == "breakfast" && l.Amount == 600_000m);
    }

    [Fact]
    public void Nothing_picked_prices_exactly_as_before()
    {
        var p = Pricing.Quote(Stay(RatePlan.None));
        Assert.Equal(0m, p.BreakfastFee);
        Assert.Equal(2_000_000m, p.Subtotal);
    }

    [Fact]
    public void A_choice_the_room_does_not_sell_is_refused_by_name()
    {
        var bare = new RoomTypeOption { Id = 6 };
        Assert.Contains("không hoàn tiền", RatePlan.Resolve(bare, true, false).Error);
        Assert.Contains("bữa sáng", RatePlan.Resolve(bare, false, true).Error);
        Assert.NotNull(RatePlan.Resolve(null, false, true).Error);
        Assert.Equal(RatePlan.None, RatePlan.Resolve(null, false, false).Plan);
    }

    [Fact]
    public void The_room_decides_the_numbers_and_non_refundable_changes_the_tier()
    {
        var (plan, error) = RatePlan.Resolve(Room, true, true);
        Assert.Null(error);
        Assert.Equal(new RatePlan(10, 150_000m), plan);
        Assert.Equal(CancellationTier.NonRefundable, plan!.TierFor(CancellationTier.Flexible));
        Assert.Equal(CancellationTier.Flexible, RatePlan.None.TierFor(CancellationTier.Flexible));
    }
}
