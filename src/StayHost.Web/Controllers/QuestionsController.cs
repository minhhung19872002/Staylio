using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

public record AskQuestionRequest(string? Question);
public record AnswerQuestionRequest(string? Answer);

public record ListingQuestionDto(
    int Id, string Question, string? Answer, DateTime CreatedAt, DateTime? AnsweredAt,
    /// <summary>Set only on the asker's own copy: still waiting, or declined.</summary>
    string? MyStatus);

public record HostQuestionDto(
    int Id, int ListingId, string ListingTitle, string AskerName, string Question,
    DateTime CreatedAt, string? Answer, DateTime? AnsweredAt);

/// <summary>
/// Public questions on a listing (ListingQuestions). Guests ask, the host — or a
/// co-host lent the Messages scope — answers, and only answered questions are
/// shown to everybody.
/// </summary>
[ApiController]
[Route("api")]
public class QuestionsController(
    StayHostDbContext db, AuthService auth, HostAccess access, NotificationService notifications) : ControllerBase
{
    [HttpGet("listings/{listingId:int}/questions")]
    public async Task<ActionResult<IReadOnlyList<ListingQuestionDto>>> List(int listingId, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        var uid = user?.Id ?? 0;

        var rows = await db.ListingQuestions
            .Where(q => q.ListingId == listingId
                        && ((q.Answer != null && q.DismissedAt == null) || q.AskerUserId == uid))
            .OrderByDescending(q => q.AnsweredAt ?? q.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return Ok(rows.Select(q => new ListingQuestionDto(
            q.Id, q.Question, ListingQuestions.IsPublic(q) ? q.Answer : null, q.CreatedAt,
            ListingQuestions.IsPublic(q) ? q.AnsweredAt : null,
            q.AskerUserId != uid ? null
                : q.DismissedAt is not null ? "Chủ nhà không đăng câu hỏi này"
                : q.Answer is null ? "Đang chờ chủ nhà trả lời" : null)).ToList());
    }

    [HttpPost("listings/{listingId:int}/questions")]
    public async Task<IActionResult> Ask(int listingId, [FromBody] AskQuestionRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Đăng nhập để đặt câu hỏi." });

        var listing = await db.Listings.Include(l => l.Host)
            .FirstOrDefaultAsync(l => l.Id == listingId && l.IsPublished
                                      && l.ReviewStatus == ListingReviewStatus.Approved, ct);
        if (listing is null) return NotFound();
        if (listing.Host?.UserId == user.Id)
            return BadRequest(new { message = "Bạn không thể hỏi trên chỗ nghỉ của chính mình." });

        if (ListingQuestions.Problem(req.Question, ListingQuestions.MaxLength) is { } problem)
            return BadRequest(new { message = problem });

        var open = await db.ListingQuestions.CountAsync(q =>
            q.ListingId == listingId && q.AskerUserId == user.Id && q.Answer == null && q.DismissedAt == null, ct);
        if (open >= ListingQuestions.MaxOpenPerAsker)
            return StatusCode(429, new { message = $"Bạn đang có {open} câu hỏi chờ trả lời cho chỗ nghỉ này." });

        db.ListingQuestions.Add(new ListingQuestion
        {
            ListingId = listingId, AskerUserId = user.Id, Question = req.Question!.Trim()
        });

        var hostUser = await db.Users.FirstOrDefaultAsync(u => u.Id == listing.Host!.UserId, ct);
        await notifications.QueueWithEmailAsync(hostUser, NotificationKind.MessageReceived,
            "Có câu hỏi mới về chỗ nghỉ",
            $"Một khách hỏi về \"{listing.Title}\". Câu trả lời của bạn sẽ hiện công khai trên trang chỗ nghỉ.",
            "/hosting?tab=reviews", ct);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("host/questions")]
    public async Task<ActionResult<IReadOnlyList<HostQuestionDto>>> ForHost(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var ids = await access.ListingIdsAsync(user, CoHostScope.Messages, ct);
        var rows = await db.ListingQuestions
            .Where(q => ids.Contains(q.ListingId) && q.DismissedAt == null)
            .OrderBy(q => q.Answer != null).ThenByDescending(q => q.CreatedAt)
            .Take(100)
            .Select(q => new HostQuestionDto(
                q.Id, q.ListingId, q.Listing!.Title, q.AskerUser!.FullName, q.Question,
                q.CreatedAt, q.Answer, q.AnsweredAt))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("host/questions/{id:int}/answer")]
    public async Task<IActionResult> Answer(int id, [FromBody] AnswerQuestionRequest req, CancellationToken ct)
    {
        var (user, question) = await MineAsync(id, ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        if (question is null) return NotFound();
        if (question.DismissedAt is not null)
            return BadRequest(new { message = "Câu hỏi này đã bị ẩn." });

        if (ListingQuestions.Problem(req.Answer, ListingQuestions.MaxAnswerLength) is { } problem)
            return BadRequest(new { message = problem });

        var first = question.Answer is null;
        question.Answer = req.Answer!.Trim();
        question.AnsweredAt = DateTime.UtcNow;
        question.AnsweredByUserId = user.Id;

        if (first)
        {
            var asker = await db.Users.FirstOrDefaultAsync(u => u.Id == question.AskerUserId, ct);
            await notifications.QueueWithEmailAsync(asker, NotificationKind.MessageReceived,
                "Chủ nhà đã trả lời câu hỏi của bạn",
                $"Câu trả lời về \"{question.Listing!.Title}\" đã có trên trang chỗ nghỉ.",
                $"/rooms/{question.Listing.Slug}", ct);
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>The host chooses not to publish a question. The asker is still told.</summary>
    [HttpPost("host/questions/{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id, CancellationToken ct)
    {
        var (user, question) = await MineAsync(id, ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        if (question is null) return NotFound();
        question.DismissedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<(User? User, ListingQuestion? Question)> MineAsync(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return (null, null);
        var question = await db.ListingQuestions.Include(q => q.Listing)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (question?.Listing is null) return (user, null);
        // Somebody else's listing reads as not found, not as forbidden.
        return await access.MayAsync(user, question.Listing, CoHostScope.Messages, ct)
            ? (user, question)
            : (user, null);
    }
}
