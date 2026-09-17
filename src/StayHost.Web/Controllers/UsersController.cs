using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// docs/01 TK-05, docs/02 C6 — the public profile page. Open to anybody, so
/// every field on the way out is one the spec names; nothing is included merely
/// because the row happened to carry it.
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersController(StayHostDbContext db, CatalogService catalog) : ControllerBase
{
    /// <summary>How many reviews a profile leads with before "xem thêm" would be needed.</summary>
    private const int ReviewsShown = 12;

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PublicProfileDto>> Profile(int id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound(new { message = "Không tìm thấy người dùng này." });

        // docs/01 TK-12 — somebody who paused their own account is not on the
        // platform for the moment, and a profile page that still answers is the
        // one place they would still be. The same message as a missing account:
        // whether a particular person has stepped away is their business.
        if (user.PausedAt is not null)
            return NotFound(new { message = "Không tìm thấy người dùng này." });

        var host = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == user.Id, ct);
        var displayName = Profiles.DisplayNameOf(user.DisplayName, user.FullName);

        // Somebody who hosts nothing matches no listing and no review, so both
        // queries below run once rather than being written twice around a null.
        var hostId = host?.Id ?? 0;

        // docs/02 C6 — "tin đăng đang có", so drafts and paused listings stay out;
        // docs/01 AT-01 keeps places still awaiting review off the public profile too.
        var listings = await db.Listings
            .Where(l => l.HostId == hostId && l.IsPublished
                        && l.ReviewStatus == ListingReviewStatus.Approved)
            .Include(l => l.Images)
            .Include(l => l.Amenities).ThenInclude(la => la.Amenity)
            .AsSplitQuery()
            .ToListAsync(ct);

        var favIds = await catalog.FavoriteIdsAsync(HttpContext.SessionId(), ct);

        // docs/03 §7 — unpublished reviews are invisible to everyone, including
        // the person they are about.
        var asHost = await db.Reviews
            .Where(r => r.PublishedAt != null && r.Listing!.HostId == hostId)
            .OrderByDescending(r => r.PublishedAt)
            .Take(ReviewsShown)
            .Select(r => new
            {
                r.Id, r.AuthorName, r.AuthorInitials, r.AuthorUserId, r.When, r.Text, r.Rating,
                ListingTitle = r.Listing!.Title,
                ListingSlug = r.Listing.Slug
            })
            .ToListAsync(ct);

        var asGuest = await db.GuestReviews
            .Where(r => r.GuestUserId == user.Id && r.PublishedAt != null)
            .OrderByDescending(r => r.PublishedAt)
            .Take(ReviewsShown)
            .Select(r => new
            {
                r.Id,
                AuthorUserId = (int?)r.HostUserId,
                AuthorDisplayName = r.HostUser!.DisplayName,
                AuthorFullName = r.HostUser.FullName,
                r.Text,
                r.Rating,
                r.CreatedAt,
                ListingTitle = r.Booking!.Listing!.Title,
                ListingSlug = r.Booking.Listing.Slug
            })
            .ToListAsync(ct);

        // docs/03 §7 — the three headings hosts score a guest on, averaged over
        // everything published about them.
        var scored = await db.GuestReviews
            .Where(r => r.GuestUserId == user.Id && r.PublishedAt != null)
            .Select(r => new { r.Cleanliness, r.Communication, r.HouseRules, r.WouldHostAgain })
            .ToListAsync(ct);

        static double? Avg(IEnumerable<int?> xs)
        {
            var given = xs.Where(x => x.HasValue).Select(x => (double)x!.Value).ToList();
            return given.Count == 0 ? null : Math.Round(given.Average(), 1);
        }

        var guestScores = scored.Count == 0 ? null : new GuestScoresDto(
            scored.Count,
            Avg(scored.Select(r => r.Cleanliness)),
            Avg(scored.Select(r => r.Communication)),
            Avg(scored.Select(r => r.HouseRules)),
            (int)Math.Round(100.0 * scored.Count(r => r.WouldHostAgain) / scored.Count));

        return Ok(new PublicProfileDto(
            user.Id,
            displayName,
            Profiles.InitialsOf(displayName),
            user.AvatarUrl,
            Profiles.JoinedLabel(user.CreatedAt),
            Profiles.Badges(user.EmailConfirmed, user.PhoneConfirmed, user.IsIdentityVerified),
            user.Bio,
            Profiles.UnpackLanguages(user.SpokenLanguages).Select(Profiles.LanguageLabel).ToList(),
            user.Location,
            user.Occupation,
            Profiles.UnpackInterests(user.Interests),
            host is not null,
            host?.IsSuperhost ?? false,
            host?.ResponseRate,
            host?.ResponseTime,
            Profiles.OverallRating(listings.Select(l => (l.Rating, l.ReviewCount))),
            listings.Sum(l => l.ReviewCount),
            listings
                .OrderByDescending(l => l.Rating)
                .Select(l => CatalogService.ToCard(l, favIds))
                .ToList(),
            asHost
                .Select(r => new ProfileReviewDto(
                    r.Id, r.AuthorName, r.AuthorInitials, r.AuthorUserId, r.When, r.Text,
                    Math.Round(r.Rating, 1), r.ListingTitle, r.ListingSlug))
                .ToList(),
            asGuest
                .Select(r =>
                {
                    var author = Profiles.DisplayNameOf(r.AuthorDisplayName, r.AuthorFullName);
                    return new ProfileReviewDto(
                        r.Id, author, Profiles.InitialsOf(author), r.AuthorUserId,
                        Profiles.MonthLabel(r.CreatedAt),
                        r.Text, Math.Round(r.Rating, 1), r.ListingTitle, r.ListingSlug);
                })
                .ToList()) { GuestScores = guestScores });
    }
}
