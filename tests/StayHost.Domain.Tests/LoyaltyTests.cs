namespace StayHost.Domain.Tests;

public class LoyaltyTests
{
    [Theory]
    [InlineData(false, 20, 0)]
    [InlineData(true, 0, 1)]
    [InlineData(true, 2, 1)]
    [InlineData(true, 3, 2)]
    [InlineData(true, 9, 2)]
    [InlineData(true, 10, 3)]
    public void Stays_decide_the_level_and_an_account_is_needed(bool signedIn, int stays, int level) =>
        Assert.Equal(level, Loyalty.LevelFor(signedIn, stays));

    [Fact]
    public void Only_a_listing_that_opted_in_gives_anything_and_only_from_level_two()
    {
        Assert.Equal(0, Loyalty.PercentFor(3, 0));
        Assert.Equal(0, Loyalty.PercentFor(1, 10));
        Assert.Equal(10, Loyalty.PercentFor(2, 10));
        Assert.Equal(15, Loyalty.PercentFor(3, 10));
        Assert.Equal(Loyalty.MaxHostPercent, Loyalty.PercentFor(2, 90));
    }

    [Fact]
    public void Progress_counts_down_to_the_next_level()
    {
        Assert.Equal(2, Loyalty.StaysToNext(1, 1));
        Assert.Equal(6, Loyalty.StaysToNext(2, 4));
        Assert.Null(Loyalty.StaysToNext(3, 12));
    }

    [Fact]
    public void The_discount_is_a_room_discount_under_the_same_cap()
    {
        var monday = new DateOnly(2026, 9, 7);
        var price = Pricing.Quote(new Pricing.Request
        {
            Listing = new Listing { Id = 1, PricePerNight = 1_000_000m, WeekendSurchargeRate = 0m, FreeGuestThreshold = 2 },
            CheckIn = monday,
            CheckOut = monday.AddDays(2),
            BookedOn = monday.AddDays(-3),
            LoyaltyPercent = 15
        });
        Assert.Equal(300_000m, price.RoomDiscount);
        Assert.Contains(price.DiscountParts, p => p.Key == "loyalty" && p.Percent == 15);
    }
}
