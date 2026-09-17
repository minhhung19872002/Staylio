using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// docs/08 §5.2–§5.3 — a restriction or a suspension "có thời hạn" ends on its
/// own when that time is up.
///
/// Nothing did that. The expiry date was written on the sanction and on the
/// account, <see cref="Sanction.IsActive"/> knew how to read it, and no code
/// ever asked: a seven-day lock kept the account shut, its listings hidden and
/// its payouts held until an admin happened to press "khôi phục".
///
/// The account's flags are rebuilt from whatever sanctions are still in force,
/// so an overlapping longer sanction keeps working when a shorter one ends.
/// </summary>
public class SanctionExpiry(
    StayHostDbContext db, NotificationService notifications, ILogger<SanctionExpiry> log)
{
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var lapsed = await db.Sanctions
            .Where(s => s.LiftedAt == null && !s.OverturnedOnAppeal && s.ExpiresAt != null && s.ExpiresAt <= now)
            .ToListAsync(ct);
        if (lapsed.Count == 0) return 0;

        foreach (var s in lapsed)
        {
            s.LiftedAt = now;
            s.LiftedReason = "Hết thời hạn.";
        }
        await db.SaveChangesAsync(ct);

        foreach (var userId in lapsed.Select(s => s.UserId).Distinct())
        {
            var user = await db.Users.Include(u => u.HostProfile).FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) continue;

            await RebuildAsync(user, now, ct);

            await notifications.QueueWithEmailAsync(user, NotificationKind.System,
                "Hình thức xử lý đã hết thời hạn",
                user.IsSuspended || user.RestrictionMask != 0
                    ? "Một hình thức xử lý trên tài khoản của bạn đã hết thời hạn. Các hình thức khác vẫn còn hiệu lực."
                    : "Tài khoản Staylio của bạn đã hoạt động bình thường trở lại.",
                "/", ct);
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Đã gỡ {Count} hình thức xử lý hết hạn.", lapsed.Count);
        return lapsed.Count;
    }

    /// <summary>The account as the sanctions still in force say it should be.</summary>
    private async Task RebuildAsync(User user, DateTime now, CancellationToken ct)
    {
        var active = (await db.Sanctions
                .Where(s => s.UserId == user.Id && s.LiftedAt == null && !s.OverturnedOnAppeal)
                .ToListAsync(ct))
            .Where(s => s.IsActive(now))
            .ToList();

        var locks = active.Where(s => s.Level is SanctionLevel.Suspension or SanctionLevel.Ban).ToList();

        user.IsBanned = locks.Any(s => s.Level == SanctionLevel.Ban);
        user.IsSuspended = locks.Count > 0;
        user.SuspendedUntil = locks.Count == 0 || locks.Any(s => s.ExpiresAt is null)
            ? null
            : locks.Max(s => s.ExpiresAt);
        if (!user.IsSuspended) user.MayStillRespondToDisputes = false;

        user.RestrictionMask = active
            .Where(s => s.Level == SanctionLevel.Restriction && s.Restriction is not null)
            .Aggregate(0, (mask, s) => mask | (1 << (int)s.Restriction!.Value));

        if (user.HostProfile is not { } host) return;

        // The same two things Restore gives back, and only once nothing still in
        // force asks for them.
        if (!user.IsSuspended && !Restrictions.Has(user.RestrictionMask, RestrictionKind.ListingsHiddenFromSearch))
        {
            foreach (var l in await db.Listings
                         .Where(l => l.HostId == host.Id && l.HiddenBySanctionAt != null)
                         .ToListAsync(ct))
            {
                l.IsPublished = true;
                l.HiddenBySanctionAt = null;
            }
        }

        if (!user.IsSuspended && !Restrictions.Has(user.RestrictionMask, RestrictionKind.PayoutsHeld))
        {
            foreach (var p in await db.Payments
                         .Where(p => p.PayoutStatus == PayoutStatus.OnHold
                                     && p.PayoutHoldReason == PayoutHoldReason.AccountUnderReview
                                     && p.Booking!.Listing!.HostId == host.Id)
                         .ToListAsync(ct))
            {
                p.PayoutStatus = PayoutStatus.Scheduled;
                p.PayoutHoldReason = PayoutHoldReason.None;
            }
        }
    }
}
