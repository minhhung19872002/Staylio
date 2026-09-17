using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// Password auth with PBKDF2 + per-user salt, and opaque session tokens kept in an
/// HttpOnly cookie. No JWT: revoking a session is a single row update.
/// </summary>
public class AuthService(StayHostDbContext db, IHttpContextAccessor accessor)
{
    public const string CookieName = "sh_auth";

    public sealed record AuthResult(
        bool Ok, string? Error = null, User? User = null,
        /// <summary>
        /// docs/01 TK-08 — set when the password was right but a second factor
        /// is still owed. No session exists yet; this token only says which
        /// account is halfway in.
        /// </summary>
        string? TwoFactorChallenge = null);

    private HttpContext Ctx => accessor.HttpContext
        ?? throw new InvalidOperationException("No active HTTP context.");

    /* ------------------------------------------------------------ register */

    /// <summary>
    /// docs/01 TK-01 — either an email or a phone number is enough, and TK-03
    /// puts an age gate in front of both.
    /// </summary>
    public async Task<AuthResult> RegisterAsync(
        string? email, string password, string fullName, string? phone, CancellationToken ct,
        DateOnly? dateOfBirth = null)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        var normalisedPhone = Identity.NormalisePhone(phone);

        var taken =
            (email.Length > 0 && await db.Users.AnyAsync(u => u.Email == email, ct))
            || (normalisedPhone is not null && await db.Users.AnyAsync(u => u.Phone == normalisedPhone, ct));

        // docs/08 §5.4 — a permanent ban is a closed door, not a name change.
        // Checked before the ordinary validation so the refusal stays vague:
        // "Email này đã được đăng ký" would both confirm the account exists and
        // point at exactly which detail to change next time.
        if (await IsBannedComebackAsync(email.Length > 0 ? email : null, normalisedPhone, ct))
            return new(false, BannedComebackMessage());

        var check = Identity.CanRegister(
            email.Length > 0 ? email : null,
            string.IsNullOrWhiteSpace(phone) ? null : phone,
            password, fullName, dateOfBirth, DateOnly.FromDateTime(DateTime.UtcNow), taken);

        if (!check.Ok) return new(false, check.Message);

        var (hash, salt) = PasswordHasher.Hash(password);
        var user = new User
        {
            Email = email,
            FullName = fullName.Trim(),
            Initials = MakeInitials(fullName),
            Phone = normalisedPhone,
            DateOfBirth = dateOfBirth,
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = UserRole.Guest,
            AdoptedSessionId = Ctx.SessionId()
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        await AdoptAnonymousDataAsync(user, ct);
        await IssueSessionAsync(user, ct);
        return new(true, null, user);
    }

    /* --------------------------------------------------------------- login */

    /// <summary>docs/01 TK-01 — people sign in with whichever one they gave us.</summary>
    /// <summary>
    /// docs/08 §5.3 and §6 — the message a locked-out account gets. Somebody with
    /// an open dispute keeps a way in, because §6 refuses to let a lock decide a
    /// case by silencing one side of it.
    /// </summary>
    private static string? LockedOut(User user)
    {
        if (user.IsBanned)
        {
            return "Tài khoản này đã bị khoá vĩnh viễn. " +
                   "Nếu bạn cho rằng đây là nhầm lẫn, hãy liên hệ hỗ trợ Staylio.";
        }

        if (!user.IsSuspended) return null;

        if (user.MayStillRespondToDisputes) return null;

        var until = user.SuspendedUntil is { } at
            ? $" tới {at:HH:mm dd/MM/yyyy}"
            : "";

        return $"Tài khoản này đang tạm khoá{until}. " +
               "Bạn có thể khiếu nại quyết định này — hãy kiểm tra email chúng tôi đã gửi.";
    }

