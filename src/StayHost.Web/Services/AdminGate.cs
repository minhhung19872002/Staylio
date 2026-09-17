using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// docs/08 §1 — the four principles, enforced in one place.
///
/// Every admin action goes through <see cref="AllowAsync"/>: it checks the
/// permission matrix of §2, insists on a reason where §1.4 does, and refuses
/// when §1.3's conflict rule applies. Doing this per-controller would mean
/// twenty chances to forget one of the four.
/// </summary>
public class AdminGate(StayHostDbContext db, AuthService auth, AdminAudit audit)
{
    /// <summary>Why an action was refused, or the admin who may do it.</summary>
    public sealed record Verdict(User? Admin, string? Refusal, int? Status = null)
    {
        public bool Ok => Admin is not null;
    }

    /// <param name="targetUserId">
    /// The account being acted on, when there is one. Supplying it is what turns
    /// on the §1.3 conflict check, so it is not optional in practice — a decision
    /// about somebody with no idea who they are cannot be checked.
    /// </param>
    public async Task<Verdict> AllowAsync(
        AdminAction action, string? reason, CancellationToken ct, int? targetUserId = null)
    {
        var admin = await auth.CurrentUserAsync(ct);

        if (admin is null || admin.Role != UserRole.Admin)
            return new Verdict(null, "Bạn cần đăng nhập bằng tài khoản quản trị.", 401);

        // docs/08 §3 — two-factor is a gate, not a nag.
        if (!AdminActions.MayHoldAdminSession(admin.TwoFactorEnabled))
            return new Verdict(null, AdminActions.TwoFactorRequiredMessage(), 403);

        if (!AdminActions.Allows(admin.AdminScope, action))
        {
            return new Verdict(null,
                $"{AdminActions.DeniedMessage(action)} Cần vai: {AdminActions.RequiredRoles(action)}.", 403);
        }

        if (AdminActions.RequiresReason(action) && !AdminActions.ReasonIsUsable(reason))
            return new Verdict(null, AdminActions.MissingReasonMessage(action), 400);

        if (targetUserId is { } target && AdminConflict.AppliesTo(action))
        {
            var conflict = await ConflictAsync(admin, target, ct);
            if (AdminConflict.Blocks(conflict))
                return new Verdict(null, AdminConflict.Message(conflict), 403);
        }

        // docs/08 §3 — the quarterly review needs to know who is still working.
        admin.AdminLastActiveAt = DateTime.UtcNow;

        // docs/08 §10 — the top role has no permission above it, so it gets a
        // witness instead: another Super is told about every decision a Super
        // makes. The row rides the caller's SaveChanges, like the audit row does.
        if (admin.AdminScope.HasFlag(AdminScope.Super) && AdminActions.Of(action).Decides)
        {
            var witness = await db.Users
                .Where(u => u.Id != admin.Id && u.Role == UserRole.Admin
                            && u.AdminScope.HasFlag(AdminScope.Super))
                .OrderBy(u => u.Id)
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync(ct);

            if (witness is { } witnessId)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = witnessId,
                    Kind = NotificationKind.System,
                    Title = "Quản trị tối cao vừa ra một quyết định",
                    Body = $"{admin.FullName}: {AdminActions.Of(action).Label.ToLowerInvariant()}"
                           + (targetUserId is { } t ? $" (người dùng #{t})" : "")
                           + (reason is { Length: > 0 } ? $". Lý do: {reason.Trim()}" : "."),
                    Link = "/admin"
                });
            }
        }

        return new Verdict(admin, null);
    }

    /// <summary>
    /// docs/08 §1.3 — "Hệ thống phải tự phát hiện và chặn." Everything here is
    /// read from data the platform already keeps, so it cannot be talked around.
    /// </summary>
    /// <summary>
    /// docs/08 §1.3 and §3 for decisions that are about a booking rather than an
    /// account: the admin must hold a two-factor session and must not be either
    /// party to it. Four money decisions — a Shield case, a resolution case, a
    /// price match and force majeure — checked the role and nothing else, so an
    /// arbitrator could rule on their own stay and pay themselves.
    /// </summary>
    public async Task<string?> PartyConflictAsync(
        User admin, int? guestUserId, int? hostUserId, CancellationToken ct)
    {
        if (!AdminActions.MayHoldAdminSession(admin.TwoFactorEnabled))
            return AdminActions.TwoFactorRequiredMessage();

        foreach (var party in new[] { guestUserId, hostUserId })
        {
            if (party is not { } id) continue;
            var conflict = await ConflictAsync(admin, id, ct);
            if (AdminConflict.Blocks(conflict)) return AdminConflict.Message(conflict);
        }

        return null;
    }

    public async Task<ConflictKind> ConflictAsync(User admin, int targetUserId, CancellationToken ct)
    {
        if (admin.Id == targetUserId) return ConflictKind.Self;

        var target = await db.Users
            .Where(u => u.Id == targetUserId)
            .Select(u => new { u.Id, u.Phone, u.HostProfileId })
            .FirstOrDefaultAsync(ct);

        if (target is null) return ConflictKind.None;

        // A booking with the admin on one side and this person on the other.
        var shareBooking = await db.Bookings.AnyAsync(
            bk => (bk.GuestUserId == admin.Id && bk.Listing!.Host!.UserId == targetUserId)
                  || (bk.GuestUserId == targetUserId && bk.Listing!.Host!.UserId == admin.Id), ct);

        if (shareBooking) return ConflictKind.SharedBooking;

        // Same phone number, or the same account the money goes to.
        var sharedContact = target.Phone is { Length: > 0 } && target.Phone == admin.Phone;

        if (!sharedContact && admin.HostProfileId is { } mine && target.HostProfileId is { } theirs)
        {
            var accounts = await db.Hosts
                .Where(h => h.Id == mine || h.Id == theirs)
                .Select(h => h.PayoutAccountLast4)
                .ToListAsync(ct);

            sharedContact = accounts.Count == 2
                            && accounts[0] is { Length: > 0 }
                            && accounts[0] == accounts[1];
        }

        if (sharedContact) return ConflictKind.LinkedAccount;

        var decidedBefore = await db.Sanctions.AnyAsync(
            s => s.UserId == targetUserId && s.DecidedByUserId == admin.Id, ct);

        return decidedBefore ? ConflictKind.AlreadyDecided : ConflictKind.None;
    }

    /// <summary>
    /// docs/08 §2 — the ✓* rows get their own line even though they only read.
    /// Called on top of the ordinary audit row, not instead of it.
    /// </summary>
    public void RecordSensitiveRead(User admin, AdminAction action, string target, string reason) =>
        audit.Record(admin, $"admin.read.{action}".ToLowerInvariant(), target, null, "đã xem", reason);

    /// <summary>docs/08 §10 — every profile opened, ticket or no ticket.</summary>
    public void RecordProfileView(User admin, int targetUserId, int? ticketId) =>
        db.AdminProfileViews.Add(new AdminProfileView
        {
            AdminUserId = admin.Id,
            TargetUserId = targetUserId,
            TicketId = ticketId
        });
}
