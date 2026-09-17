namespace StayHost.Domain;

/// <summary>
/// What a guest tells the host about the stay itself, beyond the free-text note:
/// roughly when they arrive, who is actually staying when the booker is not,
/// whether it is a work trip, and a fixed list of special requests.
///
/// A fixed list rather than more free text because the host has to act on it —
/// "cũi cho em bé" is something to put in the room, and a list is something a
/// host can scan across twenty bookings and translate into any language the
/// interface speaks. None of it is a promise: the host is asked, not bound.
/// </summary>
public static class StayDetails
{
    public const int MaxNameLength = 120;

    /// <summary>Request keys and the label a host reads for each.</summary>
    public static readonly IReadOnlyDictionary<string, string> Requests = new Dictionary<string, string>
    {
        ["quiet-room"] = "Phòng yên tĩnh",
        ["high-floor"] = "Tầng cao",
        ["crib"] = "Cũi cho em bé",
        ["extra-bed"] = "Giường phụ",
        ["early-check-in"] = "Nhận phòng sớm",
        ["late-check-out"] = "Trả phòng muộn",
        ["accessible"] = "Lối đi cho xe lăn",
        ["airport-pickup"] = "Đón tại sân bay"
    };

    /// <summary>
    /// The requests in their canonical order, comma-joined, or null for none.
    /// An unknown key is an error with its name rather than something dropped,
    /// because a guest who asked for something should not be told it was sent.
    /// </summary>
    public static (string? Value, string? Error) NormaliseRequests(IEnumerable<string>? keys)
    {
        var picked = (keys ?? []).Select(k => k?.Trim().ToLowerInvariant() ?? "")
            .Where(k => k.Length > 0).Distinct().ToList();
        var unknown = picked.FirstOrDefault(k => !Requests.ContainsKey(k));
        if (unknown is not null) return (null, $"Không có yêu cầu đặc biệt \"{unknown}\".");
        var ordered = Requests.Keys.Where(picked.Contains).ToList();
        return (ordered.Count == 0 ? null : string.Join(',', ordered), null);
    }

    public static IReadOnlyList<string> Keys(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(Requests.ContainsKey).ToList();

    public static IReadOnlyList<string> Labels(string? stored) =>
        Keys(stored).Select(k => Requests[k]).ToList();

    /// <summary>An arrival window is a whole hour of the local day, 0–23.</summary>
    public static bool ValidArrival(int? hour) => hour is null or (>= 0 and <= 23);

    /// <summary>"14:00–15:00", read on the host's own clock.</summary>
    public static string? ArrivalLabel(int? hour) =>
        hour is { } h ? $"{h:00}:00–{(h + 1) % 24:00}:00" : null;

    /// <summary>
    /// The name of whoever is staying, or null when it is the booker. Trimmed,
    /// capped, and dropped when it only repeats the booker's own name.
    /// </summary>
    public static string? StayingGuest(string? name, string? bookerName)
    {
        var n = name?.Trim();
        if (string.IsNullOrEmpty(n)) return null;
        if (n.Length > MaxNameLength) n = n[..MaxNameLength];
        return string.Equals(n, bookerName?.Trim(), StringComparison.OrdinalIgnoreCase) ? null : n;
    }

    /// <summary>The details as sentences for a notice to the host; empty when none were given.</summary>
    public static IReadOnlyList<string> Sentences(Booking b)
    {
        var lines = new List<string>();
        if (ArrivalLabel(b.EstimatedArrivalHour) is { } arrival) lines.Add($"Giờ đến dự kiến: {arrival}.");
        if (!string.IsNullOrWhiteSpace(b.StayingGuestName)) lines.Add($"Người lưu trú: {b.StayingGuestName}.");
        if (b.IsBusinessTrip) lines.Add("Chuyến công tác.");
        var labels = Labels(b.SpecialRequests);
        if (labels.Count > 0) lines.Add($"Yêu cầu đặc biệt: {string.Join(", ", labels)}.");
        return lines;
    }

    /// <summary>
    /// The guest may still correct these while the stay is ahead of them —
    /// a flight moves, a baby comes along. Not once it has ended or been called off.
    /// </summary>
    public static bool Editable(BookingStatus status) =>
        status is BookingStatus.PendingPayment or BookingStatus.PendingHostApproval
            or BookingStatus.Confirmed;
}
