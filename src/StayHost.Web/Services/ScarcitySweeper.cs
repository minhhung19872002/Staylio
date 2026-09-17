using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// docs/01 YT-08, second half — "báo khi chỗ ở đã lưu … sắp hết phòng".
///
/// The price-drop half of YT-08 has an obvious trigger: the host saves a lower
/// number. This half has none — a listing runs out of nights because *other*
/// guests booked it, so nothing the owner of the wishlist did can be hooked. It
/// is therefore a sweep, and the sweep is the reason for
/// <see cref="Listing.LowAvailabilityNotifiedAt"/>: without a mark, every tick
/// would resend the same warning until the place freed up again.
///
/// The threshold is <see cref="Scarcity"/>, shared with the "Hiếm có" badge on
/// the listing page. A guest who clicks through from the notice must not land on
/// a page that disagrees with it.
/// </summary>
public class ScarcitySweeper(
    StayHostDbContext db, NotificationService notifications, ILogger<ScarcitySweeper> log)
{
    /// <summary>
    /// Only listings somebody signed-in actually saved are worth measuring; the
    /// rest would be a full calendar scan for nobody to read.
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizon = today.AddDays(Scarcity.WindowDays);

        var saved = await db.Favorites
            .Where(f => f.UserId != null)
            .Select(f => f.ListingId)
            .Distinct()
            .Take(500)
            .ToListAsync(ct);
        if (saved.Count == 0) return 0;

        var listings = await db.Listings
            .Where(l => saved.Contains(l.Id))
            .ToListAsync(ct);

        var bookings = await db.Bookings
            .Where(b => saved.Contains(b.ListingId)
                        && BookingLifecycle.BlocksDates.Contains(b.Status)
                        && b.CheckOut > today && b.CheckIn < horizon)
            .Select(b => new { b.ListingId, b.CheckIn, b.CheckOut })
            .ToListAsync(ct);

        var blocks = await db.CalendarBlocks
            .Where(b => saved.Contains(b.ListingId) && b.To >= today && b.From < horizon)
            .Select(b => new { b.ListingId, b.From, b.To, Imported = b.FeedId != null })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var listing in listings)
        {
            // The same night counted twice — booked and blocked — is still one
            // night gone, so the set does the arithmetic rather than a sum.
            var taken = new HashSet<DateOnly>();
            var closed = new HashSet<DateOnly>();

            foreach (var b in bookings.Where(x => x.ListingId == listing.Id))
                // A stay holds every night from check-in up to, not including, check-out.
                for (var d = b.CheckIn; d < b.CheckOut; d = d.AddDays(1))
                    if (d >= today && d < horizon) taken.Add(d);

            foreach (var b in blocks.Where(x => x.ListingId == listing.Id))
                // A block covers both of its endpoints. An imported one is a
                // night sold elsewhere; the host's own is a night off the market.
                for (var d = b.From; d <= b.To; d = d.AddDays(1))
                    if (d >= today && d < horizon) (b.Imported ? taken : closed).Add(d);

            var reading = Scarcity.ReadingOf(taken, closed, today);
            var scarce = Scarcity.IsRareFind(reading);

            // Opened up again: arm the next crossing and say nothing.
            if (!scarce)
            {
                if (listing.LowAvailabilityNotifiedAt is not null) listing.LowAvailabilityNotifiedAt = null;
                continue;
            }

            // Scarce, and already told about this run of it.
            if (listing.LowAvailabilityNotifiedAt is not null) continue;

            // A place nobody can book is not news either.
            if (!ListingModeration.IsPubliclyVisible(listing.IsPublished, listing.ReviewStatus)) continue;

            var savers = await db.Favorites
                .Where(f => f.ListingId == listing.Id && f.UserId != null)
                .Select(f => f.UserId!.Value)
                .Distinct()
                .ToListAsync(ct);

            var users = await db.Users.Where(u => savers.Contains(u.Id)).ToListAsync(ct);
            foreach (var saver in users)
            {
                // docs/03 §11 — this rides the same marketing topic as the price
                // drop, so anyone who silenced that is not woken by this either.
                await notifications.QueueWithEmailAsync(saver, NotificationKind.PriceDrop,
                    "Chỗ bạn đã lưu sắp hết phòng",
                    Scarcity.LowAvailabilityNotice(listing.Title, reading),
                    $"/rooms/{listing.Slug}", ct);
                sent++;
            }

            listing.LowAvailabilityNotifiedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        if (sent > 0) log.LogInformation("Đã báo {Count} lượt sắp hết phòng.", sent);
        return sent;
    }
}
