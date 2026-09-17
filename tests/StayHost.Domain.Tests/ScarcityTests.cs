namespace StayHost.Domain.Tests;

/// <summary>docs/01 TĐ-16 "Hiếm có" and the YT-08 "sắp hết phòng" notice.</summary>
public class ScarcityTests
{
    private static Scarcity.Reading Free(int nights) =>
        new(nights, Scarcity.WindowDays);

    [Fact]
    public void A_calendar_with_almost_nothing_left_is_a_rare_find()
    {
        // 25% of 60 is 15, and the rule is strictly below that share.
        Assert.True(Scarcity.IsRareFind(Free(14)));
        Assert.False(Scarcity.IsRareFind(Free(15)));
    }

    [Fact]
    public void A_wide_open_calendar_says_nothing()
    {
        Assert.False(Scarcity.IsRareFind(Free(Scarcity.WindowDays)));
        Assert.False(Scarcity.IsRareFind(Free(40)));
    }

    [Fact]
    public void A_window_too_small_to_read_is_not_evidence_of_demand()
    {
        // A brand new listing that blocked every one of its few open days is
        // empty, not popular.
        var tiny = new Scarcity.Reading(0, Scarcity.MinNightsForSignal - 1);
        Assert.False(Scarcity.IsRareFind(tiny));

        var justEnough = new Scarcity.Reading(0, Scarcity.MinNightsForSignal);
        Assert.True(Scarcity.IsRareFind(justEnough));
    }

    [Fact]
    public void An_empty_window_never_divides_by_zero()
    {
        var nothing = new Scarcity.Reading(0, 0);
        Assert.Equal(1, nothing.FreeShare);
        Assert.False(Scarcity.IsRareFind(nothing));
    }


    [Fact]
    public void The_reason_carries_the_numbers_a_guest_can_check()
    {
        var reason = Scarcity.RareFindReason(Free(4));

        Assert.Contains("4", reason);
        Assert.Contains(Scarcity.WindowDays.ToString(), reason);
    }

    [Fact]
    public void The_notice_names_the_place_once_and_reads_as_one_sentence()
    {
        var notice = Scarcity.LowAvailabilityNotice("Tra Que Farmstay", Free(4));

        Assert.Contains("Tra Que Farmstay", notice);
        Assert.Contains("4", notice);
        // The badge's wording has its own subject; splicing a title in front of
        // it gave a sentence with two, so the notice must not reuse it.
        Assert.DoesNotContain("Chỗ này", notice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_threshold_is_monotonic_so_the_badge_never_flickers()
    {
        // Both the badge and the sweep ask IsRareFind and nothing else. Fewer
        // free nights must never make a place *less* scarce, or a guest could
        // click through from the notice and find no badge waiting.
        var seenRare = false;
        for (var free = Scarcity.WindowDays; free >= 0; free--)
        {
            var rare = Scarcity.IsRareFind(Free(free));
            if (seenRare) Assert.True(rare, $"{free} free nights un-did the badge");
            seenRare |= rare;
        }
        Assert.True(seenRare);
    }

    [Fact]
    public void Nights_the_host_closed_are_not_demand()
    {
        var today = new DateOnly(2026, 9, 17);
        IEnumerable<DateOnly> Days(int from, int count) =>
            Enumerable.Range(from, count).Select(i => today.AddDays(i));

        // Fifty nights shut for repairs and nobody booked: not a rare find, and
        // with ten nights left in the window there is not enough to say anything.
        var shut = Scarcity.ReadingOf([], Days(0, 50), today);
        Assert.Equal(new Scarcity.Reading(10, 10), shut);
        Assert.False(Scarcity.IsRareFind(shut));

        // The same fifty nights sold, through bookings or an imported calendar,
        // are exactly what the badge is for.
        Assert.True(Scarcity.IsRareFind(Scarcity.ReadingOf(Days(0, 50), [], today)));

        // A night both booked and closed counts once, as closed.
        var both = Scarcity.ReadingOf(Days(0, 5), Days(0, 5), today);
        Assert.Equal(new Scarcity.Reading(Scarcity.WindowDays - 5, Scarcity.WindowDays - 5), both);
    }
}
