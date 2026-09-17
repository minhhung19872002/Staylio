namespace StayHost.Domain;

/// <summary>
/// "Gửi xác nhận cho người đi cùng" — the booker forwards the plan of a stay to
/// somebody travelling with them, the way Booking.com lets you share a
/// confirmation.
///
/// What goes out is what a companion needs to plan around — where (the city),
/// when, how many, whose name is on the booking — and nothing that belongs to
/// the booker alone: no price, no address, no door code. Those stay behind the
/// booker's own sign-in, because an email address typed into a box is not an
/// identity, and forwarding a door code to a mistyped address is a key handed
/// to a stranger.
/// </summary>
public static class TripShare
{
    /// <summary>A cap per booking, so the box is not a way to send mail to anybody.</summary>
    public const int MaxPerBooking = 10;

    public static bool CanShare(BookingStatus status) =>
        status is BookingStatus.Confirmed or BookingStatus.InProgress;

    public static string Subject(string reference) => $"Chuyến đi {reference} đã được xác nhận";

    public static string Body(
        string bookerName, string listingTitle, string city,
        DateOnly checkIn, DateOnly checkOut, int nights, int guests, string reference) =>
        $"{bookerName} đã đặt \"{listingTitle}\" ở {city} cho chuyến đi của các bạn: " +
        $"nhận phòng {checkIn:dd/MM/yyyy}, trả phòng {checkOut:dd/MM/yyyy} " +
        $"({nights} đêm, {guests} khách). Mã đặt chỗ {reference}. " +
        "Địa chỉ chính xác và hướng dẫn nhận phòng nằm ở chỗ người đặt.";
}
