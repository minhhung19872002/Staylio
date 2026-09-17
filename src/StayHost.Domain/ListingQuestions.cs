namespace StayHost.Domain;

/// <summary>
/// Public questions on a listing, answered by its host — Booking.com's "Hỏi đáp
/// của khách". Unlike a message, the answer is for every later reader too, so
/// only answered questions are public: an unanswered one would be a question
/// mark hanging over the listing that the host never agreed to show.
/// </summary>
public class ListingQuestion
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public int AskerUserId { get; set; }
    public User? AskerUser { get; set; }

    public string Question { get; set; } = "";
    public string? Answer { get; set; }
    public int? AnsweredByUserId { get; set; }
    public DateTime? AnsweredAt { get; set; }

    /// <summary>The host declined to publish it. Hidden from everyone but the asker.</summary>
    public DateTime? DismissedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class ListingQuestions
{
    public const int MinLength = 10;
    public const int MaxLength = 500;
    public const int MaxAnswerLength = 1000;

    /// <summary>Open questions one person may have on one listing at a time.</summary>
    public const int MaxOpenPerAsker = 3;

    /// <summary>
    /// Null when the text may be posted, otherwise why not. Contact details are
    /// refused the same way a review refuses them (docs/03 §7): a public answer
    /// is exactly where a phone number would take a booking off the platform.
    /// </summary>
    public static string? Problem(string? text, int maxLength)
    {
        var t = (text ?? "").Trim();
        if (t.Length < MinLength) return $"Cần tối thiểu {MinLength} ký tự.";
        if (t.Length > maxLength) return $"Tối đa {maxLength} ký tự.";
        var finding = ContentGuard.Inspect(t);
        if (finding.Any) return $"Không được chứa {finding.Explain()}. Vui lòng bỏ phần đó rồi gửi lại.";
        // Same abuse check a review gets, since this is just as public.
        var check = ContentGuard.CheckReview(t);
        return check.Ok ? null : "Nội dung có ngôn từ xúc phạm hoặc phân biệt đối xử nên không được đăng.";
    }

    public static bool IsPublic(ListingQuestion q) => q.Answer is not null && q.DismissedAt is null;
}
