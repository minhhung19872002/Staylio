using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// docs/03 §8 — grants and takes away the two titles.
///
/// Until this existed, `IsSuperhost` was a column the seeder filled and nothing
/// ever changed: a host could meet all four criteria for a year and never be
/// given the title, and one who stopped meeting them kept it for ever.
///
/// The decision is driven off a per-row stamp rather than off "is today the
/// first of the quarter", so a server that was down on 1 April still catches up
/// on 2 April, and running the sweep twice in a day changes nothing.
/// </summary>
public class BadgeService(StayHostDbContext db, NotificationService notifications, ILogger<BadgeService> log)
{
    public sealed record Result(int HostsReviewed, int HostsGained, int HostsLost, int ListingsReviewed, int ListingsChanged);

    public async Task<Result> SweepAsync(CancellationToken ct, DateOnly? asOf = null)
    {
        var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var (reviewed, gained, lost) = await ReviewSuperhostsAsync(today, ct);
        var (listings, changed) = await ReviewGuestFavoritesAsync(today, ct);

        if (reviewed + listings > 0)
            log.LogInformation(
                "Badge sweep {Today}: {Hosts} hosts (+{Gained}/-{Lost}), {Listings} listings ({Changed} changed).",
                today, reviewed, gained, lost, listings, changed);

        return new Result(reviewed, gained, lost, listings, changed);
    }

    /* --------------------------------------------- Chủ nhà Ưu tú, quarterly */

    private async Task<(int Reviewed, int Gained, int Lost)> ReviewSuperhostsAsync(DateOnly today, CancellationToken ct)
    {
        var quarter = Badges.CurrentQuarterStart(today);

        var due = await db.Hosts
            .Where(h => h.SuperhostReviewedOn == null || h.SuperhostReviewedOn < quarter)
            .Include(h => h.User)
            .ToListAsync(ct);

        if (due.Count == 0) return (0, 0, 0);

        var yearAgo = today.AddYears(-1);
        var gained = 0;
        var lost = 0;

        foreach (var host in due)
        {
            var stats = await HostStatsAsync(host, yearAgo, ct);
            var qualifies = Badges.QualifiesAsSuperhost(stats);
            var was = host.IsSuperhost;

            host.IsSuperhost = qualifies;
            host.SuperhostReviewedOn = quarter;

            // Every listing carries the host's badge, because that is what the
            // search filter reads. Synced on every review rather than only when
            // the decision changed: a listing seeded — or edited — out of step
            // would otherwise keep a badge its host does not hold. The filter on
            // the update means an already-agreeing row costs nothing.
            await db.Listings.Where(l => l.HostId == host.Id && l.IsSuperhost != qualifies)
                .ExecuteUpdateAsync(u => u.SetProperty(l => l.IsSuperhost, qualifies), ct);

            if (qualifies && !was)
            {
                gained++;
                await notifications.QueueWithEmailAsync(host.User, NotificationKind.System,
                    "Bạn đã đạt danh hiệu Siêu chủ nhà",
                    "Bốn tiêu chí của quý này đều đạt. Danh hiệu hiển thị trên mọi tin đăng của bạn.",
                    "/hosting", ct);
            }
            else if (!qualifies && was)
            {
                lost++;
                // docs/03 §8 — "Mất danh hiệu không phải là hình phạt vĩnh viễn."
                await notifications.QueueWithEmailAsync(host.User, NotificationKind.System,
                    "Danh hiệu Siêu chủ nhà tạm dừng",
                    $"Quý này chưa đủ cả bốn tiêu chí. Xét lại vào {Badges.NextSuperhostReview(today):dd/MM/yyyy} — " +
                    "đạt lại thì có lại.",
                    "/hosting", ct);
            }
        }

        await db.SaveChangesAsync(ct);
        return (due.Count, gained, lost);
    }

