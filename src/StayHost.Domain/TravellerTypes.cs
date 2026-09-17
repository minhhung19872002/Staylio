namespace StayHost.Domain;

/// <summary>
/// Who wrote a review, as a kind of trip — Booking.com's "Loại khách" filter.
/// Worked out from the stay the review is about rather than asked for, so it
/// cannot be wrong about the party and needs no extra question at the end of a
/// stay. Seeded reviews have no stay behind them and so no type.
/// </summary>
public static class TravellerTypes
{
    public const string Business = "business";
    public const string Family = "family";
    public const string Couple = "couple";
    public const string Solo = "solo";
    public const string Group = "group";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Business] = "Công tác",
        [Family] = "Gia đình",
        [Couple] = "Cặp đôi",
        [Solo] = "Một mình",
        [Group] = "Nhóm bạn"
    };

    /// <summary>
    /// A work trip wins over everything; any child makes it a family; then the
    /// number of adults decides.
    /// </summary>
    public static string Of(int adults, int children, int infants, bool isBusinessTrip)
    {
        if (isBusinessTrip) return Business;
        if (children > 0 || infants > 0) return Family;
        return adults switch
        {
            <= 1 => Solo,
            2 => Couple,
            _ => Group
        };
    }
}

/// <summary>One reader saying a review helped. One vote per reader per review.</summary>
public class ReviewHelpfulVote
{
    public long Id { get; set; }
    public int ReviewId { get; set; }
    public Review? Review { get; set; }

    /// <summary>"u:12" for a signed-in reader, "s:&lt;session&gt;" otherwise.</summary>
    public string VoterKey { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
