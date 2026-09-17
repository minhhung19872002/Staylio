using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController(
    AuthService auth, StayHostDbContext db, WalletService wallet, IdentityService identity,
    ExternalTokenVerifier verifier, ExternalLoginSettings externalLogin,
    IWebHostEnvironment env)
    : ControllerBase
{
    /// <summary>204 when nobody is signed in, so clients get an unambiguous empty response.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        return user is null ? NoContent() : Ok(await ToDtoAsync(user, ct));
    }

    /// <summary>Staylio Thân thiết — this account's level and how far the next one is.</summary>
    [HttpGet("loyalty")]
    public async Task<ActionResult<LoyaltyDto>> LoyaltyLevel(
        [FromServices] LoyaltyService loyalty, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        return Ok(await loyalty.SummaryAsync(user.Id, ct));
    }

    [HttpPost("register")]
    public async Task<ActionResult<CurrentUserDto>> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(
            req.Email, req.Password, req.FullName, req.Phone, ct, req.DateOfBirth);
        if (!result.Ok) return BadRequest(new { message = result.Error });

        // A referral code typed at signup, or simply the email somebody invited:
        // either way the new account is linked to whoever brought them here.
        await wallet.ClaimAsync(result.User!, req.ReferralCode, ct);

        return Ok(await ToDtoAsync(result.User!, ct));
    }

    [HttpPost("login")]
    public async Task<ActionResult<CurrentUserDto>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var result = await auth.LoginAsync(req.Email, req.Password, ct);
        if (!result.Ok) return Unauthorized(new { message = result.Error });

        // docs/01 TK-08 — 202 rather than 200: the password was accepted, the
        // login is not finished, and no session cookie came back with this.
        if (result.TwoFactorChallenge is { } challenge)
            return Accepted(await ChallengeDtoAsync(result.User!, challenge, ct));

        return Ok(await ToDtoAsync(result.User!, ct));
    }

    /* ------------------------------------------- docs/01 TK-08: two-factor */

    /// <summary>Finishes a login that stopped for a code.</summary>
    [HttpPost("two-factor")]
    public async Task<ActionResult<CurrentUserDto>> TwoFactor(
        [FromBody] TwoFactorVerifyRequest req, CancellationToken ct)
    {
        var pending = await auth.ReadChallengeAsync(req.Challenge, ct);
        if (pending is null)
            return Unauthorized(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." });

        var check = await identity.ConfirmCodeAsync(pending, pending.TwoFactorKind, req.Code, ct);
        if (!check.Ok) return BadRequest(new { message = check.Error });

        var signedIn = await auth.RedeemChallengeAsync(req.Challenge!, ct);
        if (!signedIn.Ok) return Unauthorized(new { message = signedIn.Error });

        return Ok(await ToDtoAsync(signedIn.User!, ct));
    }

    /// <summary>Sends the code again for a login already waiting on one.</summary>
    [HttpPost("two-factor/resend")]
    public async Task<ActionResult<TwoFactorChallengeDto>> ResendTwoFactor(
        [FromBody] TwoFactorVerifyRequest req, CancellationToken ct)
    {
        var pending = await auth.ReadChallengeAsync(req.Challenge, ct);
        if (pending is null)
            return Unauthorized(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." });

        return Ok(await ChallengeDtoAsync(pending, req.Challenge!, ct));
    }

    [HttpGet("two-factor")]
    public async Task<ActionResult<TwoFactorStateDto>> TwoFactorState(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        return Ok(new TwoFactorStateDto(
            user.TwoFactorEnabled,
            user.TwoFactorKind.ToString(),
            user.TwoFactorEnabled ? Mask(user, user.TwoFactorKind) : null));
    }

    /// <summary>
    /// docs/01 TK-08 — turning it on takes two calls: one with no code, which
    /// sends one, and one with the code. Nobody switches on a second factor
    /// pointed at an address they cannot read.
    /// </summary>
    [HttpPost("two-factor/enable")]
    public async Task<IActionResult> EnableTwoFactor([FromBody] TwoFactorSetupRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var kind = ParseKind(req.Kind);
        var target = kind == IdentifierKind.Phone ? user.Phone : user.Email;
        if (string.IsNullOrWhiteSpace(target))
            return BadRequest(new { message = $"Tài khoản chưa có {Identity.KindLabel(kind)}." });

        if (string.IsNullOrWhiteSpace(req.Code))
        {
            var sent = await identity.SendCodeAsync(user, kind, ct);
            if (!sent.Ok) return BadRequest(new { message = sent.Error });
            return Ok(new { message = $"Đã gửi mã tới {Mask(user, kind)}.", devCode = sent.DevCode });
        }

        var check = await identity.ConfirmCodeAsync(user, kind, req.Code, ct);
        if (!check.Ok) return BadRequest(new { message = check.Error });

        user.TwoFactorEnabled = true;
        user.TwoFactorKind = kind;
        await db.SaveChangesAsync(ct);

        return Ok(new TwoFactorStateDto(true, kind.ToString(), Mask(user, kind)));
    }

    /// <summary>
    /// Turning it off asks for the password, not a code: somebody who walked up
    /// to an unlocked screen should not be able to strip the second factor.
    /// </summary>
    [HttpPost("two-factor/disable")]
    public async Task<IActionResult> DisableTwoFactor([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        if (!PasswordHasher.Verify(req.Password ?? "", user.PasswordHash, user.PasswordSalt))
            return BadRequest(new { message = "Mật khẩu không đúng." });

        user.TwoFactorEnabled = false;
        await db.SaveChangesAsync(ct);
        return Ok(new TwoFactorStateDto(false, user.TwoFactorKind.ToString(), null));
    }

    private async Task<TwoFactorChallengeDto> ChallengeDtoAsync(User user, string challenge, CancellationToken ct)
    {
        var sent = await identity.SendCodeAsync(user, user.TwoFactorKind, ct);
        return new TwoFactorChallengeDto(
            challenge,
            user.TwoFactorKind.ToString(),
            Mask(user, user.TwoFactorKind),
            Identity.CodeLength,
            sent.DevCode);
    }

    /// <summary>
    /// Enough for somebody to recognise their own address and not enough for
    /// anybody else to learn it from a login screen.
    /// </summary>
    private static string Mask(User user, IdentifierKind kind)
    {
        if (kind == IdentifierKind.Phone)
        {
            var phone = user.Phone ?? "";
            return phone.Length < 7 ? "số điện thoại của bạn" : $"{phone[..2]}****{phone[^3..]}";
        }

        var email = user.Email ?? "";
        var at = email.IndexOf('@');
        return at < 1 ? "email của bạn" : $"{email[0]}***{email[at..]}";
    }

    /* ------------------------------------------------- docs/01 TK-01: OTP */

    /// <summary>
    /// Sends a six-digit code to the account's phone or email. In development
    /// the code comes back in the response — there is no SMS provider behind
    /// this build and it is the only way to finish the flow end to end.
    /// </summary>
    [HttpPost("send-code")]
    public async Task<IActionResult> SendCode([FromBody] SendCodeRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var kind = ParseKind(req.Kind);
        var result = await identity.SendCodeAsync(user, kind, ct);

        return result.Ok
            ? Ok(new
            {
                message = $"Đã gửi mã tới {Identity.KindLabel(kind)} của bạn.",
                devCode = result.DevCode
            })
            : BadRequest(new { message = result.Error });
    }

    [HttpPost("confirm-code")]
    public async Task<ActionResult<CurrentUserDto>> ConfirmCode(
        [FromBody] ConfirmCodeRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var result = await identity.ConfirmCodeAsync(user, ParseKind(req.Kind), req.Code, ct);
        if (!result.Ok) return BadRequest(new { message = result.Error });

        return Ok(await ToDtoAsync(user, ct));
    }

    /// <summary>What is confirmed and what is linked, for the account screen.</summary>
    [HttpGet("verification")]
    public async Task<ActionResult<VerificationStateDto>> Verification(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var linked = await identity.LinkedAsync(user.Id, ct);

        return Ok(new VerificationStateDto(
            string.IsNullOrWhiteSpace(user.Email) ? null : user.Email,
            user.EmailConfirmed,
            user.Phone,
            user.PhoneConfirmed,
            Identity.CodeLength,
            (int)Identity.CodeLifetime.TotalMinutes,
            linked.Select(l => new LinkedLoginDto(
                l.Provider.ToString().ToLower(), Identity.ProviderLabel(l.Provider),
                l.ProviderEmail, l.LastUsedAt)).ToList()));
    }

    /* --------------------------------- docs/01 TK-02: Google, Apple, Facebook */

    /// <summary>
    /// What the browser needs before it can put a provider's own button on screen.
    /// The ids are public by design — they end up in the page either way.
    /// </summary>
    [HttpGet("external/config")]
    public ActionResult<ExternalLoginConfigDto> ExternalConfig() =>
        Ok(new ExternalLoginConfigDto(
            externalLogin.HasGoogle ? externalLogin.GoogleClientId : null,
            externalLogin.HasApple ? externalLogin.AppleServicesId : null,
            externalLogin.HasApple ? externalLogin.AppleRedirectUri : null,
            externalLogin.HasFacebook ? externalLogin.FacebookAppId : null));

    [HttpPost("external")]
    public async Task<ActionResult<CurrentUserDto>> External(
        [FromBody] ExternalSignInRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<ExternalProvider>(req.Provider, true, out var provider))
            return BadRequest(new { message = "Chỉ hỗ trợ Google, Apple hoặc Facebook." });

        // Who somebody is comes out of the provider's signed token, never out of
        // the request body. Anything the browser claims about itself is ignored.
        var checkResult = await verifier.VerifyAsync(provider, req.Credential, ct);
        if (!checkResult.Ok) return BadRequest(new { message = checkResult.Error });

        var who = checkResult.Identity!;

        var result = await identity.SignInWithAsync(
            provider, who.Subject,
            // An unconfirmed address must not be allowed to attach itself to an
            // existing account; it is kept as a display detail only.
            who.EmailVerified ? who.Email : null,
            who.FullName, ct);

        if (!result.Ok) return BadRequest(new { message = result.Error });

        if (result.TwoFactorChallenge is { } challenge)
            return Accepted(await ChallengeDtoAsync(result.User!, challenge, ct));

        return Ok(await ToDtoAsync(result.User!, ct));
    }

    [HttpDelete("external/{provider}")]
    public async Task<IActionResult> Unlink(string provider, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        if (!Enum.TryParse<ExternalProvider>(provider, true, out var parsed))
            return BadRequest(new { message = "Nhà cung cấp không hợp lệ." });

        var error = await identity.UnlinkAsync(user, parsed, ct);
        return error is null ? NoContent() : BadRequest(new { message = error });
    }

    private static IdentifierKind ParseKind(string? kind) =>
        string.Equals(kind, "phone", StringComparison.OrdinalIgnoreCase) ? IdentifierKind.Phone
        : string.Equals(kind, "workemail", StringComparison.OrdinalIgnoreCase) ? IdentifierKind.WorkEmail
        : IdentifierKind.Email;

    /* --------------------------------------- docs/01 TK-07: company email */

    /// <summary>
    /// docs/01 TK-07 — start verifying a company email. The address is stored
    /// unconfirmed and a six-digit code is sent to it; a free consumer mailbox is
    /// refused because the badge is about belonging to an organisation.
    /// </summary>
    [HttpPost("work-email")]
    public async Task<IActionResult> SetWorkEmail([FromBody] WorkEmailRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var normalised = WorkEmail.Normalise(req.Email);
        if (WorkEmail.Domain(normalised) is null)
            return BadRequest(new { message = WorkEmail.InvalidMessage() });
        if (!WorkEmail.IsCompanyEmail(normalised))
            return BadRequest(new { message = WorkEmail.FreeProviderMessage() });

        user.WorkEmail = normalised;
        user.WorkEmailConfirmed = false;
        await db.SaveChangesAsync(ct);

        var result = await identity.SendCodeAsync(user, IdentifierKind.WorkEmail, ct);
        return result.Ok
            ? Ok(new { message = $"Đã gửi mã tới {normalised}.", devCode = result.DevCode })
            : BadRequest(new { message = result.Error });
    }

    /// <summary>docs/01 TK-07 — confirm the company email with the code just sent.</summary>
    [HttpPost("work-email/confirm")]
    public async Task<ActionResult<CurrentUserDto>> ConfirmWorkEmail(
        [FromBody] ConfirmCodeRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var result = await identity.ConfirmCodeAsync(user, IdentifierKind.WorkEmail, req.Code, ct);
        if (!result.Ok) return BadRequest(new { message = result.Error });
        return Ok(await ToDtoAsync(user, ct));
    }

    /// <summary>docs/01 TK-07 — remove the company email and its badge.</summary>
    [HttpDelete("work-email")]
    public async Task<ActionResult<CurrentUserDto>> RemoveWorkEmail(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        user.WorkEmail = null;
        user.WorkEmailConfirmed = false;
        await db.SaveChangesAsync(ct);
        return Ok(await ToDtoAsync(user, ct));
    }


    /* ------------------------------------------ docs/02 H1: my reviews */

    /// <summary>
    /// docs/02 H1 — the three groups a person's reviews fall into: the ones
    /// still owed, the ones they wrote, and the ones written about them.
    ///
    /// Every piece of this existed and none of it was gathered anywhere: a guest
    /// could review a stay only from the trip it belonged to, could not read
    /// back what they had written without opening each trip, and could see what
    /// hosts said about them only by visiting their own public profile — a page
    /// built for other people to look at.
    ///
    /// Both sides are answered in one call because one account is often both: a
    /// host owes reviews of their guests under docs/01 ĐG-06 exactly as a guest
    /// owes reviews of the stay under ĐG-01.
    /// </summary>
    [HttpGet("reviews")]
    public async Task<ActionResult<MyReviewsDto>> MyReviews(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var now = DateTime.UtcNow;
        var hostId = await db.Hosts.Where(h => h.UserId == user.Id)
            .Select(h => (int?)h.Id).FirstOrDefaultAsync(ct);

        // docs/01 ĐG-01 — only a completed stay is reviewable, and docs/01 ĐG-02
        // closes the window fourteen days after check-out.
        var completed = await db.Bookings
            .Where(b => b.Status == BookingStatus.Completed
                        && (b.GuestUserId == user.Id
                            || (hostId != null && b.Listing!.HostId == hostId)))
            .OrderByDescending(b => b.CheckOut)
            .Take(200)
            .Select(b => new
            {
                b.Id, b.Reference, b.CheckOut, b.GuestUserId,
                ListingTitle = b.Listing!.Title,
                HostId = b.Listing.HostId,
                Image = b.Listing.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                GuestName = b.GuestUser!.DisplayName ?? b.GuestUser.FullName,
                HostName = b.Listing.Host!.Name,
                HasGuestReview = db.Reviews.Any(r => r.BookingId == b.Id),
                HasHostReview = db.GuestReviews.Any(r => r.BookingId == b.Id)
            })
            .ToListAsync(ct);

        var todo = new List<ReviewTodoDto>();
        foreach (var b in completed)
        {
            var deadline = b.CheckOut.ToDateTime(TimeOnly.MinValue) + ReviewService.Window;
            if (now >= deadline) continue;

            var days = (int)Math.Ceiling((deadline - now).TotalDays);

            if (b.GuestUserId == user.Id && !b.HasGuestReview)
                todo.Add(new ReviewTodoDto(b.Id, b.Reference, b.ListingTitle, b.Image,
                    b.CheckOut, deadline, days, "guest", b.HostName));

            if (hostId != null && b.HostId == hostId && !b.HasHostReview)
                todo.Add(new ReviewTodoDto(b.Id, b.Reference, b.ListingTitle, b.Image,
                    b.CheckOut, deadline, days, "host", b.GuestName));
        }

        // What this person wrote: about a place as a guest, and about a guest as
        // a host. Unpublished ones are theirs to see — they wrote them.
        var mineOfStays = await db.Reviews
            .Where(r => r.AuthorUserId == user.Id)
            .OrderByDescending(r => r.CreatedAt).Take(100)
            .Select(r => new MyReviewDto(
                r.Id, r.BookingId, r.Text, r.Rating, r.When, r.CreatedAt,
                r.Listing!.Title, null,
                r.PublishedAt != null,
                r.PublishedAt == null && (r.EditableUntil == null || r.EditableUntil >= now),
                r.HostReply, null))
            .ToListAsync(ct);

        var mineOfGuests = await db.GuestReviews
            .Where(r => r.HostUserId == user.Id)
            .OrderByDescending(r => r.CreatedAt).Take(100)
            .Select(r => new MyReviewDto(
                r.Id, r.BookingId, r.Text, r.Rating,
                Profiles.MonthLabel(r.CreatedAt), r.CreatedAt,
                r.Booking!.Listing!.Title,
                r.GuestUser!.DisplayName ?? r.GuestUser.FullName,
                r.PublishedAt != null,
                false, null, r.WouldHostAgain))
            .ToListAsync(ct);

        // docs/03 §7 — what others said is only readable once it is public. A
        // blind review shown early to the person it is about is not blind.
        var aboutMeAsGuest = await db.GuestReviews
            .Where(r => r.GuestUserId == user.Id && r.PublishedAt != null)
            .OrderByDescending(r => r.CreatedAt).Take(100)
            .Select(r => new MyReviewDto(
                r.Id, r.BookingId, r.Text, r.Rating,
                Profiles.MonthLabel(r.CreatedAt), r.CreatedAt,
                r.Booking!.Listing!.Title,
                r.HostUser!.DisplayName ?? r.HostUser.FullName,
                true, false, null, r.WouldHostAgain))
            .ToListAsync(ct);

        var aboutMyPlaces = hostId is null
            ? []
            : await db.Reviews
                .Where(r => r.Listing!.HostId == hostId && r.PublishedAt != null)
                .OrderByDescending(r => r.CreatedAt).Take(100)
                .Select(r => new MyReviewDto(
                    r.Id, r.BookingId, r.Text, r.Rating, r.When, r.CreatedAt,
                    r.Listing!.Title, r.AuthorName,
                    true, false, r.HostReply, null))
                .ToListAsync(ct);

        return Ok(new MyReviewsDto(
            todo.OrderBy(x => x.Deadline).ToList(),
            mineOfStays.Concat(mineOfGuests).OrderByDescending(r => r.CreatedAt).ToList(),
            aboutMeAsGuest.Concat(aboutMyPlaces).OrderByDescending(r => r.CreatedAt).ToList()));
    }


    /* ------------------------------------ docs/01 TK-12: pausing an account */

    /// <summary>
    /// docs/01 TK-12 — "tạm vô hiệu hoá hoặc xoá tài khoản". The erase half has
    /// existed since the data-request work; this half had no column, no endpoint
    /// and no button, and the code was ticked anyway because one clause of an
    /// "hoặc" was done.
    ///
    /// Listings come off sale the way a sanction takes them off — by
    /// unpublishing, which <see cref="Availability"/> already refuses to book
    /// against — rather than by a new condition threaded through the six search
    /// queries. One mechanism, already tested, and nothing to keep in sync.
    /// </summary>
    [HttpPost("pause")]
    public async Task<ActionResult<AccountPauseDto>> Pause(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        if (user.PausedAt is not null) return Ok(await PauseStateAsync(user, ct));

        var live = await LiveBookingCountAsync(user, ct);
        // docs/01 TC-01 — money the platform is still holding for this host has
        // to land before they disappear; the payout state lives on the payment,
        // not on the booking.
        var owed = await db.Payments
            .Where(p => p.Booking!.Listing!.Host!.UserId == user.Id
                        && (p.PayoutStatus == PayoutStatus.Scheduled
                            || p.PayoutStatus == PayoutStatus.OnHold
                            || p.PayoutStatus == PayoutStatus.Sent)
                        && p.Status == PaymentStatus.Captured)
            .SumAsync(p => (decimal?)p.HostPayout, ct) ?? 0m;

        var check = AccountPause.CanPause(user.IsSuspended, user.IsBanned, live, owed);
        if (!check.Ok) return BadRequest(new { message = check.Message, reason = check.Reason.ToString() });

        var now = DateTime.UtcNow;
        user.PausedAt = now;

        var host = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == user.Id, ct);
        if (host is not null)
        {
            foreach (var l in await db.Listings
                         .Where(l => l.HostId == host.Id && l.IsPublished).ToListAsync(ct))
            {
                l.IsPublished = false;
                l.HiddenByPauseAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(await PauseStateAsync(user, ct));
    }

    /// <summary>
    /// docs/01 TK-12 — coming back. Signing in does this on its own
    /// (<see cref="AccountPause.ResumesOnSignIn"/>); this is the same gesture for
    /// somebody already signed in who changed their mind on the settings page.
    /// </summary>
    [HttpPost("resume")]
    public async Task<ActionResult<AccountPauseDto>> Resume(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        await ResumeAsync(db, user, ct);
        await db.SaveChangesAsync(ct);
        return Ok(await PauseStateAsync(user, ct));
    }

    [HttpGet("pause")]
    public async Task<ActionResult<AccountPauseDto>> PauseState(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        return Ok(await PauseStateAsync(user, ct));
    }

    /// <summary>
    /// Lifts a pause and puts back exactly the listings the pause took down.
    ///
    /// A listing a sanction is also holding down stays down: resuming your own
    /// pause must not undo somebody else's decision (docs/08 §5.5).
    /// </summary>
    internal static async Task ResumeAsync(StayHostDbContext db, User user, CancellationToken ct)
    {
        if (user.PausedAt is null) return;
        user.PausedAt = null;

        var host = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == user.Id, ct);
        if (host is null) return;

        foreach (var l in await db.Listings
                     .Where(l => l.HostId == host.Id && l.HiddenByPauseAt != null).ToListAsync(ct))
        {
            l.HiddenByPauseAt = null;
            if (l.HiddenBySanctionAt is null) l.IsPublished = true;
        }
    }

    /// <summary>
    /// Stays that would be left without one of their two sides. Counted for both
    /// roles because one account is often both.
    /// </summary>
    private async Task<int> LiveBookingCountAsync(User user, CancellationToken ct)
    {
        BookingStatus[] live =
        [
            BookingStatus.PendingHostApproval, BookingStatus.PendingPayment,
            BookingStatus.Confirmed, BookingStatus.InProgress
        ];

        return await db.Bookings.CountAsync(
            b => live.Contains(b.Status)
                 && (b.GuestUserId == user.Id || b.Listing!.Host!.UserId == user.Id), ct);
    }

    private async Task<AccountPauseDto> PauseStateAsync(User user, CancellationToken ct)
    {
        var live = await LiveBookingCountAsync(user, ct);
        var check = AccountPause.CanPause(user.IsSuspended, user.IsBanned, live, 0m);

        var hidden = await db.Listings.CountAsync(
            l => l.HiddenByPauseAt != null && l.Host!.UserId == user.Id, ct);

        return new AccountPauseDto(
            user.PausedAt is not null, user.PausedAt, hidden, live,
            user.PausedAt is null && check.Ok,
            user.PausedAt is null && !check.Ok ? check.Message : null,
            AccountPause.Notice);
    }

    /* --------------------------------------- docs/01 TK-09: tuỳ chỉnh */

    /// <summary>
    /// docs/01 TK-09 (P0) — saves "ngôn ngữ, tiền tệ, múi giờ" on the account,
    /// where they survive a new device. A deliberate endpoint of its own rather
    /// than a ride on the profile PUT: that handler assigns every field it
    /// knows, so a partial write through it would null the rest.
    ///
    /// Invalid values are refused by name, never treated as a clear — a typo
    /// that silently erased a preference would fail with no witness. What is
    /// NOT here: nothing server-side reads Language yet. The emails are still
    /// composed in Vietnamese; storing the choice is the half being shipped,
    /// and the columns say so out loud.
    /// </summary>
    [HttpPut("preferences")]
    public async Task<ActionResult<CurrentUserDto>> SavePreferences(
        [FromBody] SavePreferencesRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var wantLang = (req.Language ?? "").Trim();
        if (wantLang.Length > 0 && Locales.Language(wantLang) is null)
            return BadRequest(new { message = $"Ngôn ngữ '{wantLang}' không nằm trong 8 thứ tiếng được hỗ trợ." });

        var wantCurrency = (req.Currency ?? "").Trim().ToUpperInvariant();
        if (wantCurrency.Length > 0
            && !await db.ExchangeRates.AnyAsync(r => r.Code == wantCurrency && r.IsActive, ct))
            return BadRequest(new { message = $"Tiền tệ '{wantCurrency}' không có trong danh sách đang bán." });

        var wantZone = (req.TimeZoneId ?? "").Trim();
        if (wantZone.Length > 0 && Locales.TimeZone(wantZone) is null)
            return BadRequest(new { message = $"Múi giờ '{wantZone}' không hợp lệ." });

        user.Language = wantLang.Length == 0 ? null : Locales.Language(wantLang);
        user.Currency = wantCurrency.Length == 0 ? null : wantCurrency;
        user.TimeZoneId = wantZone.Length == 0 ? null : Locales.TimeZone(wantZone);

        await db.SaveChangesAsync(ct);
        return Ok(await ToDtoAsync(user, ct));
    }

    /* ------------------------------------- docs/02 F1: lịch sử trả tiền */

    /// <summary>
    /// docs/02 F1 — every payment this account has made, across the three lines
    /// of business the platform keeps in three separate tables, plus gift cards.
    ///
    /// Read-only in the strictest sense: every amount is returned exactly as
    /// stored, never recomputed on the way out. A history that re-derives a
    /// total through today's Pricing is a history that changes when the rules
    /// do, and docs/00 §6.2 says a receipt must still add up years later.
    /// </summary>
    [HttpGet("payments")]
    public async Task<ActionResult<IReadOnlyList<PaymentHistoryRowDto>>> PaymentHistory(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var rows = new List<PaymentHistoryRowDto>();

        // Stays: the Payment row is the money record. Only states where money
        // actually moved belong on a payment history — a pending hold is a
        // booking screen's business, not a transaction that happened.
        var stays = await db.Payments
            .Where(p => p.Booking!.GuestUserId == user.Id
                        && (p.Status == PaymentStatus.Captured || p.Status == PaymentStatus.Refunded))
            .Select(p => new
            {
                p.BookingId, p.Amount, p.Method, p.CardLast4, p.Status,
                p.CapturedAt, p.CreatedAt,
                p.Booking!.Reference,
                Title = p.Booking.Listing!.Title,
            })
            .ToListAsync(ct);

        rows.AddRange(stays.Select(p => new PaymentHistoryRowDto(
            "stay", p.Reference, p.Title, p.Amount, p.Method, p.CardLast4,
            Payments.StatusLabel(p.Status), p.CapturedAt ?? p.CreatedAt, p.BookingId)));

        // Experiences and services carry their money on the booking row itself —
        // Payment.BookingId is a foreign key to stays alone, which is the same
        // separation ledger_entries keeps with its three subject columns.
        var experiences = await db.ExperienceBookings
            .Where(b => b.GuestUserId == user.Id)
            .Select(b => new { b.Reference, b.Total, b.Status, b.CreatedAt, Title = b.Slot!.Experience!.Title })
            .ToListAsync(ct);

        rows.AddRange(experiences.Select(b => new PaymentHistoryRowDto(
            "experience", b.Reference, b.Title, b.Total, "", null,
            ExperienceRules.StatusLabel(b.Status), b.CreatedAt, null)));

        var services = await db.ServiceBookings
            .Where(b => b.GuestUserId == user.Id)
            .Select(b => new { b.Reference, b.Total, b.Status, b.CreatedAt, Title = b.Offering!.Title })
            .ToListAsync(ct);

        rows.AddRange(services.Select(b => new PaymentHistoryRowDto(
            "service", b.Reference, b.Title, b.Total, "", null,
            ServiceRules.StatusLabel(b.Status), b.CreatedAt, null)));

        // Gift cards this account bought. One that was never paid for took no
        // money and does not belong on a payment history.
        var cards = await db.GiftCards
            .Where(g => g.PurchasedByUserId == user.Id
                        && g.Status != GiftCardStatus.AwaitingPayment
                        && g.Status != GiftCardStatus.Cancelled)
            .Select(g => new { g.Code, g.Amount, g.Status, g.CreatedAt })
            .ToListAsync(ct);

        rows.AddRange(cards.Select(g => new PaymentHistoryRowDto(
            "gift-card", g.Code, "Thẻ quà tặng", g.Amount, "", null,
            CreditRules.StatusLabel(g.Status), g.CreatedAt, null)));

        return Ok(rows.OrderByDescending(r => r.At).ToList());
    }

    /* --------------------------------------- docs/01 TM-23: saved searches */

    /// <summary>docs/01 TM-23 — the searches this account asked to be alerted about.</summary>
    [HttpGet("saved-searches")]
    public async Task<ActionResult<IReadOnlyList<SavedSearchDto>>> SavedSearches(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var rows = await db.SavedSearches
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return Ok(rows.Select(s => new SavedSearchDto(s.Id, s.Label, SavedSearchSummary(s), s.CreatedAt)).ToList());
    }

    /// <summary>
    /// docs/01 TM-23 — save the current search. The high-water mark starts at the
    /// newest listing that exists now, so only places added afterwards raise an
    /// alert; the guest is not told about the whole catalogue the moment they save.
    /// </summary>
    [HttpPost("saved-searches")]
    public async Task<ActionResult<SavedSearchDto>> SaveSearch([FromBody] SaveSearchRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        if (await db.SavedSearches.CountAsync(s => s.UserId == user.Id, ct) >= 30)
            return BadRequest(new { message = "Bạn đã lưu tối đa 30 bộ tìm kiếm." });

        var newest = await db.Listings.MaxAsync(l => (int?)l.Id, ct) ?? 0;

        var search = new SavedSearch
        {
            UserId = user.Id,
            Label = string.IsNullOrWhiteSpace(req.Label) ? "Tìm kiếm đã lưu" : req.Label.Trim(),
            Q = Trim(req.Q, 200),
            Category = Trim(req.Category, 40),
            MinPrice = req.MinPrice,
            MaxPrice = req.MaxPrice,
            Guests = Math.Max(0, req.Guests),
            AmenitiesCsv = req.Amenities is { Count: > 0 } ? string.Join(',', req.Amenities.Take(20)) : null,
            RoomType = Trim(req.RoomType, 20),
            Bedrooms = Math.Max(0, req.Bedrooms),
            SuperhostOnly = req.SuperhostOnly,
            InstantBookOnly = req.InstantBookOnly,
            HostLanguagesCsv = req.HostLanguages is { Count: > 0 } ? string.Join(',', req.HostLanguages.Take(12)) : null,
            LastNotifiedListingId = newest
        };
        db.SavedSearches.Add(search);
        await db.SaveChangesAsync(ct);

        return Ok(new SavedSearchDto(search.Id, search.Label, SavedSearchSummary(search), search.CreatedAt));
    }

    [HttpDelete("saved-searches/{id:int}")]
    public async Task<IActionResult> DeleteSavedSearch(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var search = await db.SavedSearches.FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id, ct);
        if (search is not null) { db.SavedSearches.Remove(search); await db.SaveChangesAsync(ct); }
        return NoContent();
    }

    private static string? Trim(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, max)];

    private static string SavedSearchSummary(SavedSearch s)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.Q)) parts.Add($"\"{s.Q}\"");
        if (!string.IsNullOrWhiteSpace(s.Category) && s.Category != "all") parts.Add(s.Category!);
        if (s.Guests > 0) parts.Add($"{s.Guests} khách");
        if (s.Bedrooms > 0) parts.Add($"{s.Bedrooms}+ phòng ngủ");
        if (s.MinPrice is > 0 || s.MaxPrice is > 0)
            parts.Add($"{s.MinPrice ?? 0:#,##0}–{(s.MaxPrice is > 0 ? s.MaxPrice.Value.ToString("#,##0") : "…")}₫");
        if (s.SuperhostOnly) parts.Add("Siêu chủ nhà");
        if (s.InstantBookOnly) parts.Add("Đặt ngay");
        return parts.Count > 0 ? string.Join(" · ", parts) : "Tất cả chỗ nghỉ";
    }

    /* ------------------------------------------- docs/01 AT-10: block list */

    /// <summary>docs/01 AT-10 — the people this account has blocked.</summary>
    [HttpGet("blocks")]
    public async Task<ActionResult<IReadOnlyList<BlockedUserDto>>> Blocks(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var rows = await db.UserBlocks
            .Where(b => b.BlockerUserId == user.Id)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new BlockedUserDto(
                b.BlockedUserId,
                Profiles.DisplayNameOf(b.Blocked!.DisplayName, b.Blocked.FullName),
                b.Blocked!.Initials, b.Blocked.AvatarUrl, b.CreatedAt))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>docs/01 AT-10 — block a user; neither side can message the other after.</summary>
    [HttpPost("blocks")]
    public async Task<IActionResult> Block([FromBody] BlockRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        if (req.UserId == user.Id) return BadRequest(new { message = StayHost.Domain.Blocks.CannotBlockSelf() });

        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId, ct);
        if (target is null) return NotFound(new { message = "Không tìm thấy người dùng." });

        var already = await db.UserBlocks.AnyAsync(
            b => b.BlockerUserId == user.Id && b.BlockedUserId == req.UserId, ct);
        if (!already)
        {
            db.UserBlocks.Add(new UserBlock { BlockerUserId = user.Id, BlockedUserId = req.UserId });
            await db.SaveChangesAsync(ct);
        }
        return Ok(new { message = StayHost.Domain.Blocks.Blocked() });
    }

    /// <summary>docs/01 AT-10 — lift a block this account raised.</summary>
    [HttpDelete("blocks/{userId:int}")]
    public async Task<IActionResult> Unblock(int userId, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var block = await db.UserBlocks
            .FirstOrDefaultAsync(b => b.BlockerUserId == user.Id && b.BlockedUserId == userId, ct);
        if (block is not null)
        {
            db.UserBlocks.Remove(block);
            await db.SaveChangesAsync(ct);
        }
        return Ok(new { message = StayHost.Domain.Blocks.Unblocked() });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(ct);
        return NoContent();
    }

    [HttpPut("profile")]
    public async Task<ActionResult<CurrentUserDto>> UpdateProfile([FromBody] UpdateProfileRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        if (!string.IsNullOrWhiteSpace(req.FullName)) user.FullName = req.FullName.Trim();

        // A phone number here is also a way to sign in and, for some accounts,
        // where the second factor goes. Changing it used to keep "đã xác minh"
        // on the new number, so whoever held a session could point the codes at
        // their own phone. It also was stored as typed, which the sign-in lookup
        // never matched, and a number already on another account threw a 500.
        var phone = string.IsNullOrWhiteSpace(req.Phone) ? null : Identity.NormalisePhone(req.Phone);
        if (!string.IsNullOrWhiteSpace(req.Phone) && phone is null)
            return BadRequest(new { message = "Số điện thoại không hợp lệ." });

        if (phone != user.Phone)
        {
            if (user.TwoFactorEnabled && user.TwoFactorKind == IdentifierKind.Phone)
                return BadRequest(new
                {
                    message = "Mã bảo mật 2 lớp đang gửi về số này. Chuyển bảo mật 2 lớp sang email trước khi đổi số."
                });

            if (phone is not null && await db.Users.AnyAsync(u => u.Id != user.Id && u.Phone == phone, ct))
                return BadRequest(new { message = "Số điện thoại này đã thuộc tài khoản khác." });

            user.Phone = phone;
            user.PhoneConfirmed = false;
        }

        // docs/01 TK-04 — everything below is free text somebody typed, so it is
        // trimmed and capped here rather than trusted at the length the browser
        // happened to allow.
        user.Bio = Profiles.TidyBio(req.Bio);
        user.DisplayName = Profiles.Tidy(req.DisplayName, Profiles.LineMax);
        user.Location = Profiles.Tidy(req.Location, Profiles.LineMax);
        user.Occupation = Profiles.Tidy(req.Occupation, Profiles.LineMax);
        user.SpokenLanguages = Profiles.PackLanguages(req.Languages);
        user.Interests = Profiles.PackInterests(req.Interests) is { Length: > 0 } packed ? packed : null;

        // docs/01 TK-13 — emergency contact, trimmed and capped like the rest.
        user.EmergencyContactName = Profiles.Tidy(req.EmergencyContactName, Profiles.LineMax);
        user.EmergencyContactPhone = Profiles.Tidy(req.EmergencyContactPhone, Profiles.LineMax);
        user.EmergencyContactRelation = Profiles.Tidy(req.EmergencyContactRelation, Profiles.LineMax);

        if (string.IsNullOrWhiteSpace(req.AvatarUrl)) user.AvatarUrl = null;
        else if (Profiles.IsOwnUpload(req.AvatarUrl)) user.AvatarUrl = req.AvatarUrl.Trim();
        else return BadRequest(new { message = "Ảnh đại diện phải là ảnh bạn vừa tải lên." });

        // The grey circle stands in for the photo, so it has to spell the name
        // people actually see — otherwise "Hưng" sits next to a circle reading KD.
        var shown = Profiles.DisplayNameOf(user.DisplayName, user.FullName);
        user.Initials = Profiles.InitialsOf(shown);

        // The host card on a listing shows the same person, so it follows.
        var host = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == user.Id, ct);
        if (host is not null)
        {
            host.Name = shown;
            host.Initials = user.Initials;
            host.Bio = user.Bio;
            host.AvatarUrl = user.AvatarUrl;
        }

        await db.SaveChangesAsync(ct);
        return Ok(await ToDtoAsync(user, ct));
    }

    /// <summary>docs/01 TK-04 — the spoken languages the editor may offer.</summary>
    [HttpGet("profile-options")]
    public ActionResult<IReadOnlyList<SpokenLanguageDto>> ProfileOptions() =>
        Ok(Profiles.SpokenLanguages.Select(l => new SpokenLanguageDto(l.Code, l.Label)).ToList());

    /* --------------------------------------- docs/01 TK-06: who you are */

    /// <summary>The person's own view: status and reason, never the images back again.</summary>
    [HttpGet("identity")]
    public async Task<ActionResult<IdentityCheckDto?>> IdentityStatus(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var latest = await LatestCheckAsync(user.Id, ct);
        return latest is null ? NoContent() : Ok(ToDto(latest));
    }

    [HttpPost("identity")]
    public async Task<ActionResult<IdentityCheckDto>> SubmitIdentity(
        [FromBody] IdentityCheckRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var document = Enum.TryParse<IdentityDocument>(req.Document, true, out var parsed)
            ? parsed
            : IdentityDocument.NationalId;

        var latest = await LatestCheckAsync(user.Id, ct);
        var check = IdentityChecks.CanSubmit(
            latest, document, req.FrontImageUrl, req.BackImageUrl, req.SelfieImageUrl);

        if (!check.Ok) return BadRequest(new { message = check.Message });

        var row = new IdentityCheck
        {
            UserId = user.Id,
            Document = document,
            DocumentLast4 = IdentityChecks.Last4(req.DocumentNumber),
            FrontImageUrl = req.FrontImageUrl!.Trim(),
            BackImageUrl = IdentityChecks.NeedsBackImage(document) ? req.BackImageUrl!.Trim() : null,
            SelfieImageUrl = req.SelfieImageUrl!.Trim()
        };

        db.IdentityChecks.Add(row);
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(row));
    }

    private Task<IdentityCheck?> LatestCheckAsync(int userId, CancellationToken ct) =>
        db.IdentityChecks
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.SubmittedAt).ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);

    private static IdentityCheckDto ToDto(IdentityCheck c) => new(
        c.Id,
        c.Document.ToString(),
        IdentityChecks.DocumentLabel(c.Document),
        c.DocumentLast4,
        c.Status.ToString(),
        IdentityChecks.StatusLabel(c.Status),
        IdentityChecks.BadgeClass(c.Status),
        c.Note,
        c.SubmittedAt,
        c.DecidedAt);

    /* ------------------------------------------------------ docs/01 TK-10 */

    [HttpGet("notifications")]
    public async Task<ActionResult<NotificationPrefsDto>> NotificationPreferences(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        return Ok(BuildPrefs(user.NotificationMask));
    }

    /// <summary>
    /// One cell at a time. A whole-matrix PUT would let a stale tab overwrite
    /// changes made on a phone a minute ago with what it happened to be showing.
    /// </summary>
    [HttpPut("notifications")]
    public async Task<ActionResult<NotificationPrefsDto>> UpdateNotificationPreference(
        [FromBody] UpdateNotificationPrefRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        if (!Enum.TryParse<NotificationTopic>(req.Topic, true, out var topic) ||
            !Enum.TryParse<NotificationChannel>(req.Channel, true, out var channel))
            return BadRequest(new { message = "Không có loại thông báo hoặc kênh này." });

        // docs/03 §11 — a cell that may not be turned off simply does not move,
        // rather than the request failing: the screen already shows it locked.
        user.NotificationMask = NotificationPrefs.With(user.NotificationMask, topic, channel, req.On);
        await db.SaveChangesAsync(ct);

        return Ok(BuildPrefs(user.NotificationMask));
    }

    private static NotificationPrefsDto BuildPrefs(int mask) => new(
        NotificationPrefs.Channels.Select(c => c.ToString()).ToList(),
        NotificationPrefs.Channels.Select(NotificationPrefs.ChannelLabel).ToList(),
        NotificationPrefs.Topics.Select(topic => new NotificationRowDto(
            topic.ToString(),
            NotificationPrefs.TopicLabel(topic),
            NotificationPrefs.TopicNote(topic),
            NotificationPrefs.Channels.Select(channel => new NotificationCellDto(
                channel.ToString(),
                NotificationPrefs.ChannelLabel(channel),
                NotificationPrefs.IsOn(mask, topic, channel),
                !NotificationPrefs.CanTurnOff(topic, channel))).ToList())).ToList());

    /// <summary>Turns a guest account into a host account without publishing anything yet.</summary>
    [HttpPost("become-host")]
    public async Task<ActionResult<CurrentUserDto>> BecomeHost(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        await auth.EnsureHostProfileAsync(user, ct);
        return Ok(await ToDtoAsync(user, ct));
    }

    /* ------------------------------------------------------------ password */

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var result = await auth.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword, ct);
        return result.Ok ? NoContent() : BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// Always reports success so the endpoint cannot be used to discover which
    /// emails exist. In this build the link is returned directly instead of mailed.
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<ActionResult<object>> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        var token = await auth.BeginPasswordResetAsync(req.Email, ct);

        if (token is not null)
        {
            var user = await db.Users.FirstOrDefaultAsync(
                u => u.Email == req.Email.Trim().ToLowerInvariant(), ct);

            if (user is not null)
            {
                // docs/01 TK-09 — hand-translated template, never the machine
                // path: this body carries the reset token, and a translator
                // that reshapes a URL is an account nobody can recover.
                db.EmailMessages.Add(new EmailMessage
                {
                    ToEmail = user.Email,
                    ToName = user.FullName,
                    Subject = Emails.ResetSubject(user.Language),
                    Body = Emails.ResetBody(user.Language, $"/reset-password?token={token}"),
                    Language = user.Language
                });
                await db.SaveChangesAsync(ct);
            }
        }

        // The link goes to the mailbox and nowhere else. Handing it back in the
        // response made this endpoint a takeover tool: anyone could post any
        // address, read the token out of the reply, set a new password and sign
        // in. Development keeps it so the flow can be walked without a mail
        // server; an admin who needs to reset somebody has a route of their own
        // in UserAdminController, which is permissioned, reasoned and audited.
        return Ok(new
        {
            message = "Nếu email tồn tại, chúng tôi đã gửi liên kết đặt lại mật khẩu.",
            resetLink = token is not null && env.IsDevelopment() ? $"/reset-password?token={token}" : null
        });
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult<CurrentUserDto>> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        var result = await auth.CompletePasswordResetAsync(req.Token, req.NewPassword, ct);
        if (!result.Ok) return BadRequest(new { message = result.Error });

        if (result.TwoFactorChallenge is { } challenge)
            return Accepted(await ChallengeDtoAsync(result.User!, challenge, ct));

        return Ok(await ToDtoAsync(result.User!, ct));
    }

    /* -------------------------------------------------------- verification */

    [HttpPost("send-verification")]
    public async Task<ActionResult<object>> SendVerification(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        if (user.EmailConfirmed) return Ok(new { message = "Email đã được xác minh." });

        var token = await auth.BeginEmailVerificationAsync(user, ct);
        var link = $"/verify-email?token={token}";

        // The link proves the mailbox, so it goes to the mailbox. It used to come
        // back in this response and nowhere else — no mail was queued at all — so
        // anyone signed in could "confirm" an address they did not own, and with
        // it claim a co-host invitation sent to that address. Development keeps
        // it in the response so the flow can be walked without a mail server.
        db.EmailMessages.Add(new EmailMessage
        {
            ToEmail = user.Email,
            ToName = user.FullName,
            Subject = "Xác minh email Staylio",
            Body = Emails.Compose(user.Language, user.FullName,
                "Xác minh địa chỉ email của bạn", "Mở liên kết dưới đây để xác minh. Liên kết có hiệu lực 3 ngày.",
                link),
            Language = user.Language
        });
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            message = "Đã gửi liên kết xác minh tới email của bạn.",
            verifyLink = env.IsDevelopment() ? link : null
        });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest req, CancellationToken ct)
    {
        var result = await auth.ConfirmEmailAsync(req.Token, ct);
        return result.Ok ? NoContent() : BadRequest(new { message = result.Error });
    }

    /* ------------------------------------------------------------- devices */

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<SessionDto>>> Sessions(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var current = auth.CurrentToken();
        var sessions = await auth.ActiveSessionsAsync(user.Id, ct);

        return Ok(sessions.Select(s => new SessionDto(
            s.Id, DescribeDevice(s.UserAgent), s.CreatedAt, s.ExpiresAt, s.Token == current)).ToList());
    }

    [HttpDelete("sessions/{id:int}")]
    public async Task<IActionResult> RevokeSession(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        return await auth.RevokeSessionAsync(user.Id, id, ct) ? NoContent() : NotFound();
    }

    private static string DescribeDevice(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Thiết bị không xác định";

        var os = userAgent.Contains("Windows") ? "Windows"
            : userAgent.Contains("Mac OS") ? "macOS"
            : userAgent.Contains("Android") ? "Android"
            : userAgent.Contains("iPhone") || userAgent.Contains("iPad") ? "iOS"
            : userAgent.Contains("Linux") ? "Linux" : "Khác";

        var browser = userAgent.Contains("Edg/") ? "Edge"
            : userAgent.Contains("Chrome/") ? "Chrome"
            : userAgent.Contains("Firefox/") ? "Firefox"
            : userAgent.Contains("Safari/") ? "Safari" : "Trình duyệt khác";

        // "Chrome · Windows" rather than "Chrome trên Windows": the two names are
        // proper nouns the dictionary passes straight through, and joining them
        // with a word would need a phrase whose order differs per language. A
        // separator reads the same everywhere and leaves nothing to translate.
        return $"{browser} · {os}";
    }

    private async Task<CurrentUserDto> ToDtoAsync(User user, CancellationToken ct)
    {
        var host = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == user.Id, ct);
        var listingCount = host is null ? 0 : await db.Listings.CountAsync(l => l.HostId == host.Id, ct);
        var unread = await db.Messages
            .CountAsync(m => m.SenderUserId != user.Id && m.ReadAt == null &&
                             (m.Thread!.GuestUserId == user.Id || m.Thread.HostUserId == user.Id), ct);

        return new CurrentUserDto(
            user.Id, user.Email, user.FullName, user.Initials, user.Phone, user.Bio,
            user.Role.ToString(), host is not null, host?.Id, listingCount, unread,
            user.EmailConfirmed,
            Profiles.JoinedLabel(user.CreatedAt),
            user.PhoneConfirmed,
            user.DisplayName,
            user.AvatarUrl,
            Profiles.UnpackLanguages(user.SpokenLanguages),
            user.Location,
            user.Occupation,
            Profiles.UnpackInterests(user.Interests),
            user.WorkEmail,
            user.WorkEmailConfirmed,
            user.EmergencyContactName,
            user.EmergencyContactPhone,
            user.EmergencyContactRelation,
            user.JourneyVisibility.ToString(),
            user.Language,
            user.Currency,
            user.TimeZoneId);
    }
}