    /// <summary>
    /// docs/01 QL-17 — the same numbers, for showing a host where they stand
    /// before the quarter turns.
    /// </summary>
    public Task<Badges.HostStats> ProgressStatsAsync(HostProfile host, CancellationToken ct) =>
        HostStatsAsync(host, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1), ct);

    /// <summary>The four numbers docs/03 §8 decides on, over the last year.</summary>
    private async Task<Badges.HostStats> HostStatsAsync(HostProfile host, DateOnly since, CancellationToken ct)
    {
        var listings = await db.Listings.Where(l => l.HostId == host.Id)
            .Select(l => new { l.Id, l.Rating, l.ReviewCount })
            .ToListAsync(ct);

        var listingIds = listings.Select(l => l.Id).ToList();

        // A trip is one that happened. Counting every booking checking in after
        // `since` took in unpaid holds and next year's stays, so a host could
        // "have" ten trips a year before hosting one.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var stays = await db.Bookings
            .Where(b => listingIds.Contains(b.ListingId) && b.CheckIn >= since && b.CheckIn <= today)
            .Select(b => new { b.Status, b.Nights, b.CancelledBy })
            .ToListAsync(ct);

        var completed = stays.Where(s => s.Status == BookingStatus.Completed).ToList();

        // docs/03 §8 — "tự huỷ" is a confirmed stay the host walked away from.
        // Declining a request carries CancelledBy.Host too, and used to count.
        var hostCancels = stays.Count(s => s.Status == BookingStatus.CancelledByHost
                                           && s.CancelledBy == CancelledBy.Host);
        var orders = Math.Max(1, completed.Count + hostCancels);

        var rated = listings.Where(l => l.ReviewCount > 0).ToList();

        return new Badges.HostStats(
            rated.Count == 0 ? 0 : Math.Round(rated.Average(l => l.Rating), 2),
            rated.Count,
            completed.Count,
            completed.Sum(s => s.Nights),
            ParsePercent(host.ResponseRate),
            Math.Round(hostCancels * 100.0 / orders, 2),
            hostCancels);
    }

    /* ------------------------------------------------- Khách chọn, weekly */

    private async Task<(int Reviewed, int Changed)> ReviewGuestFavoritesAsync(DateOnly today, CancellationToken ct)
    {
        var week = Badges.CurrentWeekStart(today);

        var due = await db.Listings
            .Where(l => l.FavoriteReviewedOn == null || l.FavoriteReviewedOn < week)
            .Select(l => new { l.Id, l.Rating, l.ReviewCount, l.IsGuestFavorite })
            .ToListAsync(ct);

        if (due.Count == 0) return (0, 0);

        var ids = due.Select(l => l.Id).ToList();
        var yearAgo = today.AddYears(-1);

        var cancels = await db.Bookings
            .Where(b => ids.Contains(b.ListingId) && b.CheckIn >= yearAgo)
            .GroupBy(b => b.ListingId)
            .Select(g => new
            {
                ListingId = g.Key,
                Total = g.Count(),
                Cancelled = g.Count(b => b.Status == BookingStatus.CancelledByHost
                                         || b.Status == BookingStatus.CancelledByGuest)
            })
            .ToListAsync(ct);

        // docs/03 §8 — an upheld report, not merely one somebody filed. Reports
        // about people, messages and reviews (docs/01 AT-02) share the table but
        // say nothing about the listing, so they are filtered out here.
        var reports = await db.AbuseReports
            .Where(r => r.Target == ReportTarget.Listing
                        && r.ListingId != null && ids.Contains(r.ListingId.Value)
                        && r.Status == ReportStatus.Resolved)
            .GroupBy(r => r.ListingId!.Value)
            .Select(g => new { ListingId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var cancelBy = cancels.ToDictionary(c => c.ListingId, c => c.Total == 0 ? 0 : c.Cancelled * 100.0 / c.Total);
        var reportBy = reports.ToDictionary(r => r.ListingId, r => r.Count);
        var changed = 0;

        foreach (var listing in due)
        {
            var qualifies = Badges.QualifiesAsGuestFavorite(new Badges.ListingStats(
                listing.Rating,
                listing.ReviewCount,
                cancelBy.GetValueOrDefault(listing.Id),
                reportBy.GetValueOrDefault(listing.Id)));

            if (qualifies != listing.IsGuestFavorite) changed++;

            await db.Listings.Where(l => l.Id == listing.Id)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(l => l.IsGuestFavorite, qualifies)
                    .SetProperty(l => l.FavoriteReviewedOn, week), ct);
        }

        return (due.Count, changed);
    }

    /// <summary>The response rate is stored as "97%"; the rule wants the number.</summary>
    public static double ParsePercent(string? value) =>
        double.TryParse(new string((value ?? "").Where(char.IsDigit).ToArray()), out var n) ? n : 0;
}
