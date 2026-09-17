namespace StayHost.Domain.Tests;

public class MultiRoomTests
{
    private static readonly RoomTypeOption Deluxe = new() { Id = 1, Name = "Deluxe", Inventory = 3, MaxGuests = 2, PricePerNight = 1_000_000m };
    private static readonly DateOnly Monday = new(2026, 9, 7);

    [Fact]
    public void Several_rooms_fit_several_times_the_guests()
    {
        Assert.False(HotelRules.CanBook(Deluxe, 4, 0).Ok);
        Assert.True(HotelRules.CanBook(Deluxe, 4, 0, rooms: 2).Ok);
        Assert.False(HotelRules.CanBook(Deluxe, 5, 0, rooms: 2).Ok);
    }

    [Fact]
    public void Rooms_asked_for_must_all_be_free_and_the_refusal_says_how_many_are()
    {
        var check = HotelRules.CanBook(Deluxe, 2, takenOnBusiestNight: 2, rooms: 2);
        Assert.False(check.Ok);
        Assert.Contains("chỉ còn 1 phòng", check.Message);
        Assert.True(HotelRules.CanBook(Deluxe, 2, 1, rooms: 2).Ok);
        Assert.False(HotelRules.CanBook(Deluxe, 2, 0, rooms: 10).Ok);
    }

    [Fact]
    public void A_booking_of_two_rooms_takes_two_off_the_night()
    {
        var peak = HotelRules.PeakRooms(Monday, Monday.AddDays(2),
            [(Monday, Monday.AddDays(1), 2), (Monday.AddDays(1), Monday.AddDays(3), 1)]);
        Assert.Equal(2, peak);
    }

    [Fact]
    public void Price_multiplies_by_rooms_and_each_room_brings_its_free_guests()
    {
        var listing = new Listing
        {
            Id = 1, PricePerNight = 1_000_000m, WeekendSurchargeRate = 0m,
            FreeGuestThreshold = 2, ExtraGuestFee = 100_000m, CleaningFee = 0m, MaxGuests = 2
        };
        var price = Pricing.Quote(new Pricing.Request
        {
            Listing = listing, CheckIn = Monday, CheckOut = Monday.AddDays(2),
            BookedOn = Monday.AddDays(-3), Party = new PartySize(4), Rooms = 2
        });
        Assert.Equal(4_000_000m, price.RoomBeforeDiscount);
        Assert.Equal(0m, price.ExtraGuestFee);
        Assert.Equal(1_000_000m, price.NightlyRate);
        Assert.Contains("2 phòng", price.Lines[0].Label);
    }

    [Fact]
    public void Capacity_check_scales_with_rooms()
    {
        var listing = new Listing { MaxGuests = 2, IsPublished = true };
        var result = Availability.Check(new Availability.Request
        {
            Listing = listing, CheckIn = Monday, CheckOut = Monday.AddDays(1),
            Party = new PartySize(4), LocalNow = Monday.AddDays(-5).ToDateTime(TimeOnly.MinValue), Rooms = 2
        });
        Assert.NotEqual(Availability.Reason.OverCapacity, result.Reason);
    }

    [Theory]
    [InlineData("Ph", 1, 2, 1, 20, 500_000, "3 ký tự")]
    [InlineData("Phòng đôi", 0, 2, 1, 20, 500_000, "Số phòng")]
    [InlineData("Phòng đôi", 5, 0, 1, 20, 500_000, "Số khách")]
    [InlineData("Phòng đôi", 5, 2, 1, 20, 10_000, "tối thiểu")]
    public void Room_type_fields_are_refused_by_name(string name, int inv, int guests, int beds, double sqm, int price, string expected) =>
        Assert.Contains(expected, RoomTypeRules.Problem(name, inv, guests, beds, sqm, price));

    [Fact]
    public void The_listing_follows_its_rooms()
    {
        var listing = new Listing { PricePerNight = 9m, MaxGuests = 1 };
        RoomTypeRules.SyncListing(listing, [Deluxe, new RoomTypeOption { PricePerNight = 700_000m, MaxGuests = 4, Beds = 2 }]);
        Assert.Equal(700_000m, listing.PricePerNight);
        Assert.Equal(4, listing.MaxGuests);
    }
}
