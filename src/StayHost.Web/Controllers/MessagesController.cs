using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>Guest ↔ host conversations, one thread per (listing, guest) pair.</summary>
[ApiController]
[Route("api/messages")]
public class MessagesController(
    StayHostDbContext db, AuthService auth, NotificationService notifications, HostAccess access)
    : ControllerBase
{
    /// <summary>
    /// docs/01 QL-19 — whose side of the thread this user is on: their own id
    /// when they are the guest or the host, the host's id when they are a
    /// co-host lent the Messages scope for that listing, null otherwise. A
    /// co-host speaks for the host, so what they send is the host's message.
    /// </summary>
    private async Task<int?> ActingAsAsync(User user, MessageThread thread, CancellationToken ct)
    {
        if (thread.GuestUserId == user.Id || thread.HostUserId == user.Id) return user.Id;

        var listing = thread.Listing ?? await db.Listings.FirstOrDefaultAsync(l => l.Id == thread.ListingId, ct);
        return listing is not null && await access.MayAsync(user, listing, CoHostScope.Messages, ct)
            ? thread.HostUserId
            : null;
    }

    [HttpGet("threads")]
    public async Task<ActionResult<IReadOnlyList<ThreadSummaryDto>>> Threads(
        [FromQuery] string? filter, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        if (!InboxFilters.TryParse(filter, out var view))
            return BadRequest(new { message = "Bộ lọc hộp thư không hợp lệ." });

        // Listings this user answers for as a co-host, besides their own threads.
        var helped = await access.ListingIdsAsync(user, CoHostScope.Messages, ct);

        var threads = await db.MessageThreads
            .Where(t => t.GuestUserId == user.Id || t.HostUserId == user.Id || helped.Contains(t.ListingId))
            .Include(t => t.Listing!).ThenInclude(l => l.Images)
            .Include(t => t.GuestUser)
            .Include(t => t.HostUser)
            .Include(t => t.Messages)
            .OrderByDescending(t => t.LastMessageAt)
            .AsSplitQuery()
            .ToListAsync(ct);

        // The preview line in the list obeys the same masking as the thread itself.
        var unlocked = await UnlockedThreadIdsAsync(threads, ct);

        // docs/01 TN-05 — filter by unread, awaiting-reply, or archived.
        var rows = threads
            .Select(t => Summarize(
                t, t.GuestUserId == user.Id || t.HostUserId == user.Id ? user.Id : t.HostUserId,
                unlocked.Contains(t.Id)))
            .Where(s => InboxFilters.Matches(view, s.UnreadCount, s.NeedsReply, s.IsArchived))
            .ToList();

        return Ok(rows);
    }

    /// <summary>docs/01 TN-05 — archive or restore a thread, for the viewer only.</summary>
    [HttpPost("threads/{id:int}/archive")]
    public async Task<IActionResult> Archive(int id, [FromQuery] bool on, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var thread = await db.MessageThreads.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (thread is null) return NotFound();

        // Each side archives its own view; a guest tidying up does not hide the
        // conversation from the host.
        var acting = await ActingAsAsync(user, thread, ct);
        if (acting is null) return this.Denied();
        if (acting == thread.GuestUserId) thread.ArchivedByGuest = on;
        else thread.ArchivedByHost = on;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("threads/{id:int}")]
    public async Task<ActionResult<ThreadDetailDto>> Thread(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var thread = await LoadThreadAsync(id, ct);
        if (thread is null) return NotFound();
        if (await ActingAsAsync(user, thread, ct) is not { } acting) return this.Denied();

        // Opening a thread marks the other side's messages as read.
        var unread = thread.Messages.Where(m => m.SenderUserId != acting && m.ReadAt is null).ToList();
        if (unread.Count > 0)
        {
            foreach (var m in unread) m.ReadAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Ok(await DetailAsync(thread, acting, ct));
    }

    [HttpPost]
    public async Task<ActionResult<ThreadDetailDto>> Send([FromBody] SendMessageRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var body = (req.Body ?? "").Trim();
        var hasPhotos = (req.Attachments ?? []).Any(u => !string.IsNullOrWhiteSpace(u));
        if (body.Length == 0 && !hasPhotos) return BadRequest(new { message = "Tin nhắn trống." });
        if (body.Length > 4000) return BadRequest(new { message = "Tin nhắn quá dài." });

        MessageThread? thread;
        var acting = user.Id;

        if (req.ThreadId is int threadId)
        {
            thread = await LoadThreadAsync(threadId, ct);
            if (thread is null) return NotFound();
            if (await ActingAsAsync(user, thread, ct) is not { } side) return this.Denied();
            acting = side;
        }
        else
        {
            // docs/08 §5.2 — starting a new conversation is what is blocked;
            // the ones already open keep working, which is the branch above.
            if (Restrictions.Has(user.RestrictionMask, RestrictionKind.NoNewConversations))
                return StatusCode(403, new { message = Restrictions.Message(RestrictionKind.NoNewConversations) });

            if (req.ListingId is not int listingId)
                return BadRequest(new { message = "Thiếu chỗ nghỉ để bắt đầu hội thoại." });

            var listing = await db.Listings.Include(l => l.Host).FirstOrDefaultAsync(l => l.Id == listingId, ct);
            if (listing is null) return NotFound();

            var hostUserId = listing.Host?.UserId;
            if (hostUserId is null)
                return BadRequest(new { message = "Chủ nhà demo này chưa có tài khoản nhận tin nhắn." });
            if (hostUserId == user.Id)
                return BadRequest(new { message = "Bạn không thể nhắn tin cho chính mình." });

            thread = await db.MessageThreads
                .Include(t => t.Messages)
                .FirstOrDefaultAsync(t => t.ListingId == listingId && t.GuestUserId == user.Id, ct);

            if (thread is null)
            {
                thread = new MessageThread
                {
                    ListingId = listingId,
                    GuestUserId = user.Id,
                    HostUserId = hostUserId.Value
                };
                db.MessageThreads.Add(thread);
                await db.SaveChangesAsync(ct);
            }
        }

        // docs/01 AT-10 — a block stops the conversation in both directions,
        // whichever side raised it. Existing threads and new ones are both covered
        // because the check sits after the thread is resolved.
        var counterpart = thread.GuestUserId == acting ? thread.HostUserId : thread.GuestUserId;
        var blocked = await db.UserBlocks.AnyAsync(
            bk => (bk.BlockerUserId == acting && bk.BlockedUserId == counterpart)
                  || (bk.BlockerUserId == counterpart && bk.BlockedUserId == acting), ct);
        if (blocked) return StatusCode(403, new { message = Blocks.BlockedMessage() });

        // docs/01 TN-02 — photos ride along with the text, capped so one message
        // cannot become an album.
        var attachments = string.Join('\n',
            (req.Attachments ?? []).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Take(6));

        thread.Messages.Add(new Message
        {
            ThreadId = thread.Id, SenderUserId = acting, Body = body, Attachments = attachments
        });
        thread.LastMessageAt = DateTime.UtcNow;

        var recipientId = thread.GuestUserId == acting ? thread.HostUserId : thread.GuestUserId;
        var recipient = await db.Users.FirstOrDefaultAsync(u => u.Id == recipientId, ct);
        notifications.Queue(recipientId, NotificationKind.MessageReceived,
            $"Tin nhắn mới từ {user.FullName}",
            body.Length > 140 ? body[..140] + "…" : body,
            "/messages");
        _ = recipient;

        await db.SaveChangesAsync(ct);

        var fresh = await LoadThreadAsync(thread.Id, ct);
        return Ok(await DetailAsync(fresh!, acting, ct));
    }

    /// <summary>
    /// One place builds the thread payload: the masked messages, the order card
    /// of docs/01 TN-03, and the host's saved phrases of TN-08.
    /// </summary>
    private async Task<ThreadDetailDto> DetailAsync(MessageThread thread, int viewerId, CancellationToken ct)
    {
        var open = await ContactsUnlockedAsync(thread, ct);
        var viewerIsHost = thread.HostUserId == viewerId;

        // TN-03 — the most relevant order for these two on this listing: the live
        // one if there is one, otherwise the most recent.
        var booking = await db.Bookings
            .Where(b => b.ListingId == thread.ListingId && b.GuestUserId == thread.GuestUserId)
            .OrderByDescending(b => BookingLifecycle.BlocksDates.Contains(b.Status))
            .ThenByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var quickReplies = viewerIsHost
            ? await db.QuickReplies
                .Where(q => q.HostUserId == viewerId)
                .OrderBy(q => q.SortOrder).ThenBy(q => q.Id)
                .Select(q => new QuickReplyDto(q.Id, q.Title, q.Body, q.SortOrder))
                .ToListAsync(ct)
            : [];

        var now = DateTime.UtcNow;
        var offers = await db.SpecialOffers
            .Where(o => o.ThreadId == thread.Id)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        return new ThreadDetailDto(
            Summarize(thread, viewerId, open),
            thread.Messages.OrderBy(m => m.SentAt).Select(m => ToDto(m, viewerId, open)).ToList(),
            open,
            booking is null ? null : new ThreadBookingDto(
                booking.Id, booking.Reference, booking.CheckIn, booking.CheckOut,
                booking.Nights, booking.Guests, booking.Total,
                BookingLifecycle.Label(booking.Status), BookingLifecycle.BadgeClass(booking.Status),
                viewerIsHost && booking.Status == BookingStatus.PendingHostApproval),
            quickReplies,
            offers.Select(o => ToOfferDto(o, now)).ToList());
    }

    private static SpecialOfferDto ToOfferDto(SpecialOffer o, DateTime now)
    {
        var nights = Math.Max(1, o.CheckOut.DayNumber - o.CheckIn.DayNumber);
        return new SpecialOfferDto(
            o.Id, o.CheckIn, o.CheckOut, o.Guests, nights,
            o.NightlyRate, o.NightlyRate * nights,
            o.Status.ToString(), SpecialOffers.StatusLabel(o.Status),
            SpecialOffers.IsLive(o, now), o.ExpiresAt, o.BookingId);
    }

    /* ------------------------------------------------------ ĐP-17, QL-14 */

    /// <summary>
    /// docs/01 QL-14 — the host offers this guest a private price on the thread's
    /// listing, good for 24 hours (docs/04 §5c). A message card announces it.
    /// </summary>
    [HttpPost("threads/{id:int}/offer")]
    public async Task<ActionResult<ThreadDetailDto>> SendOffer(
        int id, [FromBody] SendOfferRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var thread = await LoadThreadAsync(id, ct);
        if (thread is null) return NotFound();

        // Only the host of this conversation may make an offer on it.
        if (thread.HostUserId != user.Id) return this.Denied();

        if (SpecialOffers.Validate(req.CheckIn, req.CheckOut, req.NightlyRate, req.Guests) is { } invalid)
            return BadRequest(new { message = invalid });

        var now = DateTime.UtcNow;
        var offer = new SpecialOffer
        {
            ThreadId = thread.Id,
            ListingId = thread.ListingId,
            HostUserId = thread.HostUserId,
            GuestUserId = thread.GuestUserId,
            CheckIn = req.CheckIn,
            CheckOut = req.CheckOut,
            Guests = req.Guests,
            NightlyRate = req.NightlyRate,
            CreatedAt = now,
            ExpiresAt = SpecialOffers.ExpiryFrom(now)
        };
        db.SpecialOffers.Add(offer);

        var nights = Math.Max(1, req.CheckOut.DayNumber - req.CheckIn.DayNumber);
        db.Messages.Add(new Message
        {
            ThreadId = thread.Id,
            SenderUserId = user.Id,
            IsSystem = true,
            Body = $"Ưu đãi riêng: {req.NightlyRate:#,##0}₫/đêm cho {nights} đêm " +
                   $"({req.CheckIn:dd/MM}–{req.CheckOut:dd/MM}). Hiệu lực 24 giờ."
        });
        thread.LastMessageAt = now;

        await db.SaveChangesAsync(ct);
        return Ok(await DetailAsync((await LoadThreadAsync(id, ct))!, user.Id, ct));
    }

    /// <summary>docs/01 ĐP-17 — the host takes a still-pending offer back.</summary>
    [HttpPost("offers/{offerId:int}/withdraw")]
    public async Task<IActionResult> WithdrawOffer(int offerId, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var offer = await db.SpecialOffers.FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null) return NotFound();
        if (offer.HostUserId != user.Id) return this.Denied();

        if (offer.Status == SpecialOfferStatus.Pending)
        {
            offer.Status = SpecialOfferStatus.Withdrawn;
            offer.RespondedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    /* ------------------------------------------------------------- TN-08 */

    [HttpGet("quick-replies")]
    public async Task<ActionResult<IReadOnlyList<QuickReplyDto>>> QuickReplies(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        return Ok(await db.QuickReplies
            .Where(q => q.HostUserId == user.Id)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id)
            .Select(q => new QuickReplyDto(q.Id, q.Title, q.Body, q.SortOrder))
            .ToListAsync(ct));
    }

    [HttpPost("quick-replies")]
    public async Task<ActionResult<QuickReplyDto>> AddQuickReply(
        [FromBody] SaveQuickReplyRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var title = (req.Title ?? "").Trim();
        var body = (req.Body ?? "").Trim();
        if (title.Length == 0 || body.Length == 0)
            return BadRequest(new { message = "Mẫu trả lời cần cả tên và nội dung." });

        var reply = new QuickReply
        {
            HostUserId = user.Id, Title = title, Body = body, SortOrder = req.SortOrder
        };
        db.QuickReplies.Add(reply);
        await db.SaveChangesAsync(ct);

        return Ok(new QuickReplyDto(reply.Id, reply.Title, reply.Body, reply.SortOrder));
    }

    [HttpDelete("quick-replies/{id:int}")]
    public async Task<IActionResult> DeleteQuickReply(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var reply = await db.QuickReplies.FirstOrDefaultAsync(q => q.Id == id && q.HostUserId == user.Id, ct);
        if (reply is null) return NoContent();

        db.QuickReplies.Remove(reply);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// docs/03 §10 — contact details stay hidden until this guest has a
    /// confirmed booking at this listing. Before that, the two sides trade only
    /// through the platform.
    /// </summary>
    private Task<bool> ContactsUnlockedAsync(MessageThread thread, CancellationToken ct) =>
        db.Bookings.AnyAsync(b =>
            b.ListingId == thread.ListingId &&
            b.GuestUserId == thread.GuestUserId &&
            (b.Status == BookingStatus.Confirmed
             || b.Status == BookingStatus.InProgress
             || b.Status == BookingStatus.Completed), ct);

    private async Task<HashSet<int>> UnlockedThreadIdsAsync(
        IReadOnlyCollection<MessageThread> threads, CancellationToken ct)
    {
        if (threads.Count == 0) return [];

        var listingIds = threads.Select(t => t.ListingId).Distinct().ToList();
        var guestIds = threads.Select(t => t.GuestUserId).Distinct().ToList();

        var confirmed = await db.Bookings
            .Where(b => listingIds.Contains(b.ListingId)
                        && b.GuestUserId != null && guestIds.Contains(b.GuestUserId.Value)
                        && (b.Status == BookingStatus.Confirmed
                            || b.Status == BookingStatus.InProgress
                            || b.Status == BookingStatus.Completed))
            .Select(b => new { b.ListingId, b.GuestUserId })
            .ToListAsync(ct);

        var pairs = confirmed.Select(c => (c.ListingId, c.GuestUserId)).ToHashSet();
        return threads
            .Where(t => pairs.Contains((t.ListingId, t.GuestUserId)))
            .Select(t => t.Id)
            .ToHashSet();
    }

    private Task<MessageThread?> LoadThreadAsync(int id, CancellationToken ct) =>
        db.MessageThreads
            .Include(t => t.Listing!).ThenInclude(l => l.Images)
            .Include(t => t.GuestUser)
            .Include(t => t.HostUser)
            .Include(t => t.Messages)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    private static ThreadSummaryDto Summarize(MessageThread t, int viewerId, bool contactsUnlocked)
    {
        var viewerIsHost = t.HostUserId == viewerId;
        var other = viewerIsHost ? t.GuestUser : t.HostUser;
        var last = t.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault();

        // docs/01 TN-05 — awaiting reply when the other side spoke last (a system
        // line is not somebody waiting on an answer); archive state is per side.
        var needsReply = last is not null && !last.IsSystem && last.SenderUserId != viewerId;
        var isArchived = viewerIsHost ? t.ArchivedByHost : t.ArchivedByGuest;

        return new ThreadSummaryDto(
            t.Id,
            t.ListingId,
            t.Listing?.Slug ?? "",
            t.Listing?.Title ?? "",
            t.Listing?.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault() ?? "",
            other?.FullName ?? "Người dùng",
            other?.Initials ?? "??",
            viewerIsHost,
            last is null ? null : Visible(last, contactsUnlocked),
            t.LastMessageAt,
            t.Messages.Count(m => m.SenderUserId != viewerId && m.ReadAt is null),
            needsReply,
            isArchived);
    }

    private static MessageDto ToDto(Message m, int viewerId, bool contactsUnlocked) => new(
        m.Id, m.SenderUserId, m.SenderUser?.FullName ?? "",
        Visible(m, contactsUnlocked), m.SentAt, m.SenderUserId == viewerId, m.IsSystem,
        !contactsUnlocked && !m.IsSystem && ContactGuardHit(m.Body),
        m.Attachments.Split('\n', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// What actually goes over the wire. The stored text is never altered — the
    /// masking happens on the way out, so unlocking a thread reveals the
    /// original rather than a permanently damaged copy.
    /// </summary>
    private static string Visible(Message m, bool contactsUnlocked) =>
        contactsUnlocked || m.IsSystem ? m.Body : ContentGuard.MaskContacts(m.Body);

    private static bool ContactGuardHit(string body) => ContentGuard.Inspect(body).Any;
}
