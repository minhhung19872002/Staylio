namespace StayHost.Domain;

/// <summary>
/// docs/03 §8 — the two titles, their thresholds, and when they are re-decided.
///
/// The numbers live here and nowhere else. The screen that shows a host how
/// close they are (docs/01 QL-17) and the job that actually grants or takes the
/// title away read the same rule, so a host cannot be told they qualify by one
/// and refused by the other.
/// </summary>
public static class Badges
{
    /* ------------------------------------------------ Chủ nhà Ưu tú (§8) */

    public const double SuperhostRating = 4.8;
    public const int SuperhostStaysPerYear = 10;
    /// <summary>Fewer stays still count when they were long ones.</summary>
    public const int SuperhostFewStays = 3;
    public const int SuperhostFewStayNights = 100;
    public const double SuperhostResponseRate = 90;
    public const double SuperhostCancelRate = 1;

    /// <summary>Everything the four criteria are decided from, over the last year.</summary>
    public readonly record struct HostStats(
        double Rating,
        int RatedListings,
        int Stays,
        int Nights,
        double ResponseRate,
        double CancelRate,
        /// <summary>
        /// docs/03 §4 — "Mất tư cách Chủ nhà Ưu tú trong 1 năm". One confirmed
        /// stay the host walked away from inside the year is enough, whatever
        /// the rate works out to on a large book.
        /// </summary>
        int HostCancels = 0);

    public readonly record struct Criterion(string Key, string Label, string Current, string Target, bool Met);

    /// <summary>
    /// The four criteria with where the host stands on each. Returned as a list
    /// rather than a bool so the progress screen and the decision cannot drift:
    /// qualifying is exactly "all four met".
    /// </summary>
    public static IReadOnlyList<Criterion> SuperhostCriteria(HostStats s)
    {
        var enoughStays = s.Stays >= SuperhostStaysPerYear
                          || (s.Stays >= SuperhostFewStays && s.Nights >= SuperhostFewStayNights);

        return
        [
            new("rating", $"Điểm đánh giá tổng ≥ {SuperhostRating}",
                $"{s.Rating:0.00}", $"{SuperhostRating:0.00}",
                s.RatedListings > 0 && s.Rating >= SuperhostRating),

            new("stays", $"Từ {SuperhostStaysPerYear} chuyến/năm (hoặc {SuperhostFewStays} chuyến với ≥ {SuperhostFewStayNights} đêm)",
                $"{s.Stays} chuyến · {s.Nights} đêm", $"{SuperhostStaysPerYear} chuyến", enoughStays),

            new("response", $"Tỉ lệ phản hồi ≥ {SuperhostResponseRate:0}%",
                $"{s.ResponseRate:0.##}%", $"{SuperhostResponseRate:0}%",
                s.ResponseRate >= SuperhostResponseRate),

            new("cancellations", $"Tỉ lệ tự huỷ < {SuperhostCancelRate:0}%",
                $"{s.CancelRate:0.##}%", $"{SuperhostCancelRate:0}%",
                s.CancelRate < SuperhostCancelRate && s.HostCancels == 0)
        ];
    }

    public static bool QualifiesAsSuperhost(HostStats s) => SuperhostCriteria(s).All(c => c.Met);

    /* --------------------------------------------------- Khách chọn (§8) */

    public const double FavoriteRating = 4.9;
    public const int FavoriteReviews = 5;

    /// <summary>
    /// docs/03 §8 says only "tỉ lệ huỷ thấp" for this one. Five percent is the
    /// reading taken here — five times the tolerance a Superhost gets, because
    /// this title is about the place rather than about the person running it.
    /// A number the customer may want to move; it moves here.
    /// </summary>
    public const double FavoriteCancelRate = 5;

    public readonly record struct ListingStats(
        double Rating,
        int Reviews,
        double CancelRate,
        /// <summary>Upheld reports against the listing, not merely opened ones.</summary>
        int SeriousReports);

    public static bool QualifiesAsGuestFavorite(ListingStats s) =>
        s.Reviews >= FavoriteReviews
        && s.Rating >= FavoriteRating
        && s.CancelRate < FavoriteCancelRate
        && s.SeriousReports == 0;

    /* ------------------------------------------------- when it is decided */

    private static readonly int[] ReviewMonths = [1, 4, 7, 10];

    /// <summary>docs/03 §8 — 1 January, 1 April, 1 July, 1 October.</summary>
    public static DateOnly NextSuperhostReview(DateOnly today)
    {
        foreach (var month in ReviewMonths)
        {
            var date = new DateOnly(today.Year, month, 1);
            if (date > today) return date;
        }
        return new DateOnly(today.Year + 1, 1, 1);
    }

    /// <summary>
    /// The start of the quarter <paramref name="today"/> falls in. A title
    /// decided on or after this date has been decided for the current period;
    /// anything older is due again. Comparing against this rather than against
    /// "is today the first of the quarter" is what makes a server that was off
    /// on 1 April still catch up on 2 April.
    /// </summary>
    public static DateOnly CurrentQuarterStart(DateOnly today)
    {
        var month = ReviewMonths.Last(m => m <= today.Month);
        return new DateOnly(today.Year, month, 1);
    }

    /// <summary>
    /// docs/03 §8 — "Khách chọn" is re-decided weekly. Monday is the boundary,
    /// and the same catch-up rule applies: it is the start of this week, not
    /// the question of whether today happens to be Monday.
    /// </summary>
    public static DateOnly CurrentWeekStart(DateOnly today) =>
        today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

    /// <summary>True when a title last decided on <paramref name="reviewedOn"/> is due again.</summary>
    public static bool SuperhostDue(DateOnly? reviewedOn, DateOnly today) =>
        reviewedOn is null || reviewedOn < CurrentQuarterStart(today);

    public static bool FavoriteDue(DateOnly? reviewedOn, DateOnly today) =>
        reviewedOn is null || reviewedOn < CurrentWeekStart(today);
}
