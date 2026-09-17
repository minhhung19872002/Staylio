namespace StayHost.Domain;

/// <summary>
/// "Staylio Thân thiết" — the Booking.com Genius idea. A guest climbs by the
/// stays they complete; a host opts a listing in with a discount; a guest at
/// level 2 or above gets it.
///
/// The discount is the host's money, like every other discount in
/// <see cref="Pricing"/>: it comes off the room charge, counts toward the same
/// cap, and the host's payout shrinks with it. The platform funds nothing, so a
/// listing that did not opt in never gives anything away.
/// </summary>
public static class Loyalty
{
    public const int Level2Stays = 3;
    public const int Level3Stays = 10;

    /// <summary>Only recent stays count: the level says how the guest travels now.</summary>
    public const int WindowDays = 730;

    /// <summary>Level 3 gets this many points on top of what the host set.</summary>
    public const int Level3Bonus = 5;

    public const int MaxHostPercent = 30;

    public static readonly IReadOnlyDictionary<int, string> Labels = new Dictionary<int, string>
    {
        [1] = "Thành viên",
        [2] = "Thân thiết",
        [3] = "Thân thiết Vàng"
    };

    /// <summary>0 for somebody not signed in: a level belongs to an account.</summary>
    public static int LevelFor(bool signedIn, int completedStays) =>
        !signedIn ? 0
        : completedStays >= Level3Stays ? 3
        : completedStays >= Level2Stays ? 2
        : 1;

    /// <summary>Completed stays still needed for the next level, or null at the top.</summary>
    public static int? StaysToNext(int level, int completedStays) => level switch
    {
        <= 1 => Math.Max(0, Level2Stays - completedStays),
        2 => Math.Max(0, Level3Stays - completedStays),
        _ => null
    };

    /// <summary>The discount this guest gets on this listing.</summary>
    public static int PercentFor(int level, int hostPercent)
    {
        if (hostPercent <= 0 || level < 2) return 0;
        var host = Math.Min(hostPercent, MaxHostPercent);
        return level >= 3 ? host + Level3Bonus : host;
    }
}
