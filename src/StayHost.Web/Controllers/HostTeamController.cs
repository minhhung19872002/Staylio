using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/01 QL-19 (people helping run a listing) and QL-10 (calendars kept on
/// other platforms). Both are about letting something outside this account
/// touch a listing, so both live behind the same ownership check.
/// </summary>
[ApiController]
[Route("api/host")]
public class HostTeamController(
    StayHostDbContext db,
    AuthService auth,
    HostAccess access,
    CalendarSyncService sync,
    NotificationService notifications) : ControllerBase
{
    /* ------------------------------------------------------------- QL-19 */

    /// <summary>Everyone this host invited, and everywhere this user is a co-host.</summary>
    [HttpGet("co-hosts")]
    public async Task<ActionResult<CoHostBoardDto>> CoHosts(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        // Projected to an anonymous type first: EF cannot build a positional
        // record out of a query, and the payout labels are string work anyway.
        var invitedRows = await db.CoHosts
            .Where(c => c.OwnerUserId == user.Id && c.Status != CoHostStatus.Revoked)
            .OrderByDescending(c => c.InvitedAt)
            .Select(c => new
            {
                c.Id, c.Email, Name = c.CoHostUser!.FullName, c.ListingId,
                ListingTitle = c.Listing!.Title, c.Scope, c.Status, c.InvitedAt,
                c.PayoutKind, c.PayoutPercent, c.PayoutFixed, c.PayoutStatus, c.PayoutProposedAt,
                Paid = db.CoHostPayouts
                    .Where(p => p.CoHostId == c.Id && p.Status == PayoutStatus.Paid)
                    .Sum(p => (decimal?)(p.Amount - p.ClawedBack)) ?? 0m
            })
            .ToListAsync(ct);

        var invited = invitedRows.Select(c => new CoHostDto(
            c.Id, c.Email, c.Name, c.ListingId, c.ListingTitle,
            CoHostScopes.Keys(c.Scope), CoHostScopes.Describe(c.Scope),
            c.Status.ToString().ToLower(), StatusLabel(c.Status), c.InvitedAt,
            CoHostPayouts.Key(c.PayoutKind), c.PayoutPercent, c.PayoutFixed,
            CoHostPayouts.StatusKey(c.PayoutStatus), CoHostPayouts.StatusLabel(c.PayoutStatus),
            c.PayoutProposedAt is { } at ? CoHostPayouts.ConfirmBy(at) : null,
            c.Paid)).ToList();

        var helpingRows = await db.CoHosts
            .Where(c => (c.CoHostUserId == user.Id || (user.EmailConfirmed && c.Email == user.Email))
                        && c.Status != CoHostStatus.Revoked)
            .OrderByDescending(c => c.InvitedAt)
            .Select(c => new
            {
                c.Id, c.InviteToken, OwnerName = c.OwnerUser!.FullName, c.ListingId,
                ListingTitle = c.Listing!.Title, c.Scope, c.Status,
                c.PayoutKind, c.PayoutPercent, c.PayoutFixed, c.PayoutStatus, c.PayoutProposedAt,
                Paid = db.CoHostPayouts
                    .Where(p => p.CoHostId == c.Id && p.Status == PayoutStatus.Paid)
                    .Sum(p => (decimal?)(p.Amount - p.ClawedBack)) ?? 0m
            })
            .ToListAsync(ct);

        // docs/07 §19.3 — whether this person has anywhere to be paid. Asked
        // once for the user, not per row: it is a property of them, not of the
        // arrangement, and a share with nowhere to go sits held for ever without
        // anybody being told why.
        var hasAccount = await db.Hosts
            .AnyAsync(h => h.UserId == user.Id && h.PayoutAccountLast4 != null, ct);

        var helping = helpingRows.Select(c => new CoHostInviteDto(
            c.Id, c.InviteToken, c.OwnerName, c.ListingId, c.ListingTitle,
            CoHostScopes.Describe(c.Scope), c.Status.ToString().ToLower(), StatusLabel(c.Status),
            CoHostPayouts.Key(c.PayoutKind), c.PayoutPercent, c.PayoutFixed,
            CoHostPayouts.StatusKey(c.PayoutStatus), CoHostPayouts.StatusLabel(c.PayoutStatus),
            c.PayoutProposedAt is { } at ? CoHostPayouts.ConfirmBy(at) : null,
            c.Paid, hasAccount)).ToList();

        // What this user has been paid as somebody else's co-host, stay by stay.
        // docs/09 §3.5 taught this repo the other half of the lesson: money that
        // is collected and then never shown to the person it concerns may as
        // well not have been recorded.
        var earnings = await db.CoHostPayouts
            .Where(p => p.CoHost!.CoHostUserId == user.Id)
            .OrderByDescending(p => p.CreatedAt)
            .Take(50)
            .Select(p => new
            {
                p.Id, Reference = p.Booking!.Reference, ListingTitle = p.Booking!.Listing!.Title,
                p.Booking!.CheckIn, p.Amount, p.Kind, p.Percent, p.Fixed,
                p.Status, p.PaidOutAt, p.ClawedBack
            })
            .ToListAsync(ct);

        var earned = earnings.Where(e => e.Status == PayoutStatus.Paid).Sum(e => e.Amount - e.ClawedBack);

        var overcommitted = CoHostPayouts.Overcommitted(invitedRows
            .Where(c => c.PayoutStatus == CoHostPayoutStatus.Active)
            .Select(c => new CoHostPayouts.Terms(c.Id, c.PayoutKind, c.PayoutPercent, c.PayoutFixed)));

        return Ok(new CoHostBoardDto(
            invited, helping,
            CoHostScopes.All.Select(s => new ScopeOptionDto(s.Key, s.Label)).ToList(),
            CoHostPayouts.All
                .Select(k => new PayoutKindOptionDto(
                    k.Key, CoHostPayouts.KindLabel(k.Kind), k.NeedsPercent, k.NeedsAmount))
                .ToList(),
            earnings.Select(e => new CoHostEarningDto(
                e.Id, e.Reference, e.ListingTitle, e.CheckIn, e.Amount,
                CoHostPayouts.Key(e.Kind), e.Percent, e.Fixed,
                e.Status.ToString().ToLower(), PayoutLabel(e.Status), e.PaidOutAt, e.ClawedBack)).ToList(),
            earned,
            overcommitted));
    }

    private static string PayoutLabel(PayoutStatus status) => status switch
    {
        PayoutStatus.Paid => "Đã chuyển",
        PayoutStatus.Sent => "Đã lên lệnh, chờ ngân hàng",
        PayoutStatus.OnHold => "Đang tạm giữ",
        _ => "Chờ tới hạn"
    };

    /// <summary>
    /// The invite is keyed by email, not by account, so a host can bring in
    /// someone who has not signed up yet.
    /// </summary>
    [HttpPost("co-hosts")]
    public async Task<ActionResult<CoHostDto>> Invite([FromBody] InviteCoHostRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (email.Length == 0 || !email.Contains('@'))
            return BadRequest(new { message = "Email không hợp lệ." });
        if (string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Bạn đã là chủ nhà của tin này rồi." });

        var scope = CoHostScopes.Parse(req.Scopes);
        if (scope == CoHostScope.None)
            return BadRequest(new { message = "Chọn ít nhất một quyền cho người đồng quản lý." });

        // A listing named here must be one of the inviter's own; a co-host does
        // not get to hand out access they were lent.
        if (req.ListingId is { } listingId)
        {
            var profile = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == user.Id, ct);
            var owns = profile is not null &&
                       await db.Listings.AnyAsync(l => l.Id == listingId && l.HostId == profile.Id, ct);
            if (!owns) return this.Denied();
        }

        var existing = await db.CoHosts.FirstOrDefaultAsync(c =>
            c.OwnerUserId == user.Id && c.Email == email && c.ListingId == req.ListingId
            && c.Status != CoHostStatus.Revoked, ct);

        if (existing is not null)
        {
            existing.Scope = scope;
            await db.SaveChangesAsync(ct);
            return Ok(await ToDtoAsync(existing, ct));
        }

        // Bound to an account only once that account has proved the address;
        // otherwise the invitation waits for whoever does (see Respond).
        var invitee = await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.EmailConfirmed, ct);

        var invite = new CoHost
        {
            OwnerUserId = user.Id,
            Email = email,
            CoHostUserId = invitee?.Id,
            ListingId = req.ListingId,
            Scope = scope
        };
        db.CoHosts.Add(invite);
        await db.SaveChangesAsync(ct);

        if (invitee is not null)
        {
            await notifications.QueueWithEmailAsync(
                invitee, NotificationKind.System,
                "Lời mời đồng quản lý",
                $"{user.FullName} mời bạn cùng quản lý chỗ nghỉ ({CoHostScopes.Describe(scope)}).",
                "/hosting?tab=team", ct);
            await db.SaveChangesAsync(ct);
        }

        return Ok(await ToDtoAsync(invite, ct));
    }

    [HttpPost("co-hosts/{id:int}/{decision}")]
    public async Task<IActionResult> Respond(int id, string decision, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var invite = await db.CoHosts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (invite is null) return NotFound();

        // An invitation sent to an address belongs to whoever proved they own it.
        // Registration signs a person in before the address is confirmed, so
        // matching on the typed address alone let anybody sign up as the invitee
        // and take a Full co-host seat on somebody else's listing.
        var byAddress = string.Equals(invite.Email, user.Email, StringComparison.OrdinalIgnoreCase);
        var mine = invite.CoHostUserId == user.Id || byAddress && user.EmailConfirmed;
        if (!mine)
            return byAddress
                ? this.Denied("Xác nhận địa chỉ email của bạn trước khi nhận lời mời này.")
                : this.Denied();
        if (invite.Status is CoHostStatus.Revoked) return BadRequest(new { message = "Lời mời đã bị thu hồi." });

        invite.CoHostUserId = user.Id;
        invite.RespondedAt = DateTime.UtcNow;
        invite.Status = decision == "accept" ? CoHostStatus.Active : CoHostStatus.Declined;
        await db.SaveChangesAsync(ct);

        var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == invite.OwnerUserId, ct);
        await notifications.QueueWithEmailAsync(
            owner, NotificationKind.System,
            invite.Status == CoHostStatus.Active ? "Đã nhận lời mời đồng quản lý" : "Đã từ chối đồng quản lý",
            $"{user.FullName} {(invite.Status == CoHostStatus.Active ? "đã nhận" : "đã từ chối")} lời mời của bạn.",
            "/hosting?tab=team", ct);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /* ---------------------------------------------- docs/02 G8: the money */

    /// <summary>
    /// The owner offering a co-host a share of what they earn.
    ///
    /// It is an offer, not a setting. The terms take effect only once the person
    /// being paid says yes, because a share of somebody's income is income —
    /// they are the one who has to declare it, and they are the one who has to
    /// have told us where to send it. Changing an offer that was already accepted
    /// puts it back to waiting: nobody ends up on a smaller cut than they agreed
    /// to without being asked again.
    /// </summary>
    [HttpPut("co-hosts/{id:int}/payout")]
    public async Task<ActionResult<CoHostDto>> SetPayout(
        int id, [FromBody] CoHostPayoutRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var invite = await db.CoHosts
            .Include(c => c.CoHostUser)
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == user.Id, ct);

        if (invite is null) return NotFound();
        if (invite.Status == CoHostStatus.Revoked)
            return BadRequest(new { message = "Quyền đồng quản lý này đã bị thu hồi." });

        var kind = CoHostPayouts.Parse(req.Kind);

        if (kind == CoHostPayoutKind.None)
        {
            // Turning it off is the owner's alone to do, and it needs no
            // confirmation from anybody: nothing is being taken from the person
            // who was receiving it beyond stays that have not happened yet.
            invite.PayoutKind = CoHostPayoutKind.None;
            invite.PayoutPercent = 0m;
            invite.PayoutFixed = 0m;
            invite.PayoutStatus = CoHostPayoutStatus.None;
            invite.PayoutProposedAt = null;
            invite.PayoutRespondedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            return Ok(await ToDtoAsync(invite, ct));
        }

        if (CoHostPayouts.Invalid(kind, req.Percent, req.Amount) is { } bad)
            return BadRequest(new { message = bad });

        // The invite has to have been accepted first. Offering a cut of the
        // takings to somebody who has not agreed to help run the place is an
        // offer with nobody on the other end of it.
        if (invite.Status != CoHostStatus.Active || invite.CoHostUserId is null)
            return BadRequest(new { message = "Người này chưa nhận lời mời đồng quản lý." });

        invite.PayoutKind = kind;
        invite.PayoutPercent = req.Percent;
        invite.PayoutFixed = req.Amount;
        invite.PayoutStatus = CoHostPayoutStatus.Proposed;
        invite.PayoutProposedAt = DateTime.UtcNow;
        invite.PayoutRespondedAt = null;

        await db.SaveChangesAsync(ct);

        await notifications.QueueWithEmailAsync(invite.CoHostUser, NotificationKind.System,
            "Đề nghị chia thu nhập từ chỗ nghỉ",
            CoHostPayouts.ProposalNotice(
                user.FullName,
                CoHostPayouts.Describe(kind, req.Percent, req.Amount),
                invite.PayoutProposedAt.Value),
            "/hosting?tab=team", ct);
        await db.SaveChangesAsync(ct);

        return Ok(await ToDtoAsync(invite, ct));
    }

    /// <summary>
    /// The co-host answering. Accepting is what creates their payee record — the
    /// bank account, the verification and the debt ledger a host has, because
    /// from the platform's side somebody being paid is somebody being paid.
    /// </summary>
    [HttpPost("co-hosts/{id:int}/payout/{decision}")]
    public async Task<IActionResult> RespondPayout(int id, string decision, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var invite = await db.CoHosts
            .Include(c => c.OwnerUser)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (invite is null) return NotFound();
        if (invite.CoHostUserId != user.Id) return this.Denied();

        if (invite.PayoutStatus != CoHostPayoutStatus.Proposed)
            return BadRequest(new { message = "Không có đề nghị nào đang chờ bạn trả lời." });

        // docs/07 §19.2 — the offer has a shelf life, and the sweep may not have
        // reached it yet. Checked here as well so an offer that lapsed a minute
        // ago cannot be accepted in the gap.
        if (invite.PayoutProposedAt is { } at && CoHostPayouts.ProposalExpired(at, DateTime.UtcNow))
        {
            invite.PayoutStatus = CoHostPayoutStatus.Expired;
            await db.SaveChangesAsync(ct);
            return BadRequest(new { message = "Đề nghị này đã quá hạn. Hãy nhờ chủ nhà đề nghị lại." });
        }

        var accepted = decision == "accept";

        if (accepted)
        {
            // Their own payee record. A co-host who is already a host keeps the
            // one they have — the same bank account, the same verification, and
            // one debt to the platform rather than two.
            var payee = await auth.EnsureHostProfileAsync(user, ct);
            invite.PayeeHostId = payee.Id;
        }

        invite.PayoutStatus = accepted ? CoHostPayoutStatus.Active : CoHostPayoutStatus.Declined;
        invite.PayoutRespondedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await notifications.QueueWithEmailAsync(invite.OwnerUser, NotificationKind.System,
            accepted ? "Đã nhận đề nghị chia thu nhập" : "Đã từ chối đề nghị chia thu nhập",
            accepted
                ? CoHostPayouts.ConfirmedNotice(user.FullName,
                    CoHostPayouts.Describe(invite.PayoutKind, invite.PayoutPercent, invite.PayoutFixed))
                : CoHostPayouts.DeclinedNotice(user.FullName),
            "/hosting?tab=team", ct);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Taking the access back, which the spec asks for by name.</summary>
    [HttpDelete("co-hosts/{id:int}")]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var invite = await db.CoHosts.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == user.Id, ct);
        if (invite is null) return NotFound();

        invite.Status = CoHostStatus.Revoked;
        invite.RevokedAt = DateTime.UtcNow;

        // docs/02 G8 — taking the access back takes the share with it. Leaving
        // the terms Active would keep diverting money to somebody who can no
        // longer see the listing, and nothing would ever surface it: the sweep
        // reads the terms, not the access.
        //
        // Shares already decided for past stays are left alone. That work was
        // done and that money is owed.
        if (invite.PayoutStatus == CoHostPayoutStatus.Active
            || invite.PayoutStatus == CoHostPayoutStatus.Proposed)
        {
            invite.PayoutStatus = CoHostPayoutStatus.None;
            invite.PayoutRespondedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        if (invite.CoHostUserId is { } coHostId)
        {
            var coHost = await db.Users.FirstOrDefaultAsync(u => u.Id == coHostId, ct);
            await notifications.QueueWithEmailAsync(
                coHost, NotificationKind.System, "Quyền đồng quản lý đã kết thúc",
                "Chủ nhà đã thu hồi quyền đồng quản lý của bạn.", "/hosting", ct);
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    /* ------------------------------------------------------------- QL-10 */

    [HttpGet("listings/{id:int}/feeds")]
    public async Task<ActionResult<CalendarSyncDto>> Feeds(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var listing = await access.ListingAsync(user, id, CoHostScope.Calendar, ct);
        if (listing is null) return this.Denied();

        return Ok(await BoardAsync(listing, ct));
    }

    [HttpPost("listings/{id:int}/feeds")]
    public async Task<ActionResult<CalendarSyncDto>> AddFeed(
        int id, [FromBody] AddCalendarFeedRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var listing = await access.ListingAsync(user, id, CoHostScope.Calendar, ct);
        if (listing is null) return this.Denied();

        var url = (req.Url ?? "").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return BadRequest(new { message = "Địa chỉ lịch phải là http hoặc https." });

        if (await db.CalendarFeeds.CountAsync(f => f.ListingId == id, ct) >= 5)
            return BadRequest(new { message = "Mỗi chỗ nghỉ chỉ nối được tối đa 5 lịch." });

        var feed = new CalendarFeed
        {
            ListingId = id,
            Label = string.IsNullOrWhiteSpace(req.Label) ? uri.Host : req.Label!.Trim(),
            Url = url
        };
        db.CalendarFeeds.Add(feed);
        await db.SaveChangesAsync(ct);

        // Sync on the spot: a host who just pasted a link wants to see whether
        // it worked, not wait an hour to find out it did not.
        await sync.SyncAsync(feed, ct);

        return Ok(await BoardAsync(listing, ct));
    }

    [HttpPost("listings/{id:int}/feeds/{feedId:int}/sync")]
    public async Task<ActionResult<CalendarSyncDto>> SyncFeed(int id, int feedId, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var listing = await access.ListingAsync(user, id, CoHostScope.Calendar, ct);
        if (listing is null) return this.Denied();

        var feed = await db.CalendarFeeds.FirstOrDefaultAsync(f => f.Id == feedId && f.ListingId == id, ct);
        if (feed is null) return NotFound();

        await sync.SyncAsync(feed, ct);
        return Ok(await BoardAsync(listing, ct));
    }

    [HttpDelete("listings/{id:int}/feeds/{feedId:int}")]
    public async Task<ActionResult<CalendarSyncDto>> RemoveFeed(int id, int feedId, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var listing = await access.ListingAsync(user, id, CoHostScope.Calendar, ct);
        if (listing is null) return this.Denied();

        var feed = await db.CalendarFeeds.FirstOrDefaultAsync(f => f.Id == feedId && f.ListingId == id, ct);
        if (feed is null) return NotFound();

        // The blocks go with it: they were never this host's decision.
        db.CalendarBlocks.RemoveRange(db.CalendarBlocks.Where(b => b.FeedId == feedId));
        db.CalendarFeeds.Remove(feed);
        await db.SaveChangesAsync(ct);

        return Ok(await BoardAsync(listing, ct));
    }

    /// <summary>
    /// The export other platforms poll. No cookie, no session — the token in the
    /// URL is the credential, so a wrong one is a plain 404.
    /// </summary>
    [HttpGet("/calendars/{id:int}/{token}.ics")]
    public async Task<IActionResult> Export(int id, string token, CancellationToken ct)
    {
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing is null || listing.IcalToken != token) return NotFound();

        var body = await sync.ExportAsync(listing, ct);
        return Content(body, Ical.ContentType, System.Text.Encoding.UTF8);
    }

    private async Task<CalendarSyncDto> BoardAsync(Listing listing, CancellationToken ct)
    {
        var feeds = await db.CalendarFeeds
            .Where(f => f.ListingId == listing.Id)
            .OrderBy(f => f.Id)
            .Select(f => new CalendarFeedDto(
                f.Id, f.Label, f.Url, f.LastSyncedAt, f.LastError, f.EventCount, f.OverlapWarning))
            .ToListAsync(ct);

        var export = $"{Request.Scheme}://{Request.Host}/calendars/{listing.Id}/{listing.IcalToken}.ics";
        return new CalendarSyncDto(listing.Id, listing.Title, export, feeds);
    }

    private async Task<CoHostDto> ToDtoAsync(CoHost invite, CancellationToken ct)
    {
        var name = invite.CoHostUserId is null
            ? null
            : await db.Users.Where(u => u.Id == invite.CoHostUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        var title = invite.ListingId is null
            ? null
            : await db.Listings.Where(l => l.Id == invite.ListingId).Select(l => l.Title).FirstOrDefaultAsync(ct);

        var paid = await db.CoHostPayouts
            .Where(p => p.CoHostId == invite.Id && p.Status == PayoutStatus.Paid)
            .SumAsync(p => (decimal?)(p.Amount - p.ClawedBack), ct) ?? 0m;

        return new CoHostDto(
            invite.Id, invite.Email, name, invite.ListingId, title,
            CoHostScopes.Keys(invite.Scope), CoHostScopes.Describe(invite.Scope),
            invite.Status.ToString().ToLower(), StatusLabel(invite.Status), invite.InvitedAt,
            CoHostPayouts.Key(invite.PayoutKind), invite.PayoutPercent, invite.PayoutFixed,
            CoHostPayouts.StatusKey(invite.PayoutStatus), CoHostPayouts.StatusLabel(invite.PayoutStatus),
            invite.PayoutProposedAt is { } at ? CoHostPayouts.ConfirmBy(at) : null,
            paid);
    }

    private static string StatusLabel(CoHostStatus status) => status switch
    {
        CoHostStatus.Invited => "Đang chờ nhận lời",
        CoHostStatus.Active => "Đang đồng quản lý",
        CoHostStatus.Declined => "Đã từ chối",
        _ => "Đã thu hồi"
    };
}
