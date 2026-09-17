namespace StayHost.Domain.Tests;

public class TripShareTests
{
    [Fact]
    public void Only_a_confirmed_stay_is_worth_forwarding()
    {
        Assert.True(TripShare.CanShare(BookingStatus.Confirmed));
        Assert.True(TripShare.CanShare(BookingStatus.InProgress));
        Assert.False(TripShare.CanShare(BookingStatus.PendingPayment));
        Assert.False(TripShare.CanShare(BookingStatus.CancelledByHost));
    }

    [Fact]
    public void The_message_carries_the_plan_and_not_the_keys()
    {
        var body = TripShare.Body("Lan", "Nhà Gỗ", "Đà Lạt",
            new DateOnly(2026, 12, 10), new DateOnly(2026, 12, 12), 2, 3, "SH-ABC");
        Assert.Contains("10/12/2026", body);
        Assert.Contains("SH-ABC", body);
        Assert.DoesNotContain("₫", body);
        Assert.DoesNotContain("mã cửa", body, StringComparison.OrdinalIgnoreCase);
    }
}
