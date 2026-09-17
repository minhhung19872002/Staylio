using StayHost.Domain;

namespace StayHost.Domain.Tests;

/// <summary>
/// The nine checks of docs/03 §2, including the rule that they run in order and
/// report the first failure rather than a generic "not available".
/// </summary>
public class AvailabilityTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private static Listing MakeListing(Action<Listing>? tweak = null)
    {
        var l = new Listing
        {
            Id = 1, Title = "Test", City = "Đà Lạt",
            MaxGuests = 4, MinNights = 1, IsPublished = true
        };
        tweak?.Invoke(l);
        return l;
    }

    private static Availability.Result Check(
        Listing listing, int startsInDays = 30, int nights = 3, PartySize? party = null,
        IReadOnlyCollection<Availability.Occupied>? occupied = null,
        IReadOnlyDictionary<DateOnly, int>? minNights = null,
        DateTime? now = null)
    {
        var at = now ?? Now;
        var checkIn = DateOnly.FromDateTime(at).AddDays(startsInDays);

        return Availability.Check(new Availability.Request
        {
            Listing = listing,
            CheckIn = checkIn,
            CheckOut = checkIn.AddDays(nights),
            Party = party ?? new PartySize(2),
            LocalNow = at,
            Occupied = occupied ?? [],
            MinNightsByDay = minNights ?? new Dictionary<DateOnly, int>()
        });
    }

    [Fact]
    public void A_clear_request_passes()
    {
        Assert.True(Check(MakeListing()).Ok);
    }

    /* --------------------------------------------------------- steps 1 to 3 */

    [Fact]
    public void Step1_an_unpublished_listing_cannot_be_booked()
    {
        var r = Check(MakeListing(l => l.IsPublished = false));
        Assert.Equal(Availability.Reason.NotBookable, r.Reason);
    }

    [Fact]
    public void Step2_capacity_counts_adults_and_children_but_not_infants()
    {
        var listing = MakeListing(l => l.MaxGuests = 3);

        Assert.False(Check(listing, party: new PartySize(2, Children: 2)).Ok);
        Assert.True(Check(listing, party: new PartySize(2, Children: 1, Infants: 3)).Ok);
    }

    [Fact]
    public void Step3_pets_need_permission_and_have_a_ceiling()
    {
        var noPets = MakeListing();
        Assert.Equal(Availability.Reason.Pets, Check(noPets, party: new PartySize(2, Pets: 1)).Reason);

        var pets = MakeListing(l => { l.PetsAllowed = true; l.MaxPets = 1; });
        Assert.True(Check(pets, party: new PartySize(2, Pets: 1)).Ok);
        Assert.Equal(Availability.Reason.Pets, Check(pets, party: new PartySize(2, Pets: 2)).Reason);
    }

    /* -------------------------------------------------------------- step 4 */

    [Fact]
    public void Step4_the_past_is_never_bookable()
    {
        Assert.Equal(Availability.Reason.AdvanceNotice, Check(MakeListing(), startsInDays: -1).Reason);
    }

    [Fact]
    public void Step4_advance_notice_blocks_stays_that_start_too_soon()
    {
        var listing = MakeListing(l => l.AdvanceNoticeHours = 48);

        Assert.Equal(Availability.Reason.AdvanceNotice, Check(listing, startsInDays: 1).Reason);
        Assert.True(Check(listing, startsInDays: 3).Ok);
    }

    [Fact]
    public void Step4_same_day_booking_respects_the_cut_off_hour()
    {
        var listing = MakeListing(l => l.SameDayCutoffHour = 12);

        var morning = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Unspecified);
        Assert.True(Check(listing, startsInDays: 0, now: morning).Ok);

        var evening = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(Availability.Reason.AdvanceNotice, Check(listing, startsInDays: 0, now: evening).Reason);
    }

    /* -------------------------------------------------------------- step 5 */

    [Fact]
    public void Step5_the_calendar_only_opens_so_far_ahead()
    {
        var listing = MakeListing(l => l.CalendarVisibilityMonths = 3);

        Assert.True(Check(listing, startsInDays: 60).Ok);
        Assert.Equal(Availability.Reason.CalendarHorizon, Check(listing, startsInDays: 120).Reason);
    }

    /* -------------------------------------------------------------- step 6 */

    [Fact]
    public void Step6_night_count_has_a_floor_and_a_ceiling()
    {
        var listing = MakeListing(l => { l.MinNights = 3; l.MaxNights = 10; });

        Assert.Equal(Availability.Reason.NightCount, Check(listing, nights: 2).Reason);
        Assert.True(Check(listing, nights: 3).Ok);
        Assert.Equal(Availability.Reason.NightCount, Check(listing, nights: 11).Reason);
    }

    [Fact]
    public void Step6_a_per_day_minimum_overrides_the_listing_minimum()
    {
        var listing = MakeListing(l => l.MinNights = 1);
        var checkIn = Today.AddDays(30);
        var minNights = new Dictionary<DateOnly, int> { [checkIn] = 5 };

        var r = Check(listing, nights: 2, minNights: minNights);
        Assert.Equal(Availability.Reason.NightCount, r.Reason);
        Assert.Contains("5 đêm", r.Message);
    }

    [Fact]
    public void Step6_a_per_day_minimum_can_also_be_lower()
    {
        // "ưu tiên giá trị riêng đó" — a host opening a single night on a place
        // that normally needs three is using the rule, not breaking it.
        var listing = MakeListing(l => l.MinNights = 3);
        var checkIn = Today.AddDays(30);
        var minNights = new Dictionary<DateOnly, int> { [checkIn] = 1 };

        Assert.True(Check(listing, nights: 1, minNights: minNights).Ok);
        Assert.Equal(Availability.Reason.NightCount, Check(listing, nights: 1).Reason);
    }

    /* -------------------------------------------------------------- step 7 */

    [Fact]
    public void Step7_a_host_can_refuse_certain_weekdays()
    {
        var checkIn = Today.AddDays(30);
        var listing = MakeListing(l => l.BlockedCheckInDays = Availability.MaskOf(checkIn.DayOfWeek));

        Assert.Equal(Availability.Reason.BlockedWeekday, Check(listing, startsInDays: 30).Reason);

        // A different weekday leaves it alone.
        var other = MakeListing(l => l.BlockedCheckInDays = Availability.MaskOf(checkIn.AddDays(1).DayOfWeek));
        Assert.True(Check(other, startsInDays: 30).Ok);
    }

    [Fact]
    public void Step7_check_out_weekdays_are_a_separate_rule()
    {
        var checkOut = Today.AddDays(33);
        var listing = MakeListing(l => l.BlockedCheckOutDays = Availability.MaskOf(checkOut.DayOfWeek));

        var r = Check(listing, startsInDays: 30, nights: 3);
        Assert.Equal(Availability.Reason.BlockedWeekday, r.Reason);
        Assert.Contains("trả phòng", r.Message);
    }

    /* -------------------------------------------------------------- step 8 */

    [Fact]
    public void Step8_an_overlapping_stay_blocks_the_dates()
    {
        var checkIn = Today.AddDays(30);
        var occupied = new[] { new Availability.Occupied(checkIn.AddDays(1), checkIn.AddDays(4), false) };

        Assert.Equal(Availability.Reason.DatesTaken, Check(MakeListing(), occupied: occupied).Reason);
    }

    [Fact]
    public void Step8_back_to_back_stays_are_allowed()
    {
        // The previous guest checks out on the day the new one checks in. The
        // last night of a stay is the day before check-out, so nothing overlaps.
        var checkIn = Today.AddDays(30);
        var occupied = new[] { new Availability.Occupied(checkIn.AddDays(-3), checkIn, false) };

        Assert.True(Check(MakeListing(), occupied: occupied).Ok);
    }

    [Fact]
    public void Step8_a_host_block_covers_both_of_its_end_dates()
    {
        var checkIn = Today.AddDays(30);
        var block = new[] { new Availability.Occupied(checkIn.AddDays(-3), checkIn, true) };

        Assert.Equal(Availability.Reason.DatesTaken, Check(MakeListing(), occupied: block).Reason);
    }

    /* -------------------------------------------------------------- step 9 */

    [Fact]
    public void Step9_turnover_time_keeps_two_stays_apart()
    {
        var listing = MakeListing(l => l.TurnoverDays = 2);
        var checkIn = Today.AddDays(30);

        // Previous guest leaves the day before: one clear day, two are needed.
        var tooClose = new[] { new Availability.Occupied(checkIn.AddDays(-4), checkIn.AddDays(-1), false) };
        Assert.Equal(Availability.Reason.TurnoverTime, Check(listing, occupied: tooClose).Reason);

        var farEnough = new[] { new Availability.Occupied(checkIn.AddDays(-5), checkIn.AddDays(-2), false) };
        Assert.True(Check(listing, occupied: farEnough).Ok);
    }

    [Fact]
    public void Step9_back_to_back_stays_have_no_turnover_at_all()
    {
        var listing = MakeListing(l => l.TurnoverDays = 2);
        var checkIn = Today.AddDays(30);

        // The previous guest leaves the very day this one arrives, and the next
        // one arrives the day this one leaves: zero days either side.
        var before = new[] { new Availability.Occupied(checkIn.AddDays(-3), checkIn, false) };
        Assert.Equal(Availability.Reason.TurnoverTime, Check(listing, occupied: before).Reason);

        var after = new[] { new Availability.Occupied(checkIn.AddDays(3), checkIn.AddDays(6), false) };
        Assert.Equal(Availability.Reason.TurnoverTime, Check(listing, occupied: after).Reason);
    }

    /* ------------------------------------------------------------- ordering */

    [Fact]
    public void The_first_failing_step_is_the_one_reported()
    {
        // Over capacity (step 2) and the dates are taken (step 8). Step 2 wins.
        var listing = MakeListing(l => l.MaxGuests = 2);
        var checkIn = Today.AddDays(30);
        var occupied = new[] { new Availability.Occupied(checkIn, checkIn.AddDays(3), false) };

        var r = Check(listing, party: new PartySize(4), occupied: occupied);
        Assert.Equal(Availability.Reason.OverCapacity, r.Reason);
    }

    [Fact]
    public void Every_failure_carries_its_own_message()
    {
        var listing = MakeListing(l => l.MaxGuests = 1);
        var r = Check(listing, party: new PartySize(4));

        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Message));
        Assert.DoesNotContain("không khả dụng", r.Message);
    }
}
