namespace StayHost.Domain.Tests;

public class StayDetailsTests
{
    [Fact]
    public void Requests_are_kept_in_one_order_without_repeats()
    {
        var (value, error) = StayDetails.NormaliseRequests(["crib", " Quiet-Room ", "crib"]);
        Assert.Null(error);
        Assert.Equal("quiet-room,crib", value);
        Assert.Equal(["Phòng yên tĩnh", "Cũi cho em bé"], StayDetails.Labels(value));
    }

    [Fact]
    public void An_unknown_request_is_refused_by_name_not_dropped()
    {
        var (value, error) = StayDetails.NormaliseRequests(["crib", "jacuzzi"]);
        Assert.Null(value);
        Assert.Contains("jacuzzi", error);
    }

    [Fact]
    public void No_requests_is_stored_as_nothing() =>
        Assert.Equal((null, null), StayDetails.NormaliseRequests([]));

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    [InlineData(-1, false)]
    public void An_arrival_is_a_whole_hour_of_the_day(int? hour, bool ok) =>
        Assert.Equal(ok, StayDetails.ValidArrival(hour));

    [Fact]
    public void The_last_window_of_the_day_ends_at_midnight() =>
        Assert.Equal("23:00–00:00", StayDetails.ArrivalLabel(23));

    [Fact]
    public void Booking_for_yourself_is_not_booking_for_someone_else()
    {
        Assert.Null(StayDetails.StayingGuest(" nguyễn văn a ", "Nguyễn Văn A"));
        Assert.Equal("Trần Thị B", StayDetails.StayingGuest(" Trần Thị B ", "Nguyễn Văn A"));
        Assert.Equal(StayDetails.MaxNameLength, StayDetails.StayingGuest(new string('x', 500), null)!.Length);
    }

    [Fact]
    public void Details_can_change_only_while_the_stay_is_ahead()
    {
        Assert.True(StayDetails.Editable(BookingStatus.Confirmed));
        Assert.False(StayDetails.Editable(BookingStatus.Completed));
        Assert.False(StayDetails.Editable(BookingStatus.CancelledByGuest));
    }
}
