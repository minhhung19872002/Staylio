using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// docs/03 §7 — reviews are blind both ways. Neither side sees the other's
/// until both have written one, or the 14-day window closes. This is the only
/// place that decides when a review becomes visible.
/// </summary>
public class ReviewService(StayHostDbContext db, NotificationService notifications)
{
    /// <summary>How long after check-out a review can still be written.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);

    /// <summary>docs/01 ĐG-08 — the writer may correct it inside this long.</summary>
    public static readonly TimeSpan EditWindow = TimeSpan.FromHours(48);

    /// <summary>docs/01 ĐG-02 — nudge on day 1, 7 and 13 of the window.</summary>
    public static readonly int[] ReminderDays = [1, 7, 13];

    /// <summary>The last moment a review for this booking may be written.</summary>
    public static DateTime Deadline(Booking booking) =>
        booking.CheckOut.ToDateTime(TimeOnly.MinValue) + Window;

    /// <summary>
    /// Publishes both sides of a booking's reviews when the conditions are met.
    /// Safe to call after either side submits, and again from the sweep.
    /// </summary>
    public async Task<bool> TryPublishAsync(int bookingId, CancellationToken ct)
    {
        var booking = await db.Bookings.Include(b => b.Listing).FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return false;

        var guestReview = await db.Reviews.FirstOrDefaultAsync(r => r.BookingId == bookingId, ct);
        var hostReview = await db.GuestReviews.FirstOrDefaultAsync(r => r.BookingId == bookingId, ct);

        var bothWritten = guestReview is not null && hostReview is not null;
        var windowClosed = DateTime.UtcNow >= Deadline(booking);

        if (!bothWritten && !windowClosed) return false;

        var now = DateTime.UtcNow;
        var changed = false;

        if (guestReview is { PublishedAt: null }) { guestReview.PublishedAt = now; changed = true; }
        if (hostReview is { PublishedAt: null }) { hostReview.PublishedAt = now; changed = true; }

        if (changed && guestReview is not null && booking.Listing is not null)
            await RecomputeRatingAsync(booking.ListingId, ct);

        // docs/03 §11 — "Đánh giá được công khai": both sides are told. Nobody
        // was, so a review appeared on a listing without either party knowing.
        if (changed)
        {
            var guest = booking.GuestUserId is { } gid
                ? await db.Users.FirstOrDefaultAsync(u => u.Id == gid, ct)
                : null;
            var host = await db.Users.FirstOrDefaultAsync(u => u.HostProfile!.Id == booking.Listing!.HostId, ct);
            var title = booking.Listing?.Title ?? "";

            await notifications.QueueWithEmailAsync(guest, NotificationKind.ReviewReceived,
                "Đánh giá đã được công khai",
                $"Đánh giá cho chuyến đi tại \"{title}\" (mã {booking.Reference}) đã hiện với mọi người.",
                $"/trips/{booking.Id}", ct);
            await notifications.QueueWithEmailAsync(host, NotificationKind.ReviewReceived,
                "Đánh giá đã được công khai",
                $"Đánh giá của đơn {booking.Reference} tại \"{title}\" đã hiện với mọi người.",
                "/hosting?tab=reviews", ct);
        }

        return changed;
    }

    /// <summary>
    /// A listing's score only counts reviews the world can actually see, so it
    /// moves the moment one is published rather than when it was written.
    /// </summary>
    public async Task RecomputeRatingAsync(int listingId, CancellationToken ct)
    {
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, ct);
        if (listing is null) return;

        var ratings = await db.Reviews
            .Where(r => r.ListingId == listingId && r.PublishedAt != null)
            .Select(r => r.Rating)
            .ToListAsync(ct);

        listing.Rating = ratings.Count == 0 ? 0 : Math.Round(ratings.Average(), 2);
        listing.ReviewCount = ratings.Count;

        // docs/03 §8 — "Khách chọn" is decided by the weekly review in
        // BadgeService, which also weighs cancellations and confirmed reports.
        // Setting it here from the score alone handed the title straight back to
        // a listing that review had just taken it from.
    }

    /// <summary>
    /// The sweep half: publish everything whose window has closed, and queue the
    /// day 1 / 7 / 13 reminders for stays still inside it.
    /// </summary>
    public async Task<SweepResult> SweepAsync(NotificationService notifications, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var result = new SweepResult();

        // Only stays whose window is open or just closed; the whole history was
        // read every minute.
        var horizon = DateOnly.FromDateTime(now).AddDays(-30);

        var completed = await db.Bookings
            .Where(b => b.Status == BookingStatus.Completed && b.CheckOut >= horizon)
            .Include(b => b.Listing)
            .Include(b => b.GuestUser)
            .ToListAsync(ct);

        foreach (var booking in completed)
        {
            var deadline = Deadline(booking);

            if (now >= deadline)
            {
                if (await TryPublishAsync(booking.Id, ct)) result.Published++;
                continue;
            }

            // Day 1, 7 and 13 counted from check-out, one notification each.
            var daysIn = (now - booking.CheckOut.ToDateTime(TimeOnly.MinValue)).Days;
            if (!ReviewService.ReminderDays.Contains(daysIn)) continue;

            var left = Math.Max(1, (deadline - now).Days);

            // Once per reminder day, marked in the link. The old check looked back
            // twenty hours inside a twenty-four-hour day, so each day sent two.
            var guestLink = $"/trips/{booking.Id}?danh-gia={daysIn}";
            if (!booking.HasReview && booking.GuestUserId is { } guestId
                && !await db.Notifications.AnyAsync(n => n.UserId == guestId && n.Link == guestLink, ct))
            {
                await notifications.QueueWithEmailAsync(booking.GuestUser, NotificationKind.ReviewReceived,
                    "Đánh giá chuyến đi của bạn",
                    $"Bạn còn {left} ngày để đánh giá \"{booking.Listing?.Title}\". " +
                    "Đánh giá của hai bên chỉ hiện khi cả hai đã gửi.",
                    guestLink, ct);
                result.Reminded++;
            }

            // docs/03 §7, §11 — the host is invited too ("mời viết đánh giá").
            var hostLink = $"/hosting?tab=reviews&don={booking.Id}&danh-gia={daysIn}";
            var hostUser = await db.Users.FirstOrDefaultAsync(u => u.HostProfile!.Id == booking.Listing!.HostId, ct);
            if (hostUser is not null && booking.GuestUserId is not null
                && !await db.GuestReviews.AnyAsync(r => r.BookingId == booking.Id, ct)
                && !await db.Notifications.AnyAsync(n => n.UserId == hostUser.Id && n.Link == hostLink, ct))
            {
                await notifications.QueueWithEmailAsync(hostUser, NotificationKind.ReviewReceived,
                    "Đánh giá khách của bạn",
                    $"Bạn còn {left} ngày để đánh giá {booking.GuestName} (đơn {booking.Reference}). " +
                    "Đánh giá của hai bên chỉ hiện khi cả hai đã gửi.",
                    hostLink, ct);
                result.Reminded++;
            }
        }

        if (result.Published + result.Reminded > 0) await db.SaveChangesAsync(ct);
        return result;
    }

    public sealed class SweepResult
    {
        public int Published { get; set; }
        public int Reminded { get; set; }
        public bool Any => Published + Reminded > 0;
        public override string ToString() => $"{Published} đánh giá công khai, {Reminded} lời nhắc";
    }
}