    /// <summary>
    /// The same lock, checked at every door. External sign-in has no password
    /// step, but docs/08 §5.3 does not say "khoá, trừ khi đăng nhập bằng Google".
    /// </summary>
    public static string? LockedOutMessage(User user) => LockedOut(user);

    /* ------------------------------------------------ §5.4, banned comebacks */

    /// <summary>
    /// docs/08 §5.4 — a ban blocks the same email, phone or browser from coming
    /// back under a new name. The browser signal is the anonymous session cookie
    /// the banned account once adopted — not the user-agent string, which half
    /// the country shares. The refusal is deliberately vague: confirming a ban
    /// to whoever is probing is confirming the account exists.
    /// </summary>
    public async Task<bool> IsBannedComebackAsync(string? email, string? phone, CancellationToken ct)
    {
        var sid = Ctx.SessionId();
        var hasEmail = !string.IsNullOrEmpty(email);
        var hasSid = !string.IsNullOrEmpty(sid);

        return await db.Users.AnyAsync(u => u.IsBanned
            && ((hasEmail && u.Email == email)
                || (phone != null && u.Phone == phone)
                || (hasSid && u.AdoptedSessionId == sid)), ct);
    }

    public static string BannedComebackMessage() =>
        "Không thể tạo tài khoản với thông tin này. " +
        "Nếu bạn cho rằng có nhầm lẫn, hãy liên hệ hỗ trợ Staylio.";

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct)
    {
        var typed = (email ?? "").Trim();
        var asEmail = typed.ToLowerInvariant();
        var asPhone = Identity.NormalisePhone(typed);

        var user = asPhone is not null
            ? await db.Users.FirstOrDefaultAsync(u => u.Phone == asPhone, ct)
            : await db.Users.FirstOrDefaultAsync(u => u.Email == asEmail, ct);

        // Same message either way so the endpoint does not leak which accounts exist.
        // An external-only account has no password hash to verify against.
        if (user is null
            || string.IsNullOrEmpty(user.PasswordHash)
            || !PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
            return new(false, "Email, số điện thoại hoặc mật khẩu không đúng.");

        // docs/08 §5.3 — a locked account does not sign in. Checked after the
        // password so a wrong password still reads as a wrong password: telling a
        // stranger that an account is suspended is telling them it exists.
        if (LockedOut(user) is { } locked) return new(false, locked);

        // docs/08 §3 — an admin without two-factor cannot hold a session at all,
        // and this is the only place that can be enforced.
        if (user.Role == UserRole.Admin && !AdminActions.MayHoldAdminSession(user.TwoFactorEnabled))
            return new(false, AdminActions.TwoFactorRequiredMessage());

        // docs/01 TK-08 — the password alone does not open the door. Nothing is
        // adopted and no session is issued until the code comes back.
        if (user.TwoFactorEnabled)
            return new(true, null, user, await IssueChallengeAsync(user, ct));

        await AdoptAnonymousDataAsync(user, ct);
        await IssueSessionAsync(user, ct);
        return new(true, null, user);
    }

    /// <summary>
    /// docs/01 TK-08 — a short-lived, single-use token naming the half-signed-in
    /// account. It grants nothing on its own: <see cref="RedeemChallengeAsync"/>
    /// is the only thing that reads it, and only alongside a correct code.
    /// </summary>
    public async Task<string> IssueChallengeAsync(User user, CancellationToken ct)
    {
        // Any older challenge for this account stops working the moment a new
        // one is issued, so a stale tab cannot be used to finish a login.
        var stale = await db.UserTokens
            .Where(t => t.UserId == user.Id && t.Purpose == TokenPurpose.TwoFactorChallenge && t.UsedAt == null)
            .ToListAsync(ct);
        foreach (var t in stale) t.UsedAt = DateTime.UtcNow;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        db.UserTokens.Add(new UserToken
        {
            Token = token,
            UserId = user.Id,
            Purpose = TokenPurpose.TwoFactorChallenge,
            ExpiresAt = DateTime.UtcNow + Identity.CodeLifetime
        });
        await db.SaveChangesAsync(ct);
        return token;
    }

    /// <summary>Reads a live challenge without spending it; the code still has to be right.</summary>
    public async Task<User?> ReadChallengeAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var entry = await db.UserTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token && t.Purpose == TokenPurpose.TwoFactorChallenge, ct);

        if (entry is null || entry.UsedAt is not null || entry.ExpiresAt < DateTime.UtcNow) return null;
        return entry.User;
    }

    /// <summary>Spends the challenge and starts the session it was standing in for.</summary>
    public async Task<AuthResult> RedeemChallengeAsync(string token, CancellationToken ct)
    {
        var entry = await db.UserTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token && t.Purpose == TokenPurpose.TwoFactorChallenge, ct);

        if (entry is null || entry.UsedAt is not null || entry.ExpiresAt < DateTime.UtcNow)
            return new(false, "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");

        entry.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await AdoptAnonymousDataAsync(entry.User!, ct);
        await IssueSessionAsync(entry.User!, ct);
        return new(true, null, entry.User);
    }

    /// <summary>Starts a session for somebody a provider just vouched for (docs/01 TK-02).</summary>
    public Task SignInAsync(User user, CancellationToken ct) => IssueSessionAsync(user, ct);

    /// <summary>
    /// docs/01 TK-08, docs/08 §3 — every door that proves who somebody is, other
    /// than the password form, still owes the second factor. A reset link or a
    /// Google account used to open a full session on an account with two-factor
    /// on — the admin console included.
    /// </summary>
    public async Task<AuthResult> SignInOrChallengeAsync(User user, CancellationToken ct)
    {
        if (user.TwoFactorEnabled)
            return new(true, null, user, await IssueChallengeAsync(user, ct));

        await IssueSessionAsync(user, ct);
        return new(true, null, user);
    }

    /// <summary>
    /// docs/01 TK-02 — an account created from a Google, Apple or Facebook
    /// identity. There is no password: the provider is the way in, and the
    /// account has to set one before unlinking the last provider.
    /// </summary>
    public async Task<User> CreateExternalUserAsync(string email, string fullName, CancellationToken ct)
    {
        var user = new User
        {
            Email = email,
            FullName = fullName.Trim(),
            Initials = MakeInitials(fullName),
            // The provider already proved the address it handed over.
            EmailConfirmed = email.Length > 0,
            PasswordHash = "",
            PasswordSalt = "",
            Role = UserRole.Guest,
            AdoptedSessionId = Ctx.SessionId()
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        await AdoptAnonymousDataAsync(user, ct);

        return user;
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        var token = Ctx.Request.Cookies[CookieName];
        if (!string.IsNullOrEmpty(token))
        {
            var session = await db.AuthSessions.FirstOrDefaultAsync(s => s.Token == token, ct);
            if (session is not null)
            {
                session.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        Ctx.Response.Cookies.Delete(CookieName);
    }

    /* ------------------------------------------------------------ sessions */

    private async Task IssueSessionAsync(User user, CancellationToken ct)
    {
        // docs/01 TK-12 — a pause ends by coming back, and this is the one place
        // every way in passes through: password, the second factor, and each of
        // the three external providers. Hooking the password path alone would
        // leave somebody who signs in with Google paused forever.
        await Controllers.AccountController.ResumeAsync(db, user, ct);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        db.AuthSessions.Add(new AuthSession
        {
            Token = token,
            UserId = user.Id,
            UserAgent = Truncate(Ctx.Request.Headers.UserAgent.ToString(), 300),
            IpAddress = Truncate(Ctx.Connection.RemoteIpAddress?.ToString() ?? "", 60),
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync(ct);

        Ctx.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Ctx.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });
    }

    /// <summary>Resolves the signed-in user for this request, or null when anonymous.</summary>
    public async Task<User?> CurrentUserAsync(CancellationToken ct = default)
    {
        if (Ctx.Items.TryGetValue("__sh_user", out var cached)) return cached as User;

        User? user = null;
        var token = Ctx.Request.Cookies[CookieName];
        if (!string.IsNullOrEmpty(token))
        {
            var session = await db.AuthSessions
                .Include(s => s.User!).ThenInclude(u => u.HostProfile)
                .FirstOrDefaultAsync(s => s.Token == token, ct);

            if (session is not null && session.RevokedAt is null && session.ExpiresAt > DateTime.UtcNow)
            {
                // docs/08 §3 QT-A — an admin session idles out after 30 minutes;
                // everyone else keeps the flat 30-day cookie. ExecuteUpdate, not
                // the change tracker: this runs before the request's real work
                // and must not drag other tracked entities into an early save.
                if (session.User is { Role: UserRole.Admin })
                {
                    var now = DateTime.UtcNow;
                    var lastSeen = session.LastSeenAt ?? session.CreatedAt;

                    if (AdminActions.SessionExpired(lastSeen, now))
                    {
                        await db.AuthSessions.Where(s => s.Id == session.Id && s.RevokedAt == null)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
                    }
                    else
                    {
                        // One write a minute, not one per request.
                        if (now - lastSeen > TimeSpan.FromMinutes(1))
                        {
                            await db.AuthSessions.Where(s => s.Id == session.Id)
                                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), ct);
                        }
                        user = session.User;
                    }
                }
                else user = session.User;
            }
        }

        Ctx.Items["__sh_user"] = user;
        return user;
    }

    /* ------------------------------------------------------------ adoption */

    /// <summary>
    /// Wishlist items and bookings made before signing in belong to a cookie, not an
    /// account. On login we move them across so nothing appears lost.
    /// </summary>
    private async Task AdoptAnonymousDataAsync(User user, CancellationToken ct)
    {
        var sid = Ctx.SessionId();
        if (string.IsNullOrEmpty(sid)) return;

        var alreadyOwned = await db.Favorites
            .Where(f => f.UserId == user.Id)
            .Select(f => f.ListingId)
            .ToListAsync(ct);

        var orphanFavorites = await db.Favorites
            .Where(f => f.SessionId == sid && f.UserId == null)
            .ToListAsync(ct);

        foreach (var fav in orphanFavorites)
        {
            if (alreadyOwned.Contains(fav.ListingId)) db.Favorites.Remove(fav);
            else fav.UserId = user.Id;
        }

        var orphanLists = await db.Wishlists
            .Where(w => w.SessionId == sid && w.UserId == null)
            .ToListAsync(ct);
        var hadLists = await db.Wishlists.AnyAsync(w => w.UserId == user.Id, ct);
        foreach (var list in orphanLists)
        {
            list.UserId = user.Id;
            // The account's existing default wins over the anonymous one.
            if (hadLists) list.IsDefault = false;
        }

        var orphanBookings = await db.Bookings
            .Where(b => b.SessionId == sid && b.GuestUserId == null)
            .ToListAsync(ct);
        foreach (var booking in orphanBookings) booking.GuestUserId = user.Id;

        await db.SaveChangesAsync(ct);
    }

    /* ------------------------------------------------- password management */

    public async Task<AuthResult> ChangePasswordAsync(User user, string current, string next, CancellationToken ct)
    {
        if (!PasswordHasher.Verify(current, user.PasswordHash, user.PasswordSalt))
            return new(false, "Mật khẩu hiện tại không đúng.");
        if (next.Length < 8)
            return new(false, "Mật khẩu mới cần tối thiểu 8 ký tự.");

        var (hash, salt) = PasswordHasher.Hash(next);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;

        // Changing the password logs every other device out.
        var currentToken = Ctx.Request.Cookies[CookieName];
        var others = await db.AuthSessions
            .Where(s => s.UserId == user.Id && s.RevokedAt == null && s.Token != currentToken)
            .ToListAsync(ct);
        foreach (var s in others) s.RevokedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return new(true, null, user);
    }

    /// <summary>
    /// Issues a reset token. Returns null when the email is unknown — the caller still
    /// reports success so the endpoint cannot be used to enumerate accounts.
    /// </summary>
    public async Task<string?> BeginPasswordResetAsync(string email, CancellationToken ct)
    {
        email = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null) return null;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        db.UserTokens.Add(new UserToken
        {
            Token = token,
            UserId = user.Id,
            Purpose = TokenPurpose.PasswordReset,
            ExpiresAt = DateTime.UtcNow.AddHours(2)
        });
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<AuthResult> CompletePasswordResetAsync(string token, string newPassword, CancellationToken ct)
    {
        if (newPassword.Length < 8) return new(false, "Mật khẩu mới cần tối thiểu 8 ký tự.");

        var entry = await db.UserTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token && t.Purpose == TokenPurpose.PasswordReset, ct);

        if (entry is null || entry.UsedAt is not null || entry.ExpiresAt < DateTime.UtcNow)
            return new(false, "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");

        var (hash, salt) = PasswordHasher.Hash(newPassword);
        entry.User!.PasswordHash = hash;
        entry.User.PasswordSalt = salt;
        entry.UsedAt = DateTime.UtcNow;

        // A reset invalidates every existing session.
        var sessions = await db.AuthSessions
            .Where(s => s.UserId == entry.UserId && s.RevokedAt == null).ToListAsync(ct);
        foreach (var s in sessions) s.RevokedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return await SignInOrChallengeAsync(entry.User, ct);
    }

    /* --------------------------------------------------------- verification */

    public async Task<string> BeginEmailVerificationAsync(User user, CancellationToken ct)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        db.UserTokens.Add(new UserToken
        {
            Token = token,
            UserId = user.Id,
            Purpose = TokenPurpose.EmailVerification,
            ExpiresAt = DateTime.UtcNow.AddDays(3)
        });
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<AuthResult> ConfirmEmailAsync(string token, CancellationToken ct)
    {
        var entry = await db.UserTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token && t.Purpose == TokenPurpose.EmailVerification, ct);

        if (entry is null || entry.UsedAt is not null || entry.ExpiresAt < DateTime.UtcNow)
            return new(false, "Liên kết xác minh không hợp lệ hoặc đã hết hạn.");

        entry.User!.EmailConfirmed = true;
        entry.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new(true, null, entry.User);
    }

    /* ------------------------------------------------------------- devices */

    public Task<List<AuthSession>> ActiveSessionsAsync(int userId, CancellationToken ct) =>
        db.AuthSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public string? CurrentToken() => Ctx.Request.Cookies[CookieName];

    public async Task<bool> RevokeSessionAsync(int userId, int sessionId, CancellationToken ct)
    {
        var session = await db.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null) return false;

        session.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /* ---------------------------------------------------------- host setup */

    /// <summary>Creates the host profile the first time a user publishes a listing.</summary>
    public async Task<HostProfile> EnsureHostProfileAsync(User user, CancellationToken ct)
    {
        var existing = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == user.Id, ct);
        if (existing is not null) return existing;

        var host = new HostProfile
        {
            Name = user.FullName,
            Initials = user.Initials,
            Bio = user.Bio,
            IsSuperhost = false,
            YearsHosting = 0,
            JoinedAt = user.CreatedAt,
            UserId = user.Id
        };
        db.Hosts.Add(host);

        if (user.Role == UserRole.Guest) user.Role = UserRole.Host;
        await db.SaveChangesAsync(ct);
        return host;
    }

    public static string MakeInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "??";
        var letters = parts.Select(p => p[0]).ToArray();
        var take = Math.Min(2, letters.Length);
        return new string(letters[^take..]).ToUpperInvariant();
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max];
}
