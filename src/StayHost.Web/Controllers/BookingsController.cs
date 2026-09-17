using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;
using StayHost.Web.Services.Gateways;

namespace StayHost.Web.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController(
    StayHostDbContext db, AuthService auth, NotificationService notifications,
    CatalogService catalog, BookingService rules, ReviewService reviews, ThreadMessenger messenger,
    PaymentGateway gateway, RiskWatch risk, WalletService wallet, PaymentCompletion completion,
    CouponService coupons, ExperienceService experiences, ServiceMarketService market,
    PspRouter psp, PspCheckout pspCheckout, DataSecrets secrets, RefundGateway refunds)
    : ControllerBase
{
    /// <summary>
    /// The exclusion constraint added by the DoubleBookingGuard migration. It is
    /// the only thing that can decide between two simultaneous checkouts, so its
    /// violation is a normal outcome, not a server error.
    /// </summary>
    internal static bool IsOverlapViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("bookings_no_overlap", StringComparison.OrdinalIgnoreCase) == true;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BookingDto>>> List(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        var sid = HttpContext.SessionId();

        var bookings = await db.Bookings
            .Where(b => user != null ? b.GuestUserId == user.Id : b.SessionId == sid && b.GuestUserId == null)
            .Include(b => b.Listing!).ThenInclude(l => l.Images)
            .Include(b => b.Listing!).ThenInclude(l => l.Host)
            .Include(b => b.Payment)
            .Include(b => b.Events)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

        return Ok(bookings.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create([FromBody] CreateBookingRequest req, CancellationToken ct)
    {
        var listing = await db.Listings
            .Include(l => l.Images)
            .Include(l => l.Host)
            .FirstOrDefaultAsync(l => l.Id == req.ListingId, ct);
        if (listing is null) return NotFound(new { message = "Chỗ nghỉ không tồn tại." });

        // docs/03 §2 rule 2: infants count towards neither capacity nor price.
        var party = req.Adults is null
            ? PartySize.Of(req.Guests) with { Infants = req.Infants, Pets = req.Pets }
            : new PartySize(Math.Max(1, req.Adults.Value), req.Children, req.Infants, req.Pets);

        // The nine checks of docs/03 §2, in order, stopping at the first failure.
        var check = await rules.CheckAsync(
            listing, req.CheckIn, req.CheckOut, party, ct, roomTypeId: req.RoomTypeId);
        if (!check.Ok)
        {
            return check.Reason is Availability.Reason.DatesTaken or Availability.Reason.TurnoverTime
                ? Conflict(new { message = check.Message, reason = check.Reason.ToString() })
                : BadRequest(new { message = check.Message, reason = check.Reason.ToString() });
        }

        /*
         * docs/07 §2.5 — a booking does not need an account behind it.
         *
         * It used to: this returned 401 and said so. The rest of the platform was
         * already built for the other case — every booking carries a SessionId,
         * the trips list reads by session, and signing in adopts what the session
         * was holding — so the only thing standing between a stranger and a
         * booking was this line.
         *
         * What an account brings is still required where it is genuinely needed;
         * GuestCheckout names each refusal rather than quietly dropping it.
         */
        var user = await auth.CurrentUserAsync(ct);

        if (user is null)
        {
            var anonymous = GuestCheckout.CanBookAnonymously(
                new GuestCheckout.Contact(req.GuestName, req.GuestEmail, req.GuestPhone),
                listing.RequireGuestPhoto, listing.RequireVerifiedToBook,
                req.UseCredit, !string.IsNullOrWhiteSpace(req.CouponCode), req.OfferId is not null);

            if (!anonymous.Ok)
                return BadRequest(new { message = anonymous.Message, reason = anonymous.Reason.ToString() });
        }
        else
        {
            // docs/08 §5.2 — the restriction blocks the behaviour, not the account.
            if (Restrictions.Has(user.RestrictionMask, RestrictionKind.NoNewBookings))
                return StatusCode(403, new { message = Restrictions.Message(RestrictionKind.NoNewBookings) });

            // docs/08 §6 — a suspended account kept open for a dispute is open for
            // the dispute, not for booking holidays.
            if (user.IsSuspended)
                return StatusCode(403, new { message = "Tài khoản đang bị tạm khoá nên không đặt chỗ mới được." });
        }

        // docs/01 ĐP-10 — the host's hard preconditions. Unlike the instant-book
        // conditions these do not fall back to a request: the host asked that
        // nobody stay without meeting them. A private offer bypasses them, since
        // the host chose this guest directly.
        if (req.OfferId is null)
        {
            var hasRules = !string.IsNullOrWhiteSpace(listing.HouseRules);

            // docs/07 §11 step 6 — "gắn cờ, yêu cầu xác minh cho các đơn sau".
            // The flag is raised when arbitration lands; this is the "các đơn
            // sau" half, which had nowhere to live until now.
            // A flag is raised against an account; somebody with none carries no
            // history, which is exactly why GuestCheckout refuses them on a
            // listing whose host asked for a verified guest.
            var flaggedForChargebacks = user is not null && await db.RiskFlags.AnyAsync(
                f => f.UserId == user.Id
                     && f.Kind == RiskKind.RepeatChargebacks
                     && f.Status == RiskFlagStatus.Open, ct);

            var precheck = BookingPreconditions.Check(
                listing.RequireGuestPhoto, listing.RequireVerifiedToBook,
                !string.IsNullOrWhiteSpace(user?.AvatarUrl), user?.IsIdentityVerified ?? false,
                hasRules, req.AgreedToRules,
                platformRequiresVerified: flaggedForChargebacks);
            if (!precheck.Ok) return BadRequest(new { message = precheck.Error });
        }

        // docs/01 ĐP-17 — booking a host's private offer. The offer must be this
        // guest's, still live, and for these exact listing and dates, or its price
        // could be borrowed for a different stay. Its rate then drives the quote.
        SpecialOffer? offer = null;
        if (req.OfferId is { } offerId)
        {
            offer = await db.SpecialOffers.FirstOrDefaultAsync(o => o.Id == offerId, ct);
            var now = DateTime.UtcNow;
            if (offer is null || user is null || offer.GuestUserId != user.Id || !SpecialOffers.IsLive(offer, now))
                return BadRequest(new { message = "Ưu đãi này không còn áp dụng được." });
            if (offer.ListingId != listing.Id || offer.CheckIn != req.CheckIn || offer.CheckOut != req.CheckOut)
                return BadRequest(new { message = "Ưu đãi không khớp với chỗ nghỉ hoặc ngày đã chọn." });
        }

        // Quoting and booking go through the same builder so the guest is charged
        // exactly what the room page showed them (docs/00 §6.8).
        var quoteRequest = await catalog.BuildQuoteRequestAsync(
            listing.Id, req.CheckIn, req.CheckOut, party, ct,
            roomTypeId: req.RoomTypeId, nightlyOverride: offer?.NightlyRate);

        // docs/01 ĐP-09 — a promo code first, evaluated against the stay's total
        // before any reduction. It is refused loudly rather than silently ignored:
        // a guest who typed a code and saw full price would think the site was
        // broken. Applied before balance so the code spares the balance.
        Coupons.Check couponCheck = new(false);
        if (!string.IsNullOrWhiteSpace(req.CouponCode))
        {
            var dry = Pricing.Quote(quoteRequest!);
            var gross = dry.Subtotal + dry.GuestServiceFee + dry.Tax;
            couponCheck = await coupons.EvaluateAsync(req.CouponCode, user!.Id, gross, DateTime.UtcNow, ct: ct);
            if (!couponCheck.Ok)
                return BadRequest(new { message = couponCheck.Error });

            quoteRequest = quoteRequest! with
            {
                CouponAmount = couponCheck.Discount,
                CouponLabel = couponCheck.Label ?? "Mã giảm giá"
            };
        }

        // Balance comes off the room charge, never off the fees or the tax: it
        // is money towards a stay, not a discount on what is owed elsewhere.
        var creditUsed = 0m;
        if (req.UseCredit && user is not null)
        {
            var dry = Pricing.Quote(quoteRequest!);
            creditUsed = CreditRules.Spendable(
                await wallet.SpendableAsync(user.Id, null, ct), dry.RoomBeforeDiscount - dry.RoomDiscount);

            if (creditUsed > 0)
                quoteRequest = quoteRequest! with
                {
                    PromotionAmount = creditUsed,
                    PromotionLabel = "Số dư Staylio"
                };
        }

        var price = Pricing.Quote(quoteRequest!);
        var couponId = couponCheck.Ok
            ? (await db.Coupons.FirstAsync(c => c.Code == Coupons.Normalize(req.CouponCode), ct)).Id
            : (int?)null;

        // docs/01 ĐP-03 — a host's instant-book conditions. A guest who does not
        // meet them is not refused: the booking falls back to request-to-book so
        // the host decides. A private offer bypasses this — the host already chose
        // this guest by extending it.
        var instantAvailable = listing.InstantBook;
        string? instantFallbackNote = null;
        if (listing.InstantBook && offer is null
            && (listing.InstantBookRequiresVerified || listing.InstantBookRequiresGoodReviews))
        {
            // Somebody with no account is the plainest case of an unverified,
            // unreviewed guest, so they meet these conditions the same way any
            // other unverified guest does: the stay becomes a request the host
            // answers, which is what the host turned these on to get.
            var reviewed = user is null
                ? []
                : await db.GuestReviews.Where(r => r.GuestUserId == user.Id)
                    .Select(r => (double?)r.Rating).ToListAsync(ct);
            var eligibility = InstantBook.Check(
                listing.InstantBookRequiresVerified, listing.InstantBookRequiresGoodReviews,
                user?.IsIdentityVerified ?? false,
                reviewed.Count > 0 ? reviewed.Average(x => x!.Value) : null,
                reviewed.Count);

            if (!eligibility.MayInstantBook)
            {
                instantAvailable = false;
                instantFallbackNote = eligibility.Reason;
            }
        }

        // docs/07 §2.3 — a guest paying by transfer has to leave for a banking
        // app, so the 15 minutes a card gets would run out while they are still
        // logging in. The dates are held for the transfer window instead.
        var byTransfer = !PaymentMethods.ChargesOnBooking(req.PaymentMethod);
        var holdFor = byTransfer ? BankTransfers.Window : BookingLifecycle.PaymentHold;

        // docs/07 §6 — freeze what the guest was reading prices in, at the
        // platform's own rate. All 155 bookings before 05/09/2026 have null
        // here: the writer trusted a request field no client ever sent.
        string? displayCurrency = null;
        decimal? displayRate = null;
        var wantedCurrency = (req.DisplayCurrency ?? "").Trim().ToUpperInvariant();
        if (wantedCurrency.Length > 0 && wantedCurrency != Fx.Base)
        {
            var fx = await db.ExchangeRates
                .FirstOrDefaultAsync(r => r.Code == wantedCurrency && r.IsActive, ct);
            if (fx is not null) { displayCurrency = fx.Code; displayRate = fx.RateFromVnd; }
        }

        var booking = new Booking
        {
            Reference = "SH" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SessionId = HttpContext.SessionId(),
            GuestUserId = user?.Id,
            ListingId = listing.Id,
            Listing = listing,
            CheckIn = req.CheckIn,
            CheckOut = req.CheckOut,
            Guests = party.Counted,
            Adults = party.Adults,
            Children = party.Children,
            Infants = party.Infants,
            Pets = party.Pets,
            Nights = price.Nights,
            RoomBeforeDiscount = price.RoomBeforeDiscount,
            RoomDiscount = price.RoomDiscount,
            DiscountPercent = price.DiscountPercent,
            ExtraGuestFee = price.ExtraGuestFee,
            PetFee = price.PetFee,
            CleaningFee = price.CleaningFee,
            Subtotal = price.Subtotal,
            ServiceFee = price.GuestServiceFee,
            Tax = price.Tax,
            Promotion = price.Promotion,
            Total = price.Total,
            HostServiceFee = price.HostServiceFee,
            HostPayout = price.HostPayout,
            PriceLinesJson = SerializeLines(price.Lines),
            RoomTypeId = req.RoomTypeId,
            CreditUsed = creditUsed,
            CouponId = couponId,
            CouponDiscount = price.Coupon,
            NightlyOverride = offer?.NightlyRate,
            // docs/07 §6 — kept so "the price I was shown" can be settled from
            // the record. The rate is the platform's own at this instant, looked
            // up below — never one the browser claimed. Evidence, not an input:
            // refunds go back at the original amount in the original currency,
            // and recomputing one through this rate would be a money bug that
            // balances to zero.
            DisplayCurrency = displayCurrency,
            DisplayRate = displayRate,
            CancellationTier = listing.CancellationTier,
            GuestName = req.GuestName ?? user?.FullName,
            GuestEmail = req.GuestEmail ?? user?.Email,
            GuestPhone = Identity.NormalisePhone(req.GuestPhone) ?? user?.Phone,
            GuestNote = req.GuestNote,
            // docs/03 §2–§3: instant book takes the dates off the market for 15
            // minutes while the guest pays; a request waits 24 hours on the host
            // and deliberately does not hold the dates at all.
            Status = instantAvailable ? BookingStatus.PendingPayment : BookingStatus.PendingHostApproval,
            HoldExpiresAt = instantAvailable ? DateTime.UtcNow + holdFor : null,
            RequestExpiresAt = instantAvailable ? null : DateTime.UtcNow + BookingLifecycle.RequestWindow
        };

        booking.Payment = new Payment
        {
            Reference = "PAY" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            Amount = price.Total,
            Currency = "VND",
            Method = string.IsNullOrWhiteSpace(req.PaymentMethod) ? "card" : req.PaymentMethod,
            CardLast4 = byTransfer ? null : req.CardLast4 ?? "4242",
            Status = byTransfer ? PaymentStatus.Pending : PaymentStatus.Authorized,
            PlatformFee = price.GuestServiceFee + price.HostServiceFee,
            HostPayout = price.HostPayout,
            PayoutDueOn = req.CheckIn.AddDays(1)
        };

        db.Bookings.Add(booking);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsOverlapViolation(ex))
        {
            // Two guests reached checkout for the same nights at the same moment.
            // The database exclusion constraint decided; this one lost.
            return Conflict(new
            {
                message = "Khoảng ngày này vừa có người khác đặt xong. Vui lòng chọn ngày khác.",
                reason = nameof(Availability.Reason.DatesTaken)
            });
        }

        // docs/01 TC-09 — the redemption is written once the booking has an id.
        // A held booking counts against the campaign straight away; if it lapses
        // or is cancelled the redemption is released so the limit is not spent on
        // a stay that never happened.
        if (couponId is { } cid && price.Coupon > 0)
            coupons.Redeem(cid, user!.Id, booking.Id, price.Coupon);

        // docs/01 ĐP-17 — the offer is spent, so it cannot be booked a second
        // time. A hold that lapses is not released back here: an offer is a
        // one-shot the host extended to this guest, not a limited campaign.
        if (offer is not null)
        {
            offer.Status = SpecialOfferStatus.Accepted;
            offer.RespondedAt = DateTime.UtcNow;
            offer.BookingId = booking.Id;
        }

        db.BookingEvents.Add(BookingLifecycle.Created(booking, $"guest:{user?.Id.ToString() ?? booking.SessionId}",
            instantAvailable
                // docs/07 §2.3 — the two hold lengths are different, and the
                // history is the record of which one this booking actually got.
                ? byTransfer
                    ? $"Giữ chỗ {BankTransfers.Window.TotalHours:0} giờ để chuyển khoản"
                    : "Giữ chỗ 15 phút để thanh toán"
                : instantFallbackNote is null ? "Gửi yêu cầu đặt"
                : "Chuyển thành yêu cầu đặt do chưa đủ điều kiện Đặt ngay của chủ nhà"));
        await db.SaveChangesAsync(ct);

        // docs/01 TC-09 — a limited code taken by a request that arrived at the
        // same moment. The later booking lets its dates go at once.
        if (couponId is { } usedCoupon && price.Coupon > 0
            && !await coupons.WithinLimitsAsync(usedCoupon, booking.Id, ct))
        {
            await coupons.ReleaseAsync(booking.Id, ct);
            db.BookingEvents.Add(BookingLifecycle.Transition(
                booking, BookingStatus.CancelledByGuest, "system", "Mã giảm giá vừa hết lượt dùng."));
            await db.SaveChangesAsync(ct);
            return Conflict(new { message = "Mã giảm giá này vừa hết lượt dùng. Vui lòng đặt lại không kèm mã." });
        }

        // An instant booking is still unpaid at this point, so nobody is told
        // about it until the money is actually taken. A booking that fell back to
        // request-to-book (docs/01 ĐP-03) is a request, so the host is notified
        // like any other.
        if (!instantAvailable)
        {
            var hostUser = await db.Users.FirstOrDefaultAsync(u => u.HostProfile!.Id == listing.HostId, ct);
            await notifications.QueueWithEmailAsync(hostUser, NotificationKind.BookingCreated,
                "Có yêu cầu đặt chỗ cần duyệt",
                $"{booking.GuestName} đặt \"{listing.Title}\" từ {booking.CheckIn:dd/MM} đến {booking.CheckOut:dd/MM} " +
                $"({booking.Nights} đêm, {booking.Guests} khách). Bạn có 24 giờ để trả lời.",
                "/hosting", ct);

            var line = $"Mã đặt chỗ {booking.Reference} · {listing.Title} · {booking.Nights} đêm. " +
                       "Yêu cầu đặt không giữ ngày: ai trả tiền xong trước thì được.";

            if (user is not null)
            {
                await notifications.QueueWithEmailAsync(user, NotificationKind.BookingCreated,
                    "Đã gửi yêu cầu đặt chỗ", line, $"/trips/{booking.Id}", ct);
            }
            else
            {
                // docs/07 §2.5 — no account means no in-app row and no preference
                // mask; the address they typed is the only way to reach them, and
                // the reference in this mail is how they find the booking again.
                notifications.QueueEmailOnly(booking.GuestEmail, booking.GuestName,
                    "Đã gửi yêu cầu đặt chỗ", line, "/dat-cho");
            }

            await db.SaveChangesAsync(ct);
        }

        return Created($"/api/bookings/{booking.Id}", ToDto(booking));
    }

    /// <summary>
    /// Stands in for the bank's own 3-D Secure page (docs/07 §5). It is a
    /// separate request because in reality it is a separate site: the bank checks
    /// the code and takes the money there, and the platform finds out afterwards
    /// — or, when the guest's connection drops, does not find out at all until it
    /// asks. That is the case docs/07 §18 scenario 3 requires to work, and it
    /// cannot be exercised end to end unless the two halves can come apart.
    /// </summary>
    [HttpPost("{id:int}/bank-otp")]
    public async Task<ActionResult<CardAuthChallengeDto>> BankOtp(
        int id, [FromBody] BankOtpRequest req, CancellationToken ct)
    {
        var booking = await FindOwnedAsync(id, ct);
        if (booking is null) return NotFound();

        var pending = await db.CardAuthentications
            .FirstOrDefaultAsync(a => a.AttemptKey == req.AttemptKey && a.BookingId == booking.Id, ct);

        if (pending is null) return NotFound(new { message = "Không tìm thấy phiên xác thực." });

        if (pending.Outcome == AuthOutcome.Succeeded)
        {
            return Ok(new CardAuthChallengeDto(
                pending.AttemptKey, booking.HoldExpiresAt, pending.CodeAttempts, 0,
                CardAuth.OutcomeMessage(AuthOutcome.Succeeded, pending.CodeAttempts)));
        }

        if (!CardAuth.CanTryCodeAgain(pending.CodeAttempts))
        {
            return BadRequest(new
            {
                message = CardAuth.OutcomeMessage(AuthOutcome.WrongCode, pending.CodeAttempts),
                needsDifferentMethod = true
            });
        }

        pending.CodeAttempts++;

        var authorised = gateway.Authorise(
            pending.AttemptKey, pending.Amount, pending.Method, pending.CardLast4, req.Code);

        pending.Outcome = authorised.Ok ? AuthOutcome.Succeeded : AuthOutcome.WrongCode;
        if (authorised.Ok) pending.SettledAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        if (!authorised.Ok)
        {
            return BadRequest(new
            {
                message = CardAuth.OutcomeMessage(AuthOutcome.WrongCode, pending.CodeAttempts),
                retryable = CardAuth.CanTryCodeAgain(pending.CodeAttempts)
            });
        }

        // The money has moved. Whether the guest makes it back to Staylio is now
        // out of everyone's hands — which is exactly why the sweep exists.
        return Ok(new CardAuthChallengeDto(
            pending.AttemptKey, booking.HoldExpiresAt, pending.CodeAttempts, 0,
            CardAuth.OutcomeMessage(AuthOutcome.Succeeded, pending.CodeAttempts)));
    }

    /// <summary>
    /// Takes the money for a booking that is holding its dates. docs/01 ĐP-12:
    /// the server prices the stay again here and refuses to charge a number
    /// different from the one the guest agreed to.
    /// </summary>
    [HttpPost("{id:int}/pay")]
    public async Task<ActionResult<BookingDto>> Pay(int id, [FromBody] PayBookingRequest? req, CancellationToken ct)
    {
        // docs/07 §2.5 — the booking may belong to a session rather than an
        // account. Ownership is the same question either way, and FindOwnedAsync
        // has always answered both; this used to demand a signed-in user and so
        // could never finish a booking a stranger had started.
        var user = await auth.CurrentUserAsync(ct);
        var sid = HttpContext.SessionId();

        var booking = await db.Bookings
            .Include(b => b.Payment).Include(b => b.Events)
            .Include(b => b.Listing!).ThenInclude(l => l.Images)
            .FirstOrDefaultAsync(b => b.Id == id
                && (user != null ? b.GuestUserId == user.Id : b.SessionId == sid && b.GuestUserId == null), ct);

        if (booking is null) return NotFound();

        // docs/07 §7 — a retry of a request already answered gets that answer
        // back. Checked before the state guard, because by then the first attempt
        // has moved the booking on and the retry would be told the order is in
        // the wrong state — true, and useless to a client whose reply was lost.
        var replayKey = Payments.NamespaceKey(
            booking.Id, req?.IdempotencyKey, 0, req?.PaymentMethod ?? "card");

        if (req?.IdempotencyKey is not null)
        {
            var answered = await db.PaymentAttempts
                .FirstOrDefaultAsync(a => a.BookingId == booking.Id && a.Key == replayKey, ct);

            if (answered is { Status: PaymentAttemptStatus.Succeeded }) return Ok(ToDto(booking));
            if (answered is { Status: PaymentAttemptStatus.Failed })
                return BadRequest(new { message = answered.Message ?? Payments.Message(answered.Reason) });
        }

        if (booking.Status != BookingStatus.PendingPayment)
        {
            // The common way to get here is the ordinary one: the guest read the
            // page for longer than the hold lasts and the dates went back on the
            // market. "Đơn đang ở trạng thái Thanh toán thất bại" is true and
            // tells them nothing they can act on, so that case says what happened
            // and what to do about it.
            var lapsed = booking.Status == BookingStatus.PaymentFailed;

            return BadRequest(new
            {
                message = lapsed
                    ? "Lượt giữ chỗ đã hết hạn nên đơn này không thanh toán được nữa. Hãy đóng cửa sổ và đặt lại — ngày bạn chọn có thể vẫn còn."
                    : $"Đơn đang ở trạng thái \"{BookingLifecycle.Label(booking.Status)}\" nên không thanh toán được.",
                holdExpired = lapsed
            });
        }

        if (booking.HoldExpiresAt is { } expiry && expiry < DateTime.UtcNow)
        {
            db.BookingEvents.Add(BookingLifecycle.Transition(
                booking, BookingStatus.PaymentFailed, "system", "Hết 15 phút giữ chỗ mà chưa thanh toán xong."));
            await db.SaveChangesAsync(ct);
            return Conflict(new { message = "Đã quá 15 phút giữ chỗ. Vui lòng chọn lại ngày và đặt lại." });
        }

        // ĐP-12 — price it again and stop if anything moved while the guest paid.
        var party = new PartySize(booking.Adults, booking.Children, booking.Infants, booking.Pets);
        var fresh = await catalog.BuildQuoteRequestAsync(
            booking.ListingId, booking.CheckIn, booking.CheckOut, party, ct, booking.Id, booking.RoomTypeId,
            // docs/01 ĐP-17 — a private offer set the rate, so the re-price uses it
            // rather than the listing's normal price it would otherwise fail against.
            nightlyOverride: booking.NightlyOverride);

        // docs/01 ĐP-09 — the promo code committed at the hold is part of the
        // shown price too, so the re-price carries it exactly as quoted. The
        // discount is re-derived from today's rules rather than trusting the
        // stored figure: a campaign ended between hold and payment should stop the
        // stale figure sailing through, and CouponDiscount was only ever a cache
        // of the number the guest saw.
        if (booking.CouponId is { } couponId)
        {
            var couponEntity = await db.Coupons.FindAsync([couponId], ct);
            var dry = Pricing.Quote(fresh!);
            var gross = dry.Subtotal + dry.GuestServiceFee + dry.Tax;
            var recheck = await coupons.EvaluateAsync(
                couponEntity?.Code, booking.GuestUserId!.Value, gross, DateTime.UtcNow,
                excludeBookingId: booking.Id, ct: ct);

            if (recheck.Ok)
                fresh = fresh! with { CouponAmount = recheck.Discount, CouponLabel = recheck.Label ?? "Mã giảm giá" };
        }

        // The balance the guest committed at the hold is part of the price they
        // were shown, so the re-price has to include it or every credit booking
        // would fail its own "did the price move" check.
        if (booking.CreditUsed > 0)
            fresh = fresh! with { PromotionAmount = booking.CreditUsed, PromotionLabel = "Số dư Staylio" };

        var price = Pricing.Quote(fresh!);

        // The balance this booking promised must still be there: it may have been
        // spent on another stay since the hold, and it is only taken at confirm.
        if (booking.CreditUsed > 0 && booking.GuestUserId is { } spender
            && await wallet.SpendableAsync(spender, booking.Id, ct) < booking.CreditUsed)
        {
            return Conflict(new
            {
                message = "Số dư Staylio của bạn không còn đủ cho đơn này. Vui lòng đặt lại để tính giá mới."
            });
        }

        if (price.Total != booking.Total)
        {
            return Conflict(new
            {
                message = $"Giá vừa thay đổi từ {booking.Total:#,##0}₫ thành {price.Total:#,##0}₫. " +
                          "Vui lòng xem lại trước khi thanh toán.",
                newTotal = price.Total,
                oldTotal = booking.Total
            });
        }

        // docs/01 ĐP-06 — a deposit of at least half, with the rest taken 14 days
        // out. Too close to check-in for that and the whole amount is due now.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var partial = req?.PayDeposit == true && PartialPayment.IsAvailable(booking.CheckIn, today);
        var charged = partial ? PartialPayment.Deposit(price.Total, req?.DepositAmount) : price.Total;

        var method = (req?.PaymentMethod ?? "card").Trim().ToLowerInvariant();

        // docs/07 §2 — only a row of the catalogue is a way to pay. Anything else
        // used to fall through to the stand-in, which confirmed it.
        if (!PaymentMethods.IsAccepted(method) || method == "balance")
            return BadRequest(new
            {
                message = Payments.Message(DeclineReason.MethodUnavailable), needsDifferentMethod = true
            });

        /*
         * docs/07 §2.5 — the guest pays the host at the door, so there is nothing
         * to take here and nothing to hold. The booking is confirmed on the spot,
         * which is the whole point of it for a guest with no card.
         *
         * No ledger entry is written. Money that never reached Staylio has not
         * moved through Staylio, and a booking-captured transaction would state
         * that it had. The platform's fee is billed to the host when they confirm
         * the cash is in hand (PayAtProperty, and HostOperationsController).
         */
        if (PaymentMethods.SettlesAtProperty(method))
        {
            var allowed = PayAtProperty.CanUse(
                booking.Listing!.AcceptsPayAtProperty, partial,
                await db.BillSplits.AnyAsync(sp => sp.BookingId == booking.Id, ct),
                booking.CreditUsed > 0 || booking.CouponId is not null);

            if (!allowed.Ok)
                return BadRequest(new { message = allowed.Message, reason = allowed.Reason.ToString() });

            if (booking.Payment is not null)
            {
                booking.Payment.Method = method;
                booking.Payment.CardLast4 = null;
                // Pending, not Captured: nobody has paid anybody yet. The payout
                // sweep reads Captured, so this booking is correctly invisible to
                // it — there is no platform money to forward.
                booking.Payment.Status = PaymentStatus.Pending;
            }

            // The one place this is set. Every cancellation rule reads it off the
            // booking rather than off the payment row, which half the call sites
            // never load.
            booking.PaidAtProperty = true;
            booking.HoldExpiresAt = null;
            db.BookingEvents.Add(BookingLifecycle.Transition(
                booking, BookingStatus.Confirmed,
                user is null ? $"guest:{sid}" : $"guest:{user.Id}",
                "Trả tiền tại nơi ở khi nhận phòng."));

            await db.SaveChangesAsync(ct);
            await NotifyConfirmedAsync(booking, ct);
            return Ok(ToDto(booking));
        }

        // docs/07 §2.3 — a bank transfer is not taken here. The guest leaves for
        // their banking app, so the booking keeps its dates on a longer timer and
        // waits to be found on a statement. Everything below this point — the
        // idempotency key, the gateway, the confirmation — belongs to methods
        // that move money inside this request, and none of it applies.
        if (!PaymentMethods.ChargesOnBooking(method))
        {
            // A deposit paid by transfer would mean a second transfer, a second
            // reference and a schedule to chase it. Not in this first version, and
            // saying so is better than quietly taking the whole amount.
            if (partial)
                return BadRequest(new { message = "Chuyển khoản QR chưa dùng để đặt cọc được. Hãy trả đủ hoặc chọn cách khác." });

            if (booking.Status != BookingStatus.PendingPayment)
                return BadRequest(new { message = "Đơn này không còn ở bước chờ thanh toán." });

            booking.HoldExpiresAt = DateTime.UtcNow + BankTransfers.Window;

            if (booking.Payment is not null)
            {
                booking.Payment.Method = method;
                booking.Payment.CardLast4 = null;
                booking.Payment.Status = PaymentStatus.Pending;
            }

            await db.SaveChangesAsync(ct);

            // The QR itself comes from /api/payment-methods/vietqr/{reference},
            // which builds it from the booking rather than from anything the
            // browser could have chosen.
            return Ok(ToDto(booking));
        }

        // docs/07 §3 — the balance covering everything does not end the question
        // of where later money comes from. docs/06 §3.3 collects damages through
        // this stored method, so it has to exist before the booking does.
        if (PaymentMethods.NeedsFallbackMethod(charged, method, req?.CardLast4))
            return BadRequest(new { message = PaymentMethods.FallbackNotice(), needsFallbackMethod = true });

        // docs/07 §8 — a card tester gets five goes an hour on one booking.
        var since = DateTime.UtcNow - Payments.FailureWindow;
        var failures = await db.PaymentAttempts.CountAsync(
            a => a.BookingId == booking.Id && a.Status == PaymentAttemptStatus.Failed && a.CreatedAt >= since, ct);

        if (Payments.LockedOut(failures))
            return StatusCode(429, new { message = Payments.LockedOutMessage() });

        // docs/07 §7 — the same request twice must take the money once. The key
        // is claimed before the gateway is called and the unique index is what
        // decides the race, not the order two requests happen to arrive in.
        var key = req?.IdempotencyKey is not null
            ? replayKey
            : Payments.KeyFor(booking.Id, charged, method);

        // docs/07 §13 phương án A — a method wired to a licensed gateway is not
        // charged here at all. The guest leaves for VNPay / MoMo / ZaloPay, types
        // their card on that gateway's own page (docs/07 §14.2 — never on one of
        // ours), and the money moves before this platform hears anything.
        //
        // This has to sit above every line below it. The stand-in gateway says
        // yes to anything that is not the declining test card, so falling through
        // would confirm a stay whose money is still in the guest's account — the
        // same trap docs/07 §2.3 spells out for the bank transfer.
        if (psp.For(method) is not null)
        {
            if (booking.Payment is not null) booking.Payment.CardLast4 = null;

            // docs/07 §4 — a card the guest kept at the gateway. The token is
            // theirs and sealed here, so it is opened for exactly this call and
            // never leaves the server.
            string? cardToken = null;

            // docs/07 §4 — a saved card belongs to an account, so a booking made
            // without one has none to reach for.
            if (req?.SavedCardId is { } savedId && user is not null)
            {
                var saved = await db.SavedCards
                    .FirstOrDefaultAsync(c => c.Id == savedId && c.UserId == user.Id, ct);

                cardToken = secrets.Open(DataSecrets.CardToken, saved?.GatewayTokenSealed);
            }

            var handover = await pspCheckout.StartAsync(
                booking, method, charged, partial, key,
                Psp.ClientIp(HttpContext.Connection.RemoteIpAddress?.ToString()), ct,
                saveCard: req?.SaveCard == true, cardToken: cardToken);

            if (!handover.Ok || handover.PayUrl is null)
                return BadRequest(new { message = handover.Error, retryable = true });

            // The booking is still PendingPayment and nothing has been posted to
            // the ledger. The client's job is to send the guest to this address;
            // the confirmation comes back through /api/payments/{provider}/....
            return Ok(ToDto(booking) with
            {
                GatewayRedirectUrl = handover.PayUrl,
                GatewayOrderRef = handover.OrderRef
            });
        }

        // Past this line the money would be taken in place by the stand-in. That is
        // right for a demo build and wrong everywhere else — see PspRouter.StandInMay.
        if (charged > 0 && !psp.StandInMay(method))
            return BadRequest(new
            {
                message = Payments.Message(DeclineReason.MethodUnavailable), needsDifferentMethod = true
            });

        // docs/07 §5 — cards that need the bank's OTP go there first. The row is
        // created before the idempotency claim so a guest coming back with their
        // code resumes this attempt rather than being told it already happened.
        if (gateway.NeedsAuthentication(method, req?.CardLast4))
        {
            var challenge = await AuthenticateAsync(booking, key, charged, method, req, ct);
            if (challenge is not null) return challenge;
        }

        var settled = await db.PaymentAttempts.FirstOrDefaultAsync(a => a.Key == key, ct);
        if (settled is not null)
        {
            if (settled.Status == PaymentAttemptStatus.Failed)
                return BadRequest(new { message = settled.Message ?? Payments.Message(settled.Reason) });
            return Ok(ToDto(booking));
        }

        var claim = new PaymentAttempt
        {
            Key = key, BookingId = booking.Id, Amount = charged,
            Method = method, CardLast4 = req?.CardLast4
        };
        db.PaymentAttempts.Add(claim);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request holds this key. It is charging, or has charged;
            // either way this one must not.
            db.ChangeTracker.Clear();
            return Conflict(new { message = "Yêu cầu thanh toán này đang được xử lý. Vui lòng đợi trong giây lát." });
        }

        // docs/07 §2.3 — a method that cannot be charged here has to be refused
        // here. The stand-in gateway says yes to anything that is not the
        // declining test card, so letting VietQR through would confirm a stay
        // whose money is still sitting in the guest's own account.
        if (!PaymentMethods.ChargesOnBooking(method))
            return BadRequest(new { message = PaymentMethods.NotChargeableYet });

        // The key goes to the gateway too: docs/07 §5's self-check is only
        // possible if the platform can ask "what happened to this attempt".
        var attempt = gateway.Charge(charged, method, req?.CardLast4, key);

        if (!attempt.Ok)
        {
            claim.Status = PaymentAttemptStatus.Failed;
            claim.Reason = attempt.Decline;
            claim.Message = Payments.Message(attempt.Decline);
            claim.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return BadRequest(new
            {
                message = claim.Message,
                retryable = Payments.Retryable(attempt.Decline),
                needsDifferentMethod = Payments.NeedsDifferentMethod(attempt.Decline)
            });
        }

        claim.Status = PaymentAttemptStatus.Succeeded;
        claim.CompletedAt = DateTime.UtcNow;

        // The same steps the self-check of docs/07 §5 runs when it discovers a
        // booking was paid after the guest's connection dropped.
        await completion.ConfirmAsync(
            booking, price, charged, partial, today, user?.Id, req?.PaymentMethod, req?.CardLast4, ct);

        return Ok(ToDto(booking));
    }

    /// <summary>
    /// docs/01 ĐP-06 — the rest of a part-paid booking, either because its date
    /// came round or because the guest chose to settle up early.
    /// </summary>
    [HttpPost("{id:int}/balance")]
    public async Task<ActionResult<BookingDto>> PayBalance(
        int id, [FromServices] BalanceCollector balances, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var booking = await db.Bookings
            .Include(b => b.Payment).Include(b => b.Events)
            .Include(b => b.Listing!).ThenInclude(l => l.Images)
            .FirstOrDefaultAsync(b => b.Id == id && b.GuestUserId == user.Id, ct);

        if (booking is null) return NotFound();
        if (booking.BalanceDue <= 0 || booking.BalanceStatus is BalanceStatus.None or BalanceStatus.Paid)
            return BadRequest(new { message = "Đơn này không còn khoản nào phải trả." });

        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.InProgress))
            return BadRequest(new { message = "Đơn này không còn hiệu lực." });

        // docs/07 §13 — the rest goes the way the deposit went: out to the gateway.
        var method = booking.Payment?.Method ?? "card";
        if (psp.IsLive(method))
        {
            var started = await pspCheckout.StartForAsync(
                new PaymentSession
                {
                    BookingId = booking.Id, IsBalance = true, Method = method, Amount = booking.BalanceDue,
                    AttemptKey = $"balance-{booking.Id}-{booking.BalanceDue:0}"
                },
                booking.Id, $"Staylio {booking.Reference}", user.Id,
                Psp.ClientIp(HttpContext.Connection.RemoteIpAddress?.ToString()), ct);

            if (!started.Ok || started.PayUrl is null)
                return BadRequest(new { message = started.Error, retryable = true });

            return Ok(ToDto(booking) with { GatewayRedirectUrl = started.PayUrl, GatewayOrderRef = started.OrderRef });
        }

        var attempt = gateway.Charge(booking.BalanceDue, method, booking.Payment?.CardLast4);
        booking.BalanceAttempts++;
        booking.BalanceLastAttemptAt = DateTime.UtcNow;

        if (!attempt.Ok)
        {
            booking.BalanceFirstFailedAt ??= DateTime.UtcNow;
            booking.BalanceStatus = BalanceStatus.Retrying;
            await db.SaveChangesAsync(ct);
            return BadRequest(new { message = attempt.Reason });
        }

        await balances.ApplyAsync(
            booking, $"guest:{user.Id}", $"Đã thu nốt {booking.BalanceDue:#,##0}₫.", DateTime.UtcNow, ct);

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(booking));
    }

    /// <summary>The guest walked away from checkout; the dates go back on sale.</summary>
    [HttpPost("{id:int}/release")]
    public async Task<IActionResult> Release(int id, CancellationToken ct)
    {
        var booking = await FindOwnedAsync(id, ct);
        if (booking is null) return NotFound();
        if (booking.Status != BookingStatus.PendingPayment) return NoContent();

        db.BookingEvents.Add(BookingLifecycle.Transition(
            booking, BookingStatus.CancelledByGuest, "guest", "Khách rời bước thanh toán."));
        // A hold never paid for spent no balance, so only the promo code needs
        // handing back (docs/01 TC-09).
        await coupons.ReleaseAsync(booking.Id, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// docs/01 ĐP-14, CĐ-09 — the booking as an invoice the guest can keep. It is
    /// an HTML document rather than a PDF: the browser's own "save as PDF" turns it
    /// into one, and it avoids a rendering dependency for a page that is, in the
    /// end, a table of the numbers already on the booking.
    /// </summary>
    [HttpGet("{id:int}/invoice")]
    public async Task<IActionResult> Invoice(int id, CancellationToken ct)
    {
        var booking = await FindOwnedAsync(id, ct, includeListing: true);
        if (booking is null) return NotFound();

        var lines = DeserializeLines(booking.PriceLinesJson);
        var html = InvoiceHtml.Render(booking, lines);

        // Inline so it opens in the tab and the guest can read it or print it;
        // the download comes from the browser, and the filename is set for it.
        Response.Headers.ContentDisposition =
            $"inline; filename=\"stayhost-{Invoices.Number(booking)}.html\"";
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// docs/01 ĐP-15, docs/02 D3 — "thêm chuyến đi vào lịch cá nhân". One event,
    /// in the format every calendar reads, served from the same Ical writer the
    /// host feeds of docs/01 QL-10 use.
    ///
    /// The whole of the stay is one all-day event because that is what a night
    /// is here: DTEND is exclusive in iCalendar, which is exactly a check-out
    /// date, so no arithmetic is needed to make the two agree.
    ///
    /// The address is only in the file once the booking is confirmed — the same
    /// line docs/03 §10 draws everywhere else. A calendar entry leaves the
    /// platform the moment it is downloaded, so it must not be the one place
    /// that hands out an address for a stay nobody has agreed to yet.
    /// </summary>
    [HttpGet("{id:int}/calendar.ics")]
    public async Task<IActionResult> Calendar(int id, CancellationToken ct)
    {
        var booking = await FindOwnedAsync(id, ct, includeListing: true);
        if (booking is null) return NotFound();

        var listing = booking.Listing;
        var where = CheckInGuide.CanSeeGuide(booking.Status) && listing is not null
            ? listing.AddressLine ?? listing.City
            : listing?.City;

        var summary = $"{listing?.Title ?? "Staylio"} · {booking.Reference}";
        var text = Ical.Write(
            "Staylio",
            [new IcalEvent(
                $"booking-{booking.Id}@staylio.vn",
                booking.CheckIn, booking.CheckOut, summary)
            { Location = where, Description = CalendarNote(booking) }],
            DateTime.UtcNow);

        Response.Headers.ContentDisposition =
            $"attachment; filename=\"staylio-{booking.Reference}.ics\"";
        return Content(text, Ical.ContentType);
    }

    /// <summary>What the guest wants to read in their own calendar, not a receipt.</summary>
    private static string CalendarNote(Booking b)
    {
        var parts = new List<string>
        {
            $"Mã đặt chỗ: {b.Reference}",
            $"{b.Nights} đêm · {b.Guests} khách",
            BookingLifecycle.Label(b.Status)
        };

        if (b.Listing?.Host?.Name is { Length: > 0 } host) parts.Add($"Chủ nhà: {host}");
        if (b.Listing?.HostPhone is { Length: > 0 } phone
            && CheckInGuide.CanSeeGuide(b.Status)) parts.Add($"Điện thoại: {phone}");

        return string.Join("\n", parts);
    }

    /// <summary>
    /// docs/01 CĐ-06, docs/04 QT-4 — the guest asks to move a confirmed booking to
    /// new dates or a new guest count. The old dates stay held; nothing changes
    /// until the host accepts. The difference is quoted now so the guest sees it.
    /// </summary>
    [HttpPost("{id:int}/change-request")]
    public async Task<ActionResult<ChangeRequestDto>> RequestChange(
        int id, [FromBody] ChangeBookingRequest req, CancellationToken ct)
    {
        var booking = await FindOwnedAsync(id, ct, includeListing: true);
        if (booking is null) return NotFound();

        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.PendingHostApproval))
            return BadRequest(new { message = "Chỉ đổi được đơn đã xác nhận hoặc đang chờ duyệt." });

        var adults = req.Adults ?? Math.Max(1, req.Guests);
        if (ChangeRequests.Validate(req.CheckIn, req.CheckOut, adults) is { } invalid)
            return BadRequest(new { message = invalid });

        var party = new PartySize(adults, req.Children, req.Infants, req.Pets);
        if (party.Counted > booking.Listing!.MaxGuests)
            return BadRequest(new { message = $"Chỗ nghỉ này nhận tối đa {booking.Listing.MaxGuests} khách." });

        // The new dates must be free of every other booking — this one's own held
        // dates are excluded, since it keeps them until the change is accepted.
        var clash = await db.Bookings.AnyAsync(b =>
            b.ListingId == booking.ListingId && b.Id != booking.Id
            && BookingLifecycle.BlocksDates.Contains(b.Status)
            && b.CheckIn < req.CheckOut && req.CheckIn < b.CheckOut, ct);
        if (clash) return Conflict(new { message = "Ngày mới đã có người đặt. Vui lòng chọn ngày khác." });

        var newPrice = await RequoteAsync(booking, req.CheckIn, req.CheckOut, party, ct);
        if (newPrice is null) return NotFound();

        var now = DateTime.UtcNow;
        // One live request at a time: a new one supersedes any still pending.
        var stale = await db.BookingChangeRequests
            .Where(r => r.BookingId == booking.Id && r.Status == ChangeRequestStatus.Pending)
            .ToListAsync(ct);
        foreach (var r in stale) r.Status = ChangeRequestStatus.Withdrawn;

        var change = new BookingChangeRequest
        {
            BookingId = booking.Id,
            NewCheckIn = req.CheckIn,
            NewCheckOut = req.CheckOut,
            NewGuests = party.Counted,
            NewAdults = adults,
            NewChildren = req.Children,
            NewInfants = req.Infants,
            NewPets = req.Pets,
            NewTotal = newPrice.Total,
            Difference = newPrice.Total - booking.Total,
            CreatedAt = now,
            ExpiresAt = ChangeRequests.ExpiryFrom(now)
        };
        db.BookingChangeRequests.Add(change);
        await db.SaveChangesAsync(ct);

        return Ok(ToChangeDto(change));
    }

    /// <summary>Re-prices a stay for new dates/guests, keeping the booking's own
    /// coupon and balance so the difference is only the change in the stay.</summary>
    private async Task<Pricing.Breakdown?> RequoteAsync(
        Booking booking, DateOnly checkIn, DateOnly checkOut, PartySize party, CancellationToken ct)
    {
        var fresh = await catalog.BuildQuoteRequestAsync(
            booking.ListingId, checkIn, checkOut, party, ct, booking.Id, booking.RoomTypeId,
            nightlyOverride: booking.NightlyOverride);
        if (fresh is null) return null;

        if (booking.CouponDiscount > 0)
            fresh = fresh with { CouponAmount = booking.CouponDiscount, CouponLabel = "Mã giảm giá" };
        if (booking.CreditUsed > 0)
            fresh = fresh with { PromotionAmount = booking.CreditUsed, PromotionLabel = "Số dư Staylio" };

        return Pricing.Quote(fresh);
    }

    private static ChangeRequestDto ToChangeDto(BookingChangeRequest r) => new(
        r.Id, r.NewCheckIn, r.NewCheckOut, r.NewGuests, r.NewTotal, r.Difference,
        ChangeRequests.DiffLabel(r.Difference), r.Status.ToString(),
        ChangeRequests.IsLive(r, DateTime.UtcNow), r.ExpiresAt);

    /// <summary>docs/01 CĐ-06 — the guest calls off a still-pending change.</summary>
    [HttpPost("{id:int}/change-request/{reqId:int}/withdraw")]
    public async Task<IActionResult> WithdrawChange(int id, int reqId, CancellationToken ct)
    {
        var booking = await FindOwnedAsync(id, ct);
        if (booking is null) return NotFound();

        var change = await db.BookingChangeRequests
            .FirstOrDefaultAsync(r => r.Id == reqId && r.BookingId == booking.Id, ct);
        if (change is null) return NotFound();

        if (change.Status == ChangeRequestStatus.Pending)
        {
            change.Status = ChangeRequestStatus.Withdrawn;
            change.RespondedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    /// <summary>What the guest would get back if they cancelled right now.</summary>
    [HttpGet("{id:int}/refund-preview")]
    public async Task<ActionResult<RefundPreviewDto>> RefundPreview(int id, CancellationToken ct)
    {
        var booking = await FindOwnedAsync(id, ct);
        if (booking is null) return NotFound();

        var outcome = Cancellation.Refund(await BuildCancelContextAsync(booking, CancelledBy.Guest, ct));
        return Ok(ToPreview(booking, outcome));
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<RefundPreviewDto>> Cancel(int id, CancellationToken ct)
    {
        var booking = await FindOwnedAsync(id, ct);
        if (booking is null) return NotFound();

        if (!BookingLifecycle.CanTransition(booking.Status, BookingStatus.CancelledByGuest))
        {
            return BadRequest(new
            {
                message = $"Đơn đang ở trạng thái \"{BookingLifecycle.Label(booking.Status)}\" nên không huỷ được."
            });
        }

        var outcome = Cancellation.Refund(await BuildCancelContextAsync(booking, CancelledBy.Guest, ct));
        await ApplyCancellationAsync(booking, outcome, CancelledBy.Guest, "Khách huỷ", ct);

        var listing = await db.Listings.Include(l => l.Host!).ThenInclude(h => h.User)
            .FirstOrDefaultAsync(l => l.Id == booking.ListingId, ct);

        await notifications.QueueWithEmailAsync(listing?.Host?.User, NotificationKind.BookingCancelled,
            "Khách đã huỷ đặt chỗ",
            $"Mã {booking.Reference} · {booking.CheckIn:dd/MM}–{booking.CheckOut:dd/MM} đã được huỷ.",
            "/hosting", ct);

        await messenger.PostAsync(booking,
            $"Đơn {booking.Reference} đã được khách huỷ. Hoàn lại {outcome.Amount:#,##0}₫.", ct);

        await db.SaveChangesAsync(ct);
        return Ok(ToPreview(booking, outcome));
    }

    /// <summary>
    /// docs/07 §10 — the guest is told where each part of the money goes and how
    /// long it takes <em>before</em> confirming, not after. Cancellation decides
    /// how much; Refunds decides which pocket.
    /// </summary>
    private static RefundPreviewDto ToPreview(Booking b, Cancellation.Outcome o)
    {
        /*
         * docs/07 §2.5 — nothing was ever taken for a pay-at-property booking, so
         * there is nothing to give back. The policy still computed a figure off
         * the total, and docs/01 CĐ-07 shows that figure to the guest before they
         * confirm: it would have promised a refund of money nobody had paid.
         *
         * The penalty is zeroed with it. A penalty is the part of what you paid
         * that you do not get back; with nothing paid there is no such part.
         */
        if (b.PaidAtProperty && b.CashCollectedAt is null)
        {
            // Cancellation.Refund has already zeroed the money side; this only
            // has to stop Refunds.Allocate splitting nothing across a card that
            // was never charged, and to leave the penalty at zero.
            return new RefundPreviewDto(
                0m, 0m, b.Total, o.Explanation,
                0m, 0m, 0m, 0m, o.GoodwillCredit, 0m, 0m, "");
        }

        var split = Refunds.Allocate(SourcesOf(b), o.Amount, b.RefundedAmount);

        return new RefundPreviewDto(
            o.Amount, b.Total - o.Amount, b.Total, o.Explanation,
            o.RoomRefund, o.CleaningRefund, o.ServiceFeeRefund, o.TaxRefund, o.GoodwillCredit,
            split.ToCard, split.ToCredit, Refunds.TimingNotice(split));
    }

    /// <summary>
    /// What the stay was actually paid with.
    ///
    /// Balance is taken off the price before the card is charged (docs/07 §3),
    /// so <see cref="Booking.Total"/> is already net of it — subtracting it a
    /// second time here lost the guest half a million đồng off their own refund.
    /// The card carried the total; the balance is on top of it.
    /// </summary>
    private static Refunds.Sources SourcesOf(Booking b) => new(
        b.BalanceStatus == BalanceStatus.None ? b.Total : b.DepositPaid,
        b.CreditUsed);

    /// <summary>
    /// docs/03 §4 pre-rule 2 caps service-fee refunds at three a year, so the
    /// guest's recent history is part of the calculation.
    /// </summary>
    private async Task<Cancellation.Context> BuildCancelContextAsync(Booking booking, CancelledBy by, CancellationToken ct)
    {
        var yearAgo = DateTime.UtcNow.AddYears(-1);
        var used = booking.GuestUserId is null
            ? 0
            : await db.Bookings.CountAsync(b =>
                b.GuestUserId == booking.GuestUserId &&
                b.Status == BookingStatus.CancelledByGuest &&
                // Only what they actually did counts against the three a year.
                b.CancelledBy == CancelledBy.Guest &&
                b.RefundedAmount > 0 &&
                b.CreatedAt >= yearAgo, ct);

        return new Cancellation.Context
        {
            Booking = booking,
            Now = DateTime.UtcNow,
            By = by,
            ServiceFeeRefundsUsed = used
        };
    }

    /// <summary>
    /// Cancels the booking and books the matching double-entry transaction, so
    /// the ledger still balances afterwards (docs/00 §6.1).
    /// </summary>
    /// <summary>
    /// The part of a refund that is money going back, as opposed to balance or a
    /// receivable being written off. Nothing for a stay paid at the door
    /// (docs/07 §2.5); never more than the guest handed over.
    /// </summary>
    internal static decimal CashBackOf(Booking booking, decimal refund)
    {
        if (booking.PaidAtProperty) return 0m;
        var paid = booking.BalanceStatus == BalanceStatus.None ? booking.Total : booking.DepositPaid;
        return Math.Max(0m, Math.Min(refund, paid));
    }

    /// <param name="sent">
    /// docs/07 §10 — what the gateway took back onto the card. Whatever it would
    /// not take becomes balance. Null for callers with no card to bounce.
    /// </param>
    internal static void PostCancellation(
        StayHostDbContext db, Booking booking, Cancellation.Outcome outcome, CancelledBy by, string reason,
        RefundGateway.Sent? sent = null)
    {
        // A cancellation the platform made is not the guest's. Recording it as
        // theirs put five bookings a guest never touched on their record and,
        // worse, spent the three service-fee refunds docs/03 §4 allows them in a
        // year — a cost for somebody else's suspension. The CancelledBy column
        // keeps the truth either way; this is only about which side of the
        // ledger the terminal status sits on.
        var to = by is CancelledBy.Host or CancelledBy.Platform or CancelledBy.ForceMajeure
            ? BookingStatus.CancelledByHost
            : BookingStatus.CancelledByGuest;
        var actor = by switch
        {
            CancelledBy.Host => "host",
            CancelledBy.ForceMajeure => "admin",
            CancelledBy.Platform => "system",
            _ => "guest"
        };

        db.BookingEvents.Add(BookingLifecycle.Transition(booking, to, actor, reason));

        booking.CancellationReason = reason;
        booking.CancelledBy = by;
        booking.RefundedAmount = outcome.Amount;
        booking.GoodwillCredit = outcome.GoodwillCredit;

        if (booking.Payment is not null)
        {
            booking.Payment.Status = outcome.Amount >= booking.Total ? PaymentStatus.Refunded : PaymentStatus.Captured;
            booking.Payment.HostPayout = Math.Max(0m, booking.HostPayout - (outcome.RoomRefund + outcome.CleaningRefund));
            booking.Payment.PayoutStatus = PayoutStatus.OnHold;
        }

        // Nothing was ever captured for a pending request, so there is nothing to reverse.
        var captured = db.LedgerEntries.Local.Any(e => e.BookingId == booking.Id)
                       || db.LedgerEntries.Any(e => e.BookingId == booking.Id && e.TransactionKind == "booking-captured");
        if (!captured) return;

        // The host's fee is returned in proportion to the money leaving their side.
        var hostSideRefund = outcome.RoomRefund + outcome.CleaningRefund;
        var hostFeeReturned = booking.Subtotal > 0
            ? Math.Round(booking.HostServiceFee * hostSideRefund / booking.Subtotal, 0, MidpointRounding.AwayFromZero)
            : 0m;
        hostFeeReturned = Math.Min(hostFeeReturned, hostSideRefund);

        db.LedgerEntries.AddRange(Ledger.RefundBooking(booking, outcome, hostFeeReturned, DateTime.UtcNow));

        // docs/01 ĐP-06 — a guest who only paid a deposit cannot be sent back
        // more than they handed over. What they are owed is set against what
        // they still owe first, and only the cash difference actually moves.
        var cashBack = CashBackOf(booking, outcome.Amount);
        var netted = Math.Min(outcome.Amount - cashBack, booking.BalanceDue);

        /*
         * docs/07 §10 — "Thẻ đã hết hạn hoặc đã đóng: vẫn hoàn về thẻ đó trước…
         * Không được thì chuyển thành số dư trong tài khoản sàn và báo khách."
         *
         * Refunds.Redirect has held that rule since the document was written and
         * had no caller, because nothing here could produce the event: the
         * stand-in gateway had no refund call, so a refund could not be handed
         * back. The bank is asked first either way; only its refusal moves the
         * money, and the guest is told rather than left to find it.
         */
        // Only the part the card would not take moves. Redirecting the whole
        // amount when one of two visits bounced paid that guest twice.
        var bouncedCash = Math.Min(cashBack, sent?.Bounced ?? 0m);
        var cash = new Refunds.Split(cashBack - bouncedCash, bouncedCash, 0m);

        if (cash.ToCard > 0)
            db.LedgerEntries.AddRange(Ledger.SettleRefund(booking, cash.ToCard, DateTime.UtcNow));

        if (cash.ToCredit > 0 && booking.GuestUserId is { } bounced)
        {
            db.LedgerEntries.AddRange(Ledger.SettleRefundAsCredit(booking, cash.ToCredit, DateTime.UtcNow));
            db.CreditEntries.Add(CreditLedger.Grant(
                bounced, cash.ToCredit, CreditReason.Returned,
                $"Hoàn tiền đơn {booking.Reference} — thẻ không nhận được", DateTime.UtcNow, booking.Id));
        }

        if (netted > 0)
            db.LedgerEntries.AddRange(Ledger.NetRefundAgainstReceivable(booking, netted, DateTime.UtcNow));

        var leftover = booking.BalanceDue - netted;
        if (leftover > 0)
            db.LedgerEntries.AddRange(Ledger.WriteOffReceivable(booking, leftover, DateTime.UtcNow));

        // Balance spent on a booking comes back as balance, not as cash — and the
        // refund payable is cleared against that balance, not against the bank.
        if (booking.CreditUsed > 0 && booking.GuestUserId is { } creditOwner)
        {
            var creditBack = Math.Min(booking.CreditUsed, Math.Max(0m, outcome.Amount - cashBack - netted));
            db.LedgerEntries.AddRange(Ledger.SettleRefundAsCredit(booking, creditBack, DateTime.UtcNow));

            // Through CreditLedger.Grant rather than straight onto the table:
            // docs/01 TC-07 stamps a lifetime on a grant at the moment it is made,
            // and the row this used to build by hand quietly skipped that — so
            // returned balance never lapsed, whatever docs/07 §16 said.
            // creditBack, not CreditUsed: the ledger line above returns only what
            // the policy allows, and granting the whole balance back put the
            // wallet ahead of the books on every non-refundable stay.
            db.CreditEntries.Add(CreditLedger.Grant(
                creditOwner, creditBack, CreditReason.Returned,
                $"Hoàn số dư đơn {booking.Reference}", DateTime.UtcNow, booking.Id));
            booking.CreditUsed = 0;
        }

        booking.RefundedAmount = cashBack;
        if (booking.BalanceDue > 0)
        {
            booking.BalanceDue = 0;
            booking.BalanceStatus = BalanceStatus.Failed;
        }

        /*
         * docs/06 §8, the "Bất khả kháng" row — the guest gets everything back
         * and the fund carries the loss, but the host is owed Q-A of the booking
         * value as well. That half was never written: ForceMajeureHostRate sat
         * in ShieldSettings with no reader anywhere, so for a locked parameter
         * the customer signed off on, the host got nothing.
         *
         * Only when money actually moved. Cancelling a booking nobody had paid
         * for costs the fund nothing to undo, and paying compensation out of it
         * for a stay that was never captured would take real money out of the
         * fund against a loss that never happened.
         */
        if (by == CancelledBy.ForceMajeure)
        {
            var award = Shield.ForceMajeureHostAward(booking.Total);
            if (award > 0)
                db.LedgerEntries.AddRange(
                    Ledger.CompensateHostFromShield(booking, award, DateTime.UtcNow));
        }
    }

    private async Task ApplyCancellationAsync(
        Booking booking, Cancellation.Outcome outcome, CancelledBy by, string reason, CancellationToken ct)
    {
        // docs/07 §10 — ask the gateway that took the money before deciding
        // where it lands. RefundGateway picks the right one: a stay paid through
        // VNPay is refunded at VNPay, and one paid through the stand-in still
        // goes to the stand-in.
        var who = by switch
        {
            CancelledBy.Host => "host",
            CancelledBy.Platform or CancelledBy.ForceMajeure => "system",
            _ => "guest"
        };

        var sent = await refunds.SendAsync(
            booking, outcome.Amount, who, $"Huy don {booking.Reference}", ct);

        PostCancellation(db, booking, outcome, by, reason, sent);
        // docs/01 TC-09 — a cancelled stay hands its promo code back to the
        // campaign so a limited run is not spent on a booking that did not happen.
        await coupons.ReleaseAsync(booking.Id, ct);
        await db.SaveChangesAsync(ct);

        // "…và báo khách": money arriving somewhere they did not expect is worse
        // than money arriving late, so this is said rather than left to be found.
        // Loaded here rather than trusted from the entity: the cancel endpoint's
        // query does not Include the guest, so reading booking.GuestUser would
        // find null and this notice would quietly never be sent.
        if (sent.Bounced > 0 && booking.GuestUserId is { } guestId)
            await notifications.QueueWithEmailAsync(
                await db.Users.FirstOrDefaultAsync(u => u.Id == guestId, ct),
                NotificationKind.RefundIssued,
                "Tiền hoàn đã vào số dư Staylio",
                Refunds.RedirectNotice(sent.Bounced), $"/trips/{booking.Id}", ct);
    }

    /// <summary>A guest may review a stay once, after checkout.</summary>
    [HttpPost("{id:int}/review")]
    public async Task<IActionResult> Review(int id, [FromBody] SubmitReviewRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập để đánh giá." });

        // docs/08 §5.2 — a review ban stops the writing, not the staying.
        if (Restrictions.Has(user.RestrictionMask, RestrictionKind.NoReviews))
            return StatusCode(403, new { message = Restrictions.Message(RestrictionKind.NoReviews) });

        var booking = await db.Bookings.Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == id && b.GuestUserId == user.Id, ct);

        if (booking is null) return NotFound();
        if (booking.HasReview) return Conflict(new { message = "Bạn đã đánh giá chuyến đi này rồi." });

        // docs/03 §7: only a completed stay can be reviewed, and "completed" is
        // the lifecycle's own judgement, made in the listing's time zone.
        if (booking.Status != BookingStatus.Completed)
        {
            return BadRequest(new
            {
                message = BookingLifecycle.IsCancelled(booking.Status)
                    ? "Chuyến đi đã huỷ không thể đánh giá."
                    : "Bạn có thể đánh giá sau khi trả phòng."
            });
        }

        var text = (req.Text ?? "").Trim();
        if (text.Length < 10) return BadRequest(new { message = "Nội dung đánh giá cần tối thiểu 10 ký tự." });

        // docs/01 ĐG-09 — contact details and abuse are refused, not masked:
        // a review is permanent and public.
        var guard = ContentGuard.CheckReview(text);
        if (!guard.Ok) return BadRequest(new { message = guard.Message });

        if (DateTime.UtcNow > ReviewService.Deadline(booking))
            return BadRequest(new { message = "Đã quá 14 ngày kể từ ngày trả phòng." });

        double Clamp(double v) => Math.Clamp(v, 1, 5);

        var review = new Review
        {
            ListingId = booking.ListingId,
            BookingId = booking.Id,
            AuthorUserId = user.Id,
            // docs/01 TK-04 — a review is public, so it carries the name they
            // chose to be known by, not the one on their account.
            AuthorName = Profiles.DisplayNameOf(user.DisplayName, user.FullName),
            AuthorInitials = Profiles.InitialsOf(Profiles.DisplayNameOf(user.DisplayName, user.FullName)),
            When = Profiles.MonthLabel(DateTime.UtcNow),
            Text = text,
            PrivateNote = string.IsNullOrWhiteSpace(req.PrivateNote) ? null : req.PrivateNote.Trim(),
            EditableUntil = DateTime.UtcNow + ReviewService.EditWindow,
            Rating = Clamp(req.Rating),
            Cleanliness = Clamp(req.Cleanliness),
            Accuracy = Clamp(req.Accuracy),
            CheckIn = Clamp(req.CheckIn),
            Communication = Clamp(req.Communication),
            Location = Clamp(req.Location),
            Value = Clamp(req.Value),
            // docs/01 TĐ-11 — the writer's own interface language, so the reader's
            // filter has something exact to work from rather than a guess at the
            // characters. Unstated stays null and is guessed on the way out.
            Language = Profiles.LanguageCode(req.Language)
        };
        db.Reviews.Add(review);
        booking.HasReview = true;

        await db.SaveChangesAsync(ct);

        // docs/03 §7 — blind both ways: this only becomes visible once the host
        // has written one too, or the 14-day window closes.
        var published = await reviews.TryPublishAsync(booking.Id, ct);
        if (published) await db.SaveChangesAsync(ct);

        return Ok(new
        {
            published,
            message = published
                ? "Đánh giá của bạn và của chủ nhà đã được công khai."
                : "Đã gửi. Đánh giá sẽ hiện khi chủ nhà cũng gửi, hoặc sau 14 ngày."
        });
    }

    /// <summary>
    /// docs/01 ĐG-08 — the review this guest wrote, read back so they can
    /// correct it. Editing was written and tested but never reachable: no screen
    /// could show a guest what they had said, so nothing could offer to change
    /// it. The two conditions the PUT below enforces are answered here as one
    /// flag, so the button is not offered where the server would refuse it.
    /// </summary>
    [HttpGet("{id:int}/review")]
    public async Task<ActionResult<GuestReviewDto>> MyReview(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var review = await db.Reviews
            .FirstOrDefaultAsync(r => r.BookingId == id && r.AuthorUserId == user.Id, ct);
        if (review is null) return NotFound();

        var canEdit = review.PublishedAt is null
                      && (review.EditableUntil is not { } until || DateTime.UtcNow <= until);

        return Ok(new GuestReviewDto(
            review.Id, review.Text, review.Rating,
            review.Cleanliness, review.Accuracy, review.CheckIn,
            review.Communication, review.Location, review.Value,
            review.PrivateNote, review.EditableUntil, review.PublishedAt is not null, canEdit));
    }

    /// <summary>
    /// docs/01 ĐG-08 — the writer may correct a review inside 48 hours, and only
    /// while the other side has not written theirs. Once it is public, it stands.
    /// </summary>
    [HttpPut("{id:int}/review")]
    public async Task<IActionResult> EditReview(int id, [FromBody] SubmitReviewRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập để sửa đánh giá." });

        var review = await db.Reviews
            .FirstOrDefaultAsync(r => r.BookingId == id && r.AuthorUserId == user.Id, ct);
        if (review is null) return NotFound();

        if (review.PublishedAt is not null)
            return BadRequest(new { message = "Đánh giá đã công khai nên không sửa được nữa." });

        if (review.EditableUntil is { } until && DateTime.UtcNow > until)
            return BadRequest(new { message = "Đã quá 48 giờ kể từ khi gửi nên không sửa được nữa." });

        var text = (req.Text ?? "").Trim();
        if (text.Length < 10) return BadRequest(new { message = "Nội dung đánh giá cần tối thiểu 10 ký tự." });

        var guard = ContentGuard.CheckReview(text);
        if (!guard.Ok) return BadRequest(new { message = guard.Message });

        double Clamp(double v) => Math.Clamp(v, 1, 5);

        review.Text = text;
        review.Rating = Clamp(req.Rating);
        review.Cleanliness = Clamp(req.Cleanliness);
        review.Accuracy = Clamp(req.Accuracy);
        review.CheckIn = Clamp(req.CheckIn);
        review.Communication = Clamp(req.Communication);
        review.Location = Clamp(req.Location);
        review.Value = Clamp(req.Value);
        review.PrivateNote = string.IsNullOrWhiteSpace(req.PrivateNote) ? null : req.PrivateNote.Trim();
        // A correction may be typed with the interface in a different language
        // than the first draft was, and the text is what the filter is for.
        review.Language = Profiles.LanguageCode(req.Language) ?? review.Language;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// docs/07 §2.5, docs/01 ĐP-13 — finding a booking again with no account.
    ///
    /// The session cookie carries ownership for as long as it lasts, which is not
    /// long enough: a guest books on a phone and looks it up on a laptop, or
    /// clears their browser, and everything they have is the reference from the
    /// email and the address it went to. Both are required — a reference travels
    /// alone in a forwarded subject line.
    ///
    /// A match adopts the booking into this session, so every screen that reads
    /// by session works afterwards without a second lookup. Only a booking that
    /// belongs to no account can be adopted: an account's booking is reached by
    /// signing in, and the two must never be confusable.
    /// </summary>
    [HttpPost("lookup")]
    public async Task<ActionResult<BookingDto>> Lookup(
        [FromBody] LookupBookingRequest req, CancellationToken ct)
    {
        var reference = (req.Reference ?? "").Trim().ToUpperInvariant().Replace(" ", "");

        var booking = await db.Bookings
            .Include(b => b.Payment).Include(b => b.Events)
            .Include(b => b.Listing!).ThenInclude(l => l.Images)
            .Include(b => b.Listing!).ThenInclude(l => l.Host)
            .FirstOrDefaultAsync(b => b.Reference == reference && b.GuestUserId == null, ct);

        // One message for "no such booking" and "wrong email": telling them apart
        // turns this into a way of asking whether a reference exists.
        var notFound = new { message = "Không tìm thấy đơn nào khớp mã đặt chỗ và email này." };

        if (booking is null) return NotFound(notFound);

        if (!GuestCheckout.Matches(booking.GuestEmail, reference, req.Email, booking.Reference))
            return NotFound(notFound);

        var sid = HttpContext.SessionId();
        if (!string.IsNullOrEmpty(sid) && booking.SessionId != sid)
        {
            booking.SessionId = sid;
            await db.SaveChangesAsync(ct);
        }

        return Ok(ToDto(booking));
    }

    /// <summary>Single booking, used by the trip detail page and the printable receipt.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookingDto>> Detail(int id, CancellationToken ct)
    {
        var booking = await FindOwnedAsync(id, ct, includeListing: true);
        return booking is null ? NotFound() : Ok(ToDto(booking));
    }

    /// <summary>docs/09 §4 — how many of each the trip page is given. Six fills two
    /// rows of cards in the trip column without turning the page into a catalogue.</summary>
    private const int CrossSellCount = 6;

    /// <summary>
    /// docs/09 §4 (MR-C-02) — cross-sell: what a guest could do in the city of a
    /// stay they already hold, on the days they are there. §4 calls this the main
    /// revenue channel for both lines, so it hangs off the trip the guest is
    /// already looking at rather than waiting to be searched for.
    ///
    /// It sits on the booking, not on a new /api/trips route, because the trip page
    /// IS this booking (GET api/bookings/{id}) and the answer is derived from the
    /// booking's own city and dates — which means it has to pass through the same
    /// ownership check, <see cref="FindOwnedAsync"/>, and nothing weaker.
    /// </summary>
    [HttpGet("{id:int}/suggestions")]
    public async Task<ActionResult<StaySuggestionsDto>> Suggestions(int id, CancellationToken ct)
    {
        var booking = await FindOwnedAsync(id, ct, includeListing: true);
        if (booking is null) return NotFound();

        var city = booking.Listing?.City ?? "";
        var empty = new StaySuggestionsDto(city, booking.CheckIn, booking.CheckOut, [], []);

        // Only a stay that is going to happen sells anything: a cancelled, declined
        // or still-unpaid booking is not somebody who will be in that city.
        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.InProgress))
            return Ok(empty);

        // "Đúng khoảng ngày họ ở" — the nights of the stay. Check-out day is left
        // out on purpose: they hand the keys back around midday, so an afternoon
        // session that day is a suggestion they could not take.
        var from = booking.CheckIn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = booking.CheckOut.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        return Ok(empty with
        {
            Experiences = await experiences.SuggestForStayAsync(city, from, to, CrossSellCount, ct),
            Services = await market.SuggestForStayAsync(city, CrossSellCount, ct)
        });
    }

    /// <summary>
    /// docs/07 §5 — the trip to the bank's OTP page.
    ///
    /// Returns the response to send when the guest still has authenticating to
    /// do, and null once they are through and the charge may go ahead.
    /// </summary>
    private async Task<ActionResult<BookingDto>?> AuthenticateAsync(
        Booking booking, string key, decimal amount, string method, PayBookingRequest? req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var auth = await db.CardAuthentications.FirstOrDefaultAsync(a => a.AttemptKey == key, ct);

        if (auth is null)
        {
            auth = new CardAuthentication
            {
                BookingId = booking.Id, AttemptKey = key, Amount = amount,
                Method = method, CardLast4 = req?.CardLast4
            };
            db.CardAuthentications.Add(auth);
        }

        if (auth.Outcome == AuthOutcome.Succeeded) return null;

        // §5 — the guest may have authorised on the bank's page and only now got
        // back. The gateway is asked rather than the browser believed, so the
        // money that already moved is recognised instead of being taken twice.
        if (gateway.Lookup(key) == AuthOutcome.Succeeded)
        {
            auth.Outcome = AuthOutcome.Succeeded;
            auth.SettledAt ??= now;
            auth.ConfirmedWithGatewayAt = now;
            await db.SaveChangesAsync(ct);
            return null;
        }

        // §5.2 — the dates must not fall off the market while the guest is on
        // their bank's page. That would be our timer expiring, not them giving up.
        if (CardAuth.NeedsExtension(booking.HoldExpiresAt, now))
        {
            booking.HoldExpiresAt = CardAuth.ExtendedTo(booking.HoldExpiresAt, now);
            db.BookingEvents.Add(BookingLifecycle.Note(
                booking, "system", "Gia hạn giữ chỗ trong lúc khách xác thực với ngân hàng."));
        }

        // No code yet: this is the guest arriving, or coming back to a tab they
        // closed. Either way they are sent to the same place.
        if (string.IsNullOrWhiteSpace(req?.AuthenticationCode))
        {
            auth.Outcome = AuthOutcome.Pending;
            await db.SaveChangesAsync(ct);

            return Accepted(new CardAuthChallengeDto(
                key, booking.HoldExpiresAt, auth.CodeAttempts,
                CardAuth.MaxCodeAttempts - auth.CodeAttempts,
                CardAuth.OutcomeMessage(AuthOutcome.Pending, auth.CodeAttempts)));
        }

        if (!CardAuth.CanTryCodeAgain(auth.CodeAttempts))
        {
            await db.SaveChangesAsync(ct);
            return BadRequest(new
            {
                message = CardAuth.OutcomeMessage(AuthOutcome.WrongCode, auth.CodeAttempts),
                needsDifferentMethod = true
            });
        }

        // The code goes to the bank, and it is the bank that moves the money —
        // before this platform hears a word about it.
        var authorised = gateway.Authorise(key, amount, method, req.CardLast4, req.AuthenticationCode);

        if (!authorised.Ok)
        {
            auth.CodeAttempts++;
            auth.Outcome = AuthOutcome.WrongCode;
            await db.SaveChangesAsync(ct);

            return BadRequest(new
            {
                message = CardAuth.OutcomeMessage(AuthOutcome.WrongCode, auth.CodeAttempts),
                retryable = CardAuth.CanTryCodeAgain(auth.CodeAttempts),
                needsDifferentMethod = !CardAuth.CanTryCodeAgain(auth.CodeAttempts)
            });
        }

        auth.CodeAttempts++;
        auth.Outcome = AuthOutcome.Succeeded;
        auth.SettledAt = now;
        // The charge below is what the gateway will report; this row is confirmed
        // against it by the sweep rather than by anything the browser said.
        await db.SaveChangesAsync(ct);

        return null;
    }

    /// <summary>
    /// docs/07 §2.5 — telling both sides a pay-at-property booking is on.
    ///
    /// Split out because the guest may have no account: <see
    /// cref="NotificationService.QueueWithEmailAsync"/> takes a User and returns
    /// silently when there is none, so an anonymous guest would have been
    /// confirmed and told nothing at all.
    /// </summary>
    private async Task NotifyConfirmedAsync(Booking booking, CancellationToken ct)
    {
        var listing = booking.Listing!;

        var hostUser = await db.Users.FirstOrDefaultAsync(u => u.HostProfile!.Id == listing.HostId, ct);
        await notifications.QueueWithEmailAsync(hostUser, NotificationKind.BookingConfirmed,
            "Bạn có lượt đặt trả tiền tại nơi ở",
            $"{booking.GuestName} đặt \"{listing.Title}\" từ {booking.CheckIn:dd/MM} đến {booking.CheckOut:dd/MM} " +
            $"({booking.Nights} đêm, {booking.Guests} khách). {PayAtProperty.HostWarning}",
            "/hosting", ct);

        var line = $"Mã đặt chỗ {booking.Reference} · {listing.Title} · {booking.Nights} đêm. " +
                   PayAtProperty.Notice(booking.Total);

        if (booking.GuestUserId is { } guestId)
        {
            var guest = await db.Users.FirstOrDefaultAsync(u => u.Id == guestId, ct);
            await notifications.QueueWithEmailAsync(guest, NotificationKind.BookingConfirmed,
                "Đặt chỗ đã được xác nhận", line, $"/trips/{booking.Id}", ct);
        }
        else
        {
            notifications.QueueEmailOnly(booking.GuestEmail, booking.GuestName,
                "Đặt chỗ đã được xác nhận", line, "/dat-cho");
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<Booking?> FindOwnedAsync(int id, CancellationToken ct, bool includeListing = false)
    {
        var user = await auth.CurrentUserAsync(ct);
        var sid = HttpContext.SessionId();

        var query = db.Bookings.Include(b => b.Payment).Include(b => b.Events).AsQueryable();
        if (includeListing)
        {
            query = query
                .Include(b => b.Listing!).ThenInclude(l => l.Images)
                .Include(b => b.Listing!).ThenInclude(l => l.Host);
        }

        return await query.FirstOrDefaultAsync(b =>
            b.Id == id && (user != null ? b.GuestUserId == user.Id : b.SessionId == sid && b.GuestUserId == null), ct);
    }

    private static readonly System.Text.Json.JsonSerializerOptions LineJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static string SerializeLines(IReadOnlyList<PriceLine> lines) =>
        System.Text.Json.JsonSerializer.Serialize(
            lines.Select(l => new PriceLineDto(l.Key, l.Label, l.Amount)), LineJson);

    private static IReadOnlyList<PriceLineDto> DeserializeLines(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<PriceLineDto>>(json, LineJson) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static BookingDto ToDto(Booking b)
    {
        // "Can review" and "can cancel" are now decisions the lifecycle makes,
        // so this no longer compares dates itself.
        return new BookingDto(
            b.Id,
            b.Reference,
            b.ListingId,
            b.Listing?.Title ?? "",
            b.Listing?.City ?? "",
            b.Listing?.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault() ?? "",
            b.Listing?.Slug ?? "",
            b.CheckIn,
            b.CheckOut,
            b.Nights,
            b.Guests,
            b.Subtotal,
            b.CleaningFee,
            b.ServiceFee,
            b.Tax,
            b.Total,
            b.RefundedAmount,
            b.GoodwillCredit,
            DeserializeLines(b.PriceLinesJson),
            Cancellation.Label(b.CancellationTier),
            Cancellation.Summary(b.CancellationTier),
            b.Status.ToString(),
            BookingLifecycle.Label(b.Status),
            BookingLifecycle.BadgeClass(b.Status),
            b.Payment?.Status.ToString() ?? "Pending",
            b.Payment?.Reference,
            b.Payment?.Method,
            b.Payment?.CardLast4,
            b.HasReview,
            b.Status == BookingStatus.Completed && !b.HasReview,
            BookingLifecycle.CanTransition(b.Status, BookingStatus.CancelledByGuest),
            b.HoldExpiresAt,
            b.RequestExpiresAt,
            b.Events.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).Select(e => new BookingEventDto(
                e.FromStatus?.ToString(),
                e.FromStatus is null ? "" : BookingLifecycle.Label(e.FromStatus.Value),
                e.ToStatus.ToString(),
                BookingLifecycle.Label(e.ToStatus),
                e.Actor, e.Reason, e.CreatedAt)).ToList(),
            b.GuestNote,
            b.Listing?.Host?.Name ?? "",
            b.CreatedAt,
            b.DepositPaid,
            b.BalanceDue,
            b.BalanceDueOn,
            b.BalanceStatus.ToString(),
            PartialPayment.Label(b.BalanceStatus),
            BuildGuide(b),
            GatewayRedirectUrl: null,
            GatewayOrderRef: null,
            CanPriceMatch: b.Listing?.IsHotel == true
                           && HotelRules.WithinWindow(b.CreatedAt, DateTime.UtcNow),
            PaidAtProperty: b.PaidAtProperty,
            CashCollectedAt: b.CashCollectedAt)
        {
            Adults = b.Adults, Children = b.Children, Infants = b.Infants, Pets = b.Pets
        };
    }

    /// <summary>
    /// docs/01 CĐ-03 and CĐ-04 — the arrival guide, filtered by docs/03 §10
    /// before it leaves the server. Withheld fields are absent rather than
    /// blank, so a door code the guest may not see yet is never in the response
    /// for them to read out of it.
    /// </summary>
    private static CheckInGuideDto? BuildGuide(Booking b)
    {
        if (b.Listing is not { } l || !CheckInGuide.CanSeeGuide(b.Status)) return null;

        var localNow = BookingService.LocalNow(l);
        var codeReady = CheckInGuide.CanSeeDoorCode(b.Status, b.CheckIn, l.CheckInFrom, localNow);
        var hasCode = !string.IsNullOrWhiteSpace(l.DoorCode);

        return new CheckInGuideDto(
            CheckInGuide.WindowLabel(l.CheckInFrom, l.CheckInTo, l.CheckOutBefore),
            CheckInGuide.MethodLabel(l.CheckInMethod),
            l.AddressLine,
            l.Directions,
            l.WifiName,
            l.WifiPassword,
            CheckInGuide.Lines(l.ApplianceNotes),
            l.HostPhone,
            hasCode && codeReady ? l.DoorCode : null,
            hasCode,
            hasCode && !codeReady ? CheckInGuide.DoorCodeWaitNote(b.CheckIn, l.CheckInFrom) : null);
    }
}
