using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// docs/01 TN-04 and TN-09 — the lines the platform writes into a conversation
/// itself: order confirmed, order cancelled, check-in tomorrow, checkout today.
/// They are never masked and never counted as unread work for either side.
/// </summary>
public class ThreadMessenger(StayHostDbContext db)
{
    /// <summary>
    /// Posts a system line into the guest ↔ host thread for a booking, creating
    /// the thread if the two have not spoken yet. Silent when the host is one of
    /// the seeded demo profiles with no account behind it.
    /// </summary>
    public async Task PostAsync(Booking booking, string body, CancellationToken ct)
    {
        if (booking.GuestUserId is not int guestId) return;

        var hostUserId = await db.Listings
            .Where(l => l.Id == booking.ListingId)
            .Select(l => l.Host!.UserId)
            .FirstOrDefaultAsync(ct);

        if (hostUserId is not int hostId || hostId == guestId) return;

        var thread = await db.MessageThreads
            .FirstOrDefaultAsync(t => t.ListingId == booking.ListingId && t.GuestUserId == guestId, ct);

        if (thread is null)
        {
            thread = new MessageThread
            {
                ListingId = booking.ListingId,
                GuestUserId = guestId,
                HostUserId = hostId,
                BookingId = booking.Id
            };
            db.MessageThreads.Add(thread);
            await db.SaveChangesAsync(ct);
        }
        else if (thread.BookingId is null)
        {
            thread.BookingId = booking.Id;
        }

        db.Messages.Add(new Message
        {
            ThreadId = thread.Id,
            // Attributed to the host so it renders on their side of the thread;
            // IsSystem is what actually changes how it looks and behaves.
            SenderUserId = hostId,
            Body = body,
            IsSystem = true,
            ReadAt = DateTime.UtcNow
        });

        thread.LastMessageAt = DateTime.UtcNow;
    }

    /// <summary>
    /// docs/01 TN-09 — the milestone messages, checked once a day's worth of
    /// ticks. Each one is posted at most once per booking.
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var posted = 0;

        var soon = await db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed && b.CheckIn == today.AddDays(1))
            .Include(b => b.Listing)
            .ToListAsync(ct);

        foreach (var b in soon)
        {
            if (await AlreadyPostedAsync(b, "Ngày mai bạn nhận phòng", ct)) continue;
            await PostAsync(b,
                $"Ngày mai bạn nhận phòng tại \"{b.Listing?.Title}\". Nhận phòng sau {b.Listing?.CheckInFrom.ToString("HH:mm")}. " +
                "Nếu tới muộn, nhắn cho chủ nhà trước nhé.", ct);
            posted++;
        }

        // The lifecycle sweep closes a stay at local midnight on its check-out
        // day, so "still in progress" never matched: Completed counts too, and
        // "today" is the listing's own.
        var leaving = await db.Bookings
            .Where(b => (b.Status == BookingStatus.InProgress || b.Status == BookingStatus.Completed)
                        && b.CheckOut >= today.AddDays(-1) && b.CheckOut <= today.AddDays(1))
            .Include(b => b.Listing)
            .ToListAsync(ct);

        foreach (var b in leaving)
        {
            if (DateOnly.FromDateTime(BookingService.LocalNow(b.Listing!)) != b.CheckOut) continue;
            if (await AlreadyPostedAsync(b, "Hôm nay là ngày trả phòng", ct)) continue;
            await PostAsync(b,
                $"Hôm nay là ngày trả phòng, trước {b.Listing?.CheckOutBefore.ToString("HH:mm")}. Chúc bạn đi tiếp vui vẻ!", ct);
            posted++;
        }

        if (posted > 0) await db.SaveChangesAsync(ct);
        return posted;
    }

    private async Task<bool> AlreadyPostedAsync(Booking booking, string opening, CancellationToken ct)
    {
        if (booking.GuestUserId is not int guestId) return true;

        // Since this booking was made: a guest coming back to the same place
        // shares the thread with their earlier stay, whose milestones are there
        // already, and used to stop every reminder for the new one.
        return await db.Messages.AnyAsync(m =>
            m.IsSystem &&
            m.Thread!.ListingId == booking.ListingId &&
            m.Thread.GuestUserId == guestId &&
            m.SentAt >= booking.CreatedAt &&
            m.Body.StartsWith(opening), ct);
    }
}
