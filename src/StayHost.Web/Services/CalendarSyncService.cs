using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// docs/01 QL-10 — keeps this calendar and the ones the host runs elsewhere out
/// of each other's way. Import turns another site's events into blocks; export
/// hands ours over in the same format.
/// </summary>
public class CalendarSyncService(
    StayHostDbContext db, IHttpClientFactory http, ILogger<CalendarSyncService> log,
    NotificationService notifications)
{
    /// <summary>How often a feed is refreshed on its own, without the host asking.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>Nothing further out than this is worth carrying around.</summary>
    private const int HorizonDays = 400;

    public async Task<CalendarFeed> SyncAsync(CalendarFeed feed, CancellationToken ct)
    {
        try
        {
            var client = http.CreateClient("ical");
            client.Timeout = TimeSpan.FromSeconds(20);

            var text = await client.GetStringAsync(feed.Url, ct);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var horizon = today.AddDays(HorizonDays);

            var events = Ical.Read(text)
                .Where(e => e.To > today && e.From < horizon)
                .ToList();

            await ApplyAsync(feed, events, ct);

            // docs/01 QL-11 — warn when the import sells nights Staylio has
            // already confirmed. The blocks are still written (the dates really are
            // taken elsewhere); the host is told so they can sort out the clash.
            await FlagOverlapsAsync(feed, events, ct);

            feed.EventCount = events.Count;
            feed.LastError = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Calendar feed {FeedId} failed", feed.Id);
            // Keep the blocks from the last good sync: losing them would open
            // dates that are, as far as anyone knows, still taken.
            feed.LastError = Summarise(ex);
        }

        feed.LastSyncedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return feed;
    }

    /// <summary>
    /// Replaces exactly this feed's blocks and nothing else — a host's own
    /// closed days are not the import's to remove.
    /// </summary>
    private async Task ApplyAsync(CalendarFeed feed, IReadOnlyList<IcalEvent> events, CancellationToken ct)
    {
        var existing = await db.CalendarBlocks
            .Where(b => b.FeedId == feed.Id)
            .ToListAsync(ct);

        var byUid = existing
            .Where(b => b.ExternalUid is not null)
            .GroupBy(b => b.ExternalUid!)
            .ToDictionary(g => g.Key, g => g.First());

        var seen = new HashSet<string>();

        foreach (var e in events)
        {
            seen.Add(e.Uid);

            // Blocks name their last blocked day; iCalendar's DTEND is the day
            // after, which is the guest's check-out.
            var lastNight = e.To.AddDays(-1);

            if (byUid.TryGetValue(e.Uid, out var block))
            {
                block.From = e.From;
                block.To = lastNight;
                block.Note = Note(feed, e);
                continue;
            }

            db.CalendarBlocks.Add(new CalendarBlock
            {
                ListingId = feed.ListingId,
                FeedId = feed.Id,
                ExternalUid = e.Uid,
                From = e.From,
                To = lastNight,
                Note = Note(feed, e)
            });
        }

        db.CalendarBlocks.RemoveRange(
            existing.Where(b => b.ExternalUid is null || !seen.Contains(b.ExternalUid)));
    }

    /// <summary>
    /// docs/01 QL-11 — records a warning on the feed, and notifies the host once
    /// when a clash first appears, so a re-sync of the same clash does not nag.
    /// </summary>
    private async Task FlagOverlapsAsync(CalendarFeed feed, IReadOnlyList<IcalEvent> events, CancellationToken ct)
    {
        var confirmed = await db.Bookings
            .Where(b => b.ListingId == feed.ListingId && BookingLifecycle.BlocksDates.Contains(b.Status))
            .Select(b => new CalendarConflicts.Range(b.CheckIn, b.CheckOut))
            .ToListAsync(ct);

        var clashes = CalendarConflicts.Clashes(
            events.Select(e => new CalendarConflicts.Range(e.From, e.To)), confirmed);

        var warning = CalendarConflicts.Warn(clashes);
        var isNew = warning is not null && feed.OverlapWarning is null;
        feed.OverlapWarning = warning;

        if (isNew)
        {
            var host = await db.Users.FirstOrDefaultAsync(u => u.HostProfile!.Id ==
                db.Listings.Where(l => l.Id == feed.ListingId).Select(l => l.HostId).First(), ct);
            var title = await db.Listings.Where(l => l.Id == feed.ListingId).Select(l => l.Title).FirstAsync(ct);
            await notifications.QueueWithEmailAsync(host, NotificationKind.System,
                "Lịch nhập về trùng đơn đã xác nhận",
                $"{feed.Label} trên \"{title}\": {warning}", "/hosting", ct);
        }
    }

    /// <summary>Everything this listing has sold or closed, as an iCalendar file.</summary>
    public async Task<string> ExportAsync(Listing listing, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);

        var stays = await db.Bookings
            .Where(b => b.ListingId == listing.Id
                        && BookingLifecycle.BlocksDates.Contains(b.Status)
                        && b.CheckOut > today)
            .Select(b => new { b.Id, b.Reference, b.CheckIn, b.CheckOut })
            .ToListAsync(ct);

        // Only the host's own closed days go out: sending an imported block back
        // to the platform it came from would have the two feeding each other.
        var blocks = await db.CalendarBlocks
            .Where(b => b.ListingId == listing.Id && b.FeedId == null && b.To >= today)
            .Select(b => new { b.Id, b.From, b.To, b.Note })
            .ToListAsync(ct);

        var events = stays
            .Select(s => new IcalEvent(
                $"booking-{s.Id}@stayhost", s.CheckIn, s.CheckOut, $"Đã đặt · {s.Reference}"))
            .Concat(blocks.Select(b => new IcalEvent(
                $"block-{b.Id}@stayhost", b.From, b.To.AddDays(1), b.Note ?? "Chủ nhà khoá")))
            .OrderBy(e => e.From)
            .ToList();

        return Ical.Write($"Staylio · {listing.Title}", events, DateTime.UtcNow);
    }

    private static string Note(CalendarFeed feed, IcalEvent e) =>
        string.IsNullOrWhiteSpace(e.Summary) ? feed.Label : $"{feed.Label}: {e.Summary}";

    private static string Summarise(Exception ex)
    {
        // The host typed the address, so a refused one is said plainly; any other
        // network failure keeps to a status code and says nothing about what
        // answered on the other end.
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is Infrastructure.NotPublicAddressException refused) return refused.Message;

        return ex is HttpRequestException http
            ? $"Không tải được lịch ({(int?)http.StatusCode ?? 0})."
            : ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
    }
}

/// <summary>Refreshes every feed on a slow loop so a host never has to press anything.</summary>
public class CalendarSyncWorker(IServiceProvider services, ILogger<CalendarSyncWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Let the app finish starting before reaching out to anyone else's server.
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));

        do
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<StayHostDbContext>();
                var sync = scope.ServiceProvider.GetRequiredService<CalendarSyncService>();

                var due = DateTime.UtcNow - CalendarSyncService.Interval;
                var feeds = await db.CalendarFeeds
                    .Where(f => f.LastSyncedAt == null || f.LastSyncedAt < due)
                    .Take(20)
                    .ToListAsync(ct);

                foreach (var feed in feeds) await sync.SyncAsync(feed, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                log.LogError(ex, "Calendar sync sweep failed");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
