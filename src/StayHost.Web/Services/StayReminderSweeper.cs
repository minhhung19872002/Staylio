using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// docs/03 §11 — the three reminders around a stay:
///
/// | 7 ngày / 24 giờ trước ngày nhận | khách: nhắc + hướng dẫn | chủ nhà: nhắc chuẩn bị |
/// | Sáng ngày trả phòng              | khách: nhắc giờ trả     | —                      |
///
/// <see cref="NotificationKind.StayReminder"/> existed with no producer at all,
/// and the one message that tried (the thread line on check-out day) looked for
/// a stay still "in progress" on a day the lifecycle sweep had already closed at
/// local midnight, so it never went out either.
///
/// Days are the listing's own. Each reminder is sent once per booking: the
/// notification row carries the booking in its link, and that is what is
/// checked before sending again.
/// </summary>
public class StayReminderSweeper(
    StayHostDbContext db, NotificationService notifications, ILogger<StayReminderSweeper> log)
{
    /// <summary>Check-out reminders wait for the morning; nobody wants one at midnight.</summary>
    public const int CheckoutReminderHour = 7;

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var utcToday = DateOnly.FromDateTime(now);
        var sent = 0;

        // A day either side of UTC covers every time zone a listing can be in.
        var upcoming = await db.Bookings
            .Include(b => b.Listing!).ThenInclude(l => l.Host!).ThenInclude(h => h.User)
            .Where(b => b.Status == BookingStatus.Confirmed
                        && b.CheckIn >= utcToday && b.CheckIn <= utcToday.AddDays(8))
            .ToListAsync(ct);

        foreach (var b in upcoming)
        {
            var localToday = DateOnly.FromDateTime(BookingService.LocalNow(b.Listing!, now));
            var daysOut = b.CheckIn.DayNumber - localToday.DayNumber;

            var tag = daysOut switch
            {
                7 => "7d",
                1 => "1d",
                _ => null
            };
            if (tag is null) continue;

            var when = daysOut == 7 ? "Còn 7 ngày" : "Ngày mai";
            var title = daysOut == 7 ? "Còn 7 ngày tới chuyến đi của bạn" : "Ngày mai bạn nhận phòng";

            if (await SendOnceAsync(b.GuestUserId, $"/trips/{b.Id}?nhac={tag}", ct))
            {
                var guest = await db.Users.FirstOrDefaultAsync(u => u.Id == b.GuestUserId, ct);
                await notifications.QueueWithEmailAsync(guest, NotificationKind.StayReminder, title,
                    $"{when} là tới ngày nhận phòng tại \"{b.Listing!.Title}\" ({b.CheckIn:dd/MM}), " +
                    $"mã đặt chỗ {b.Reference}. Hướng dẫn nhận phòng có trong trang chuyến đi.",
                    $"/trips/{b.Id}?nhac={tag}", ct);
                sent++;
            }

            var host = b.Listing!.Host?.User;
            if (host is not null && await SendOnceAsync(host.Id, $"/hosting?tab=bookings&don={b.Id}&nhac={tag}", ct))
            {
                await notifications.QueueWithEmailAsync(host, NotificationKind.StayReminder,
                    daysOut == 7 ? "Còn 7 ngày tới lượt khách tiếp theo" : "Ngày mai có khách nhận phòng",
                    $"{b.GuestName} nhận phòng \"{b.Listing.Title}\" ngày {b.CheckIn:dd/MM} " +
                    $"({b.Nights} đêm, {b.Guests} khách), mã {b.Reference}. Hãy chuẩn bị chỗ nghỉ.",
                    $"/hosting?tab=bookings&don={b.Id}&nhac={tag}", ct);
                sent++;
            }
        }

        var leaving = await db.Bookings
            .Include(b => b.Listing)
            .Where(b => (b.Status == BookingStatus.InProgress || b.Status == BookingStatus.Completed)
                        && b.CheckOut >= utcToday.AddDays(-1) && b.CheckOut <= utcToday.AddDays(1))
            .ToListAsync(ct);

        foreach (var b in leaving)
        {
            var local = BookingService.LocalNow(b.Listing!, now);
            if (DateOnly.FromDateTime(local) != b.CheckOut || local.Hour < CheckoutReminderHour) continue;

            var link = $"/trips/{b.Id}?nhac=checkout";
            if (!await SendOnceAsync(b.GuestUserId, link, ct)) continue;

            var guest = await db.Users.FirstOrDefaultAsync(u => u.Id == b.GuestUserId, ct);
            var before = b.Listing!.CheckOutBefore.ToString("HH:mm");
            await notifications.QueueWithEmailAsync(guest, NotificationKind.StayReminder,
                "Hôm nay là ngày trả phòng",
                $"Nhớ trả phòng \"{b.Listing.Title}\" trước {before} hôm nay. Chúc bạn đi tiếp vui vẻ!",
                link, ct);
            sent++;
        }

        if (sent > 0)
        {
            await db.SaveChangesAsync(ct);
            log.LogInformation("Đã gửi {Count} lời nhắc chuyến đi.", sent);
        }

        return sent;
    }

    /// <summary>False when there is nobody to tell, or they were told already.</summary>
    private async Task<bool> SendOnceAsync(int? userId, string link, CancellationToken ct) =>
        userId is { } id
        && !await db.Notifications.AnyAsync(
            n => n.UserId == id && n.Kind == NotificationKind.StayReminder && n.Link == link, ct)
        && !db.Notifications.Local.Any(
            n => n.UserId == id && n.Kind == NotificationKind.StayReminder && n.Link == link);
}
