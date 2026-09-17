using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/01 TK-11 — "tải toàn bộ dữ liệu cá nhân của tôi".
///
/// One file, downloaded there and then, rather than a job that emails a link
/// later: the whole point is that somebody can see what is held about them
/// without asking anyone. Nothing here is summarised or prettied up — a receipt
/// the platform still holds is worth more to them than a nicer table.
/// </summary>
[ApiController]
[Route("api/account/data")]
public class AccountDataController(StayHostDbContext db, AuthService auth) : ControllerBase
{
    private static readonly JsonSerializerOptions Pretty = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        return File(await BuildAsync(user, ct), "application/json", FileNameFor(DateTime.UtcNow));
    }

    /// <summary>
    /// docs/08 §9 — the same file, reached by the time-limited link an admin
    /// issued instead of by being signed in. The token is the whole credential,
    /// so it is long, single-purpose and expires; anonymous by design, because
    /// somebody who asked for their data may no longer be able to sign in.
    /// </summary>
    [HttpGet("download/{token}")]
    public async Task<IActionResult> Download(string token, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var request = await db.DataRequests
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.LinkToken == token && r.Kind == DataRequestKind.Export, ct);

        if (request?.User is null || request.LinkExpiresAt is null || request.LinkExpiresAt <= now)
            return NotFound(new { message = "Liên kết tải dữ liệu không còn hiệu lực. Hãy yêu cầu lại." });

        return File(await BuildAsync(request.User, ct), "application/json", FileNameFor(now));
    }

    private static string FileNameFor(DateTime at) => $"stayhost-du-lieu-ca-nhan-{at:yyyy-MM-dd}.json";

    private async Task<byte[]> BuildAsync(User user, CancellationToken ct)
    {
        var host = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == user.Id, ct);

        var bookings = await db.Bookings
            .Where(b => b.GuestUserId == user.Id)
            .OrderBy(b => b.CheckIn)
            .Select(b => new
            {
                b.Reference, b.CheckIn, b.CheckOut, b.Nights, b.Guests,
                Listing = b.Listing!.Title,
                b.Listing.City,
                Status = b.Status.ToString(),
                b.Subtotal, b.CleaningFee, b.ServiceFee, b.Tax, b.Total,
                b.RefundedAmount, b.GoodwillCredit, b.GuestNote, b.CreatedAt
            })
            .ToListAsync(ct);

        var reviewsWritten = await db.Reviews
            .Where(r => r.AuthorUserId == user.Id)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new
            {
                Listing = r.Listing!.Title,
                r.Rating, r.Text, r.PrivateNote, r.CreatedAt, r.PublishedAt
            })
            .ToListAsync(ct);

        var reviewsReceived = await db.GuestReviews
            .Where(r => r.GuestUserId == user.Id && r.PublishedAt != null)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Rating, r.Text, r.WouldHostAgain, r.CreatedAt })
            .ToListAsync(ct);

        var messages = await db.Messages
            .Where(m => m.Thread!.GuestUserId == user.Id || m.Thread.HostUserId == user.Id)
            .OrderBy(m => m.SentAt)
            .Select(m => new
            {
                Thread = m.Thread!.Listing!.Title,
                Mine = m.SenderUserId == user.Id,
                m.Body, m.SentAt, m.ReadAt
            })
            .ToListAsync(ct);

        // docs/00 §6.8 — the balance is the sum of its rows, so the rows are
        // what somebody is owed an answer about.
        var credits = await db.CreditEntries
            .Where(c => c.UserId == user.Id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { c.Amount, Reason = c.Reason.ToString(), c.Memo, c.CreatedAt })
            .ToListAsync(ct);

        var notifications = await db.Notifications
            .Where(n => n.UserId == user.Id)
            .OrderBy(n => n.CreatedAt)
            .Select(n => new { Kind = n.Kind.ToString(), n.Title, n.Body, n.CreatedAt, n.ReadAt })
            .ToListAsync(ct);

        var sessions = await db.AuthSessions
            .Where(s => s.UserId == user.Id)
            .OrderBy(s => s.CreatedAt)
            .Select(s => new { s.UserAgent, s.CreatedAt, s.ExpiresAt, s.RevokedAt })
            .ToListAsync(ct);

        var logins = await db.ExternalLogins
            .Where(e => e.UserId == user.Id)
            .Select(e => new { Provider = e.Provider.ToString(), e.ProviderEmail, e.CreatedAt, e.LastUsedAt })
            .ToListAsync(ct);

        var claims = await db.ShieldClaims
            .Where(c => c.OpenedByUserId == user.Id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                c.Reference, Side = c.Side.ToString(), Kind = c.Kind.ToString(),
                Status = c.Status.ToString(), c.Claimed, c.Approved, c.Description, c.CreatedAt
            })
            .ToListAsync(ct);

        var export = new
        {
            ExportedAt = DateTime.UtcNow,
            Notice = "Bản sao dữ liệu cá nhân của bạn trên Staylio.",
            Account = new
            {
                user.Id, user.Email, user.Phone, user.FullName, user.DisplayName,
                user.DateOfBirth, user.Bio, user.Location, user.Occupation,
                Languages = Profiles.UnpackLanguages(user.SpokenLanguages),
                Interests = Profiles.UnpackInterests(user.Interests),
                user.AvatarUrl,
                Role = user.Role.ToString(),
                user.EmailConfirmed, user.PhoneConfirmed, user.IsIdentityVerified,
                user.CreatedAt
            },
            Hosting = host is null
                ? null
                : new { host.Name, host.IsSuperhost, host.JoinedAt, host.ResponseRate, host.ResponseTime },
            Bookings = bookings,
            ReviewsWritten = reviewsWritten,
            ReviewsReceived = reviewsReceived,
            Messages = messages,
            Credits = credits,
            ShieldClaims = claims,
            Notifications = notifications,
            Sessions = sessions,
            ExternalLogins = logins
        };

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(export, Pretty));
    }

    /* ------------------------------------------------------- docs/08 §9 */

    /// <summary>What this person has asked for, and where it got to.</summary>
    [HttpGet("requests")]
    public async Task<ActionResult<IReadOnlyList<MyDataRequestDto>>> MyRequests(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var now = DateTime.UtcNow;

        return Ok(await db.DataRequests
            .Where(r => r.UserId == user.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new MyDataRequestDto(
                r.Id,
                r.Kind.ToString(), DataRequests.KindLabel(r.Kind),
                r.Status.ToString(), DataRequests.StatusLabel(r.Status),
                r.CreatedAt, r.DueBy, r.CompletedAt, r.Note,
                r.LinkToken != null && r.LinkExpiresAt > now ? "/api/account/data/download/" + r.LinkToken : null,
                r.LinkExpiresAt))
            .ToListAsync(ct));
    }

    /// <summary>
    /// docs/08 §9 — the intake. Without this the admin queue could only ever be
    /// empty, and the 30-day clock the section sets never started.
    /// </summary>
    [HttpPost("requests")]
    public async Task<ActionResult<MyDataRequestDto>> Ask(
        [FromBody] DataRequestRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        if (!Enum.TryParse<DataRequestKind>(req.Kind, true, out var kind))
            return BadRequest(new { message = "Loại yêu cầu không hợp lệ." });

        // One open request of each kind: a second one does not make it happen
        // sooner, it just makes two clocks to answer.
        if (await db.DataRequests.AnyAsync(
                r => r.UserId == user.Id && r.Kind == kind && r.Status == DataRequestStatus.Open, ct))
        {
            return BadRequest(new
            {
                message = $"Bạn đã có một yêu cầu \"{DataRequests.KindLabel(kind).ToLowerInvariant()}\" đang xử lý."
            });
        }

        var now = DateTime.UtcNow;

        var request = new DataRequest
        {
            UserId = user.Id,
            Kind = kind,
            CreatedAt = now,
            DueBy = DataRequests.DueBy(now)
        };

        db.DataRequests.Add(request);
        await db.SaveChangesAsync(ct);

        return Ok(new MyDataRequestDto(
            request.Id, kind.ToString(), DataRequests.KindLabel(kind),
            request.Status.ToString(), DataRequests.StatusLabel(request.Status),
            request.CreatedAt, request.DueBy, null,
            $"Chúng tôi sẽ xử lý trong {DataRequests.DueDays} ngày.",
            null, null));
    }
}
