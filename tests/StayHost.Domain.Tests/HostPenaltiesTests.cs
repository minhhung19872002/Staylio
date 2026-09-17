namespace StayHost.Domain.Tests;

public class HostPenaltiesTests
{
    private static readonly DateTime Now = new(2026, 9, 17, 3, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(60 * 24, 0.10)]   // 60 days out
    [InlineData(30 * 24, 0.25)]   // exactly 30 days is no longer "far out"
    [InlineData(72, 0.25)]
    [InlineData(47, 0.50)]
    [InlineData(-5, 0.50)]        // the stay has already begun
    public void The_fine_rises_as_the_stay_comes_closer(int hoursAhead, double rate) =>
        Assert.Equal((decimal)rate, HostPenalties.RateFor(Now.AddHours(hoursAhead), Now));

    [Fact]
    public void It_is_a_share_of_the_booking_rounded_to_the_dong()
    {
        Assert.Equal(1_250_001m, HostPenalties.For(5_000_003m, Now.AddDays(10), Now));
        Assert.Equal(0m, HostPenalties.For(0m, Now.AddDays(10), Now));
    }

    [Fact]
    public void A_deployment_can_switch_it_off()
    {
        var off = new HostPenaltySettings { Enabled = false };
        Assert.Equal(0m, HostPenalties.For(5_000_000m, Now.AddHours(1), Now, off));
    }
}
