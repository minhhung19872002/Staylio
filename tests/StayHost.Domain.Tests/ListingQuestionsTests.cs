namespace StayHost.Domain.Tests;

public class ListingQuestionsTests
{
    [Fact]
    public void A_plain_question_is_fine() =>
        Assert.Null(ListingQuestions.Problem("Có chỗ đậu xe ô tô không ạ?", ListingQuestions.MaxLength));

    [Fact]
    public void Too_short_or_too_long_is_refused()
    {
        Assert.NotNull(ListingQuestions.Problem("Wifi?", ListingQuestions.MaxLength));
        Assert.NotNull(ListingQuestions.Problem(new string('a', 501), ListingQuestions.MaxLength));
    }

    [Fact]
    public void A_phone_number_in_public_is_refused_by_name()
    {
        var why = ListingQuestions.Problem("Gọi mình số 0912 345 678 nhé chủ nhà", ListingQuestions.MaxAnswerLength);
        Assert.Contains("số điện thoại", why);
    }

    [Fact]
    public void Only_an_answered_question_that_was_not_dismissed_is_public()
    {
        Assert.False(ListingQuestions.IsPublic(new ListingQuestion()));
        Assert.True(ListingQuestions.IsPublic(new ListingQuestion { Answer = "Có ạ" }));
        Assert.False(ListingQuestions.IsPublic(new ListingQuestion { Answer = "Có ạ", DismissedAt = DateTime.UtcNow }));
    }
}
