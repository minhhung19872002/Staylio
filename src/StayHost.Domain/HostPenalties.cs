namespace StayHost.Domain;

/// <summary>
/// docs/03 §4 — "Bị trừ một khoản phạt, mức phạt tăng dần khi càng sát ngày
/// nhận phòng", and docs/09 §2.8 / §3.6 — "người dẫn / nhà cung cấp bị phạt".
///
/// The spec names the rule and leaves the amounts open. The customer asked on
/// 17/09/2026 for it to be built now, so the defaults follow Airbnb's published
/// host cancellation fees: 10% more than 30 days out, 25% between 48 hours and
/// 30 days, 50% inside 48 hours or once the stay has begun — of what the booking
/// was worth to the host. They live in configuration (<c>HostPenalty:</c>) so a
/// different reading is a settings change, not a release.
///
/// Nothing is charged at the moment of cancelling: the amount is added to what
/// the host owes the platform and comes off their next transfers, exactly as a
/// lost chargeback and a pay-at-the-door fee already do.
/// </summary>
public sealed record HostPenaltySettings
{
    public decimal RateFarOut { get; init; } = 0.10m;
    public decimal RateNear { get; init; } = 0.25m;
    public decimal RateLastMinute { get; init; } = 0.50m;

    /// <summary>Further out than this many days is "far out".</summary>
    public int FarOutDays { get; init; } = 30;

    /// <summary>Closer than this many hours (or after the start) is "last minute".</summary>
    public int LastMinuteHours { get; init; } = 48;

    /// <summary>Off switch, for a deployment that decides not to fine anybody.</summary>
    public bool Enabled { get; init; } = true;

    public static HostPenaltySettings Current { get; set; } = new();
}

public static class HostPenalties
{
    /// <summary>The share of the booking value charged for cancelling at <paramref name="now"/>.</summary>
    public static decimal RateFor(DateTime startsAtUtc, DateTime nowUtc, HostPenaltySettings? settings = null)
    {
        var s = settings ?? HostPenaltySettings.Current;
        if (!s.Enabled) return 0m;

        var ahead = startsAtUtc - nowUtc;
        if (ahead < TimeSpan.FromHours(s.LastMinuteHours)) return s.RateLastMinute;
        if (ahead <= TimeSpan.FromDays(s.FarOutDays)) return s.RateNear;
        return s.RateFarOut;
    }

    /// <summary>The fine, rounded to the đồng. Nothing on a booking worth nothing.</summary>
    public static decimal For(decimal bookingValue, DateTime startsAtUtc, DateTime nowUtc,
        HostPenaltySettings? settings = null) =>
        bookingValue <= 0
            ? 0m
            : Math.Round(bookingValue * RateFor(startsAtUtc, nowUtc, settings), 0, MidpointRounding.AwayFromZero);

    public static string Notice(decimal amount, string reference) =>
        amount <= 0
            ? ""
            : $"Phí phạt huỷ đơn {reference}: {amount:#,##0}₫, trừ vào lần chuyển tiền kế tiếp của bạn.";
}
