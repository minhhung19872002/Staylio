using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

public record LoyaltyDto(int Level, string Label, int CompletedStays, int? StaysToNext, string? NextLabel);

/// <summary>
/// Staylio Thân thiết levels for a person. The viewer's level is looked up at
/// most once per request, and is 0 wherever there is no request — a background
/// sweep re-pricing a booking must use the percent frozen on it, never whoever
/// happens to be "viewing".
/// </summary>
public class LoyaltyService(StayHostDbContext db, IHttpContextAccessor http, IServiceProvider services)
{
    private int? _viewerLevel;

    public async Task<int> CompletedStaysAsync(int userId, CancellationToken ct)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-Loyalty.WindowDays);
        return await db.Bookings.CountAsync(b =>
            b.GuestUserId == userId && b.Status == BookingStatus.Completed && b.CheckOut >= since, ct);
    }

    public async Task<int> LevelForAsync(int? userId, CancellationToken ct) =>
        userId is { } id ? Loyalty.LevelFor(true, await CompletedStaysAsync(id, ct)) : 0;

    public async Task<int> ViewerLevelAsync(CancellationToken ct)
    {
        if (_viewerLevel is { } known) return known;
        if (http.HttpContext is null) return (_viewerLevel = 0).Value;
        var user = await services.GetRequiredService<AuthService>().CurrentUserAsync(ct);
        return (_viewerLevel = await LevelForAsync(user?.Id, ct)).Value;
    }

    public async Task<LoyaltyDto> SummaryAsync(int userId, CancellationToken ct)
    {
        var stays = await CompletedStaysAsync(userId, ct);
        var level = Loyalty.LevelFor(true, stays);
        return new LoyaltyDto(level, Loyalty.Labels[level], stays,
            Loyalty.StaysToNext(level, stays),
            level < 3 ? Loyalty.Labels[level + 1] : null);
    }
}
