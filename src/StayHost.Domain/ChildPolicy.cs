namespace StayHost.Domain;

/// <summary>
/// "Chính sách trẻ em và giường phụ" — what Booking.com shows on every property.
/// Whether children may stay is a rule the booking enforces (Availability);
/// cots and extra beds are information, which a guest then asks for with a
/// special request (StayDetails) and the host confirms.
/// </summary>
public static class ChildPolicy
{
    public static bool Refuses(Listing l, PartySize party) =>
        !l.ChildrenAllowed && (party.Children > 0 || party.Infants > 0);

    public const string RefusalMessage = "Chỗ nghỉ này không nhận trẻ em.";

    /// <summary>The lines the room page shows, in reading order.</summary>
    public static IReadOnlyList<string> Lines(Listing l)
    {
        if (!l.ChildrenAllowed) return ["Không nhận trẻ em."];
        return
        [
            "Trẻ em ở mọi độ tuổi đều được chào đón.",
            l.CribAvailable ? "Có cũi cho em bé — ghi trong yêu cầu đặc biệt khi đặt." : "Không có cũi cho em bé.",
            l.ExtraBedAvailable ? "Có giường phụ — ghi trong yêu cầu đặc biệt khi đặt." : "Không có giường phụ."
        ];
    }
}
