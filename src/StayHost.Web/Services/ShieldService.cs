using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;

namespace StayHost.Web.Services;

/// <summary>
/// docs/06 — Staylio Shield. Filing, answering, deciding and paying, plus the fund
/// behind it. Every number comes from <see cref="ShieldSettings"/>; nothing in
/// here invents a limit of its own.
/// </summary>
public class ShieldService(
    StayHostDbContext db,
    WalletService wallet,
    NotificationService notifications,
    ILogger<ShieldService> log)
{
    /* ------------------------------------------------------------ filing */

    public async Task<(ShieldClaim? Claim, string? Error)> FileAsync(
        User user, int bookingId, OpenShieldClaimRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<ShieldCase>(req.Kind, true, out var kind))
            return (null, "Không nhận ra tình huống này.");

        var booking = await db.Bookings
            .Include(b => b.Listing!).ThenInclude(l => l.Host)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return (null, "Không tìm thấy đơn này.");

        var side = Shield.SideOf(kind);
        var hostUserId = await HostUserIdAsync(booking, ct);

        // A guest files on their own booking; a host files on a booking of theirs.
        var allowed = side == ShieldSide.Guest
            ? booking.GuestUserId == user.Id
            : hostUserId == user.Id;
        if (!allowed) return (null, "Bạn không mở được hồ sơ cho đơn này.");

        var listing = booking.Listing!;
        var checkInAt = BookingService.LocalNow(listing) is var _
            ? booking.CheckIn.ToDateTime(new TimeOnly(14, 0), DateTimeKind.Utc)
            : default;
        var checkOutAt = booking.CheckOut.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

        var items = (req.Items ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.Name) && i.Value > 0)
            .Select(i => new ShieldItem
            {
                Name = i.Name!.Trim(),
                Value = i.Value,
                DeclaredOnListing = i.DeclaredOnListing,
                Allowed = Shield.AllowedForItem(i.Value, i.DeclaredOnListing)
            })
            .ToList();

        var claimed = side == ShieldSide.Host ? items.Sum(i => i.Allowed) : 0m;

        /*
         * docs/06 §10 C-D — lost income is capped at five nights.
         *
         * A C3 case travels through the same free-text item list as C1 and C2,
         * so before this it was only ever held to the high-value *item* ceiling,
         * which is about a stolen camera and has nothing to say about nights.
         * Shield.LostIncome existed and was tested from the day the parameter
         * was locked; nothing called it, so a host could enter any figure at all
         * under "mất thu nhập" and the cap the customer signed off on did
         * nothing.
         *
         * Nights are valued at what this booking actually earned per night —
         * room money only, since a cancelled stay costs no cleaning.
         */
        if (kind == ShieldCase.C3 && booking.Nights > 0)
        {
            var perNight = Math.Max(0m, booking.Subtotal - booking.CleaningFee) / booking.Nights;
            var ceiling = Shield.LostIncome(perNight, ShieldSettings.Current.LostIncomeNights);
            claimed = Math.Min(claimed, ceiling);
        }

        // Talking to the other side first is a rule, and it has to be in this
        // inbox — docs/06 §2.2 does not count what happened elsewhere.
        var contactedAt = await FirstMessageAtAsync(booking, user.Id, ct);

        var nextGuest = side == ShieldSide.Host
            ? await db.Bookings
                .Where(b => b.ListingId == booking.ListingId && b.Id != booking.Id
                            && b.CheckIn >= booking.CheckOut
                            && BookingLifecycle.BlocksDates.Contains(b.Status))
                .OrderBy(b => b.CheckIn)
                .Select(b => (DateOnly?)b.CheckIn)
                .FirstOrDefaultAsync(ct)
            : null;

        var check = Shield.CanFile(new Shield.Request
        {
            Kind = kind,
            Now = DateTime.UtcNow,
            CheckInAt = checkInAt,
            CheckOutAt = checkOutAt,
            HostContactedAt = contactedAt,
            Urgent = req.Urgent,
            NextGuestArrivesAt = nextGuest?.ToDateTime(new TimeOnly(14, 0), DateTimeKind.Utc),
            ThirdParty = req.ThirdPartyName,
            EvidenceCount = (req.Evidence ?? []).Count,
            AlreadyHasOpenCase = await db.ShieldClaims.AnyAsync(
                c => c.BookingId == bookingId
                     && c.Status != ShieldStatus.Settled && c.Status != ShieldStatus.Rejected, ct),
            PaidThroughPlatform = true,
            Claimed = claimed
        });

        if (!check.Ok) return (null, check.Message);

        var now = DateTime.UtcNow;
        var yearAgo = now.AddYears(-1);
        var priorCases = await db.ShieldClaims.CountAsync(
            c => c.OpenedByUserId == user.Id && c.CreatedAt >= yearAgo, ct);

        // docs/06 §5 — an empty fund never turns anybody away; it sends the case
        // to a person instead.
        var fundBalance = await db.ShieldFundMovements.SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;

        var claim = new ShieldClaim
        {
            Reference = $"SS{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            BookingId = booking.Id,
            OpenedByUserId = user.Id,
            Side = side,
            Kind = kind,
            Description = (req.Description ?? "").Trim(),
            Claimed = claimed,
            ExpensesClaimed = Math.Max(0m, req.ExpensesClaimed),
            RehousingDifference = Math.Max(0m, req.RehousingDifference),
            ThirdPartyName = req.ThirdPartyName?.Trim(),
            ThirdPartyContact = req.ThirdPartyContact?.Trim(),
            ThirdPartyKind = req.ThirdPartyKind?.Trim(),
            RespondBy = now + Shield.ResponseWindow,
            FirstResponseDueAt = now + Shield.FirstResponseDue(kind),
            DecisionDueAt = now + Shield.DecisionDue(kind),
            NeedsManualReview = Shield.NeedsManualReview(priorCases, false, fundBalance <= 0),
            Items = items,
            Evidence = (req.Evidence ?? [])
                .Where(e => !string.IsNullOrWhiteSpace(e.Url))
                .Select(e => new ShieldEvidence
                {
                    Url = e.Url!.Trim(),
                    Caption = e.Caption?.Trim(),
                    Kind = string.IsNullOrWhiteSpace(e.Kind) ? "photo" : e.Kind!.Trim()
                })
                .ToList()
        };

        claim.Events.Add(new ShieldEvent
        {
            ToStatus = ShieldStatus.Open,
            Actor = $"{side.ToString().ToLower()}:{user.Id}",
            Note = $"Mở hồ sơ {Shield.CaseLabel(kind)}."
        });

        db.ShieldClaims.Add(claim);
        await db.SaveChangesAsync(ct);

        var otherId = side == ShieldSide.Guest ? hostUserId : booking.GuestUserId;
        await NotifyAsync(otherId,
            "Có hồ sơ Staylio Shield cần bạn phản hồi",
            $"{Shield.CaseLabel(kind)} · đơn {booking.Reference}. Bạn có 24 giờ để phản hồi.",
            "/shield", ct);

        log.LogInformation("Shield claim {Reference} opened ({Kind}).", claim.Reference, kind);
        return (claim, null);
    }

    /// <summary>
    /// docs/06 §2.1 K1 — a host walking away inside 30 days of check-in opens
    /// its own case. Nobody has to notice and file it.
    /// </summary>
    public async Task<ShieldClaim?> OpenHostCancellationAsync(Booking booking, CancellationToken ct)
    {
        if (booking.GuestUserId is not { } guestId) return null;

        var checkInAt = booking.CheckIn.ToDateTime(new TimeOnly(14, 0), DateTimeKind.Utc);
        if (checkInAt - DateTime.UtcNow > Shield.HostCancelWindow) return null;
        if (await db.ShieldClaims.AnyAsync(c => c.BookingId == booking.Id && c.Kind == ShieldCase.K1, ct)) return null;

        var now = DateTime.UtcNow;
        var claim = new ShieldClaim
        {
            Reference = $"SS{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            BookingId = booking.Id,
            OpenedByUserId = guestId,
            Side = ShieldSide.Guest,
            Kind = ShieldCase.K1,
            Status = ShieldStatus.UnderReview,
            Description = "Chủ nhà huỷ đơn đã xác nhận trong vòng 30 ngày trước ngày nhận phòng.",
            RespondBy = now,
            FirstResponseDueAt = now + Shield.FirstResponseDue(ShieldCase.K1),
            DecisionDueAt = now + Shield.DecisionDue(ShieldCase.K1)
        };

        claim.Events.Add(new ShieldEvent
        {
            ToStatus = ShieldStatus.UnderReview,
            Actor = "system",
            Note = "Mở tự động vì chủ nhà huỷ sát ngày."
        });

        db.ShieldClaims.Add(claim);
        await db.SaveChangesAsync(ct);

        await NotifyAsync(guestId,
            "Staylio đang lo chỗ ở thay thế cho bạn",
            $"Đơn {booking.Reference} bị chủ nhà huỷ. Chúng tôi sẽ liên hệ trong vòng 4 giờ.",
            "/shield", ct);

        return claim;
    }

    /* ---------------------------------------------------------- answering */

    public async Task<string?> RespondAsync(
        User user, int claimId, RespondShieldRequest req, CancellationToken ct)
    {
        var claim = await LoadAsync(claimId, ct);
        if (claim is null) return "Không tìm thấy hồ sơ này.";
        if (claim.Status != ShieldStatus.Open) return "Hồ sơ này không còn chờ phản hồi.";

        var hostUserId = await HostUserIdAsync(claim.Booking!, ct);
        var respondentId = claim.Side == ShieldSide.Guest ? hostUserId : claim.Booking!.GuestUserId;
        if (respondentId != user.Id) return "Bạn không phải bên được yêu cầu phản hồi.";

        var next = req.Answer?.ToLowerInvariant() switch
        {
            "accept" => ShieldStatus.Accepted,
            "partial" => ShieldStatus.PartiallyAccepted,
            _ => ShieldStatus.UnderReview
        };

        // Agreeing in part settles the agreed slice and sends the rest to a person.
        if (next == ShieldStatus.PartiallyAccepted)
            claim.Claimed = Math.Clamp(req.AgreedAmount ?? 0m, 0m, claim.Claimed);

        Move(claim, next, $"{claim.Side.Opposite()}:{user.Id}", (req.Note ?? "").Trim());
        await db.SaveChangesAsync(ct);

        await NotifyAsync(claim.OpenedByUserId,
            "Bên kia đã phản hồi hồ sơ Staylio Shield",
            $"{Shield.StatusLabel(next)} · hồ sơ {claim.Reference}.",
            "/shield", ct);

        return null;
    }

    /* ----------------------------------------------------------- deciding */

    /// <summary>
    /// docs/06 §6 — the decision, and the money that follows it. Both halves
    /// happen together so a case never sits decided but unpaid.
    /// </summary>
    public async Task<string?> DecideAsync(
        User admin, int claimId, DecideShieldRequest req, CancellationToken ct)
    {
        var claim = await LoadAsync(claimId, ct);
        if (claim is null) return "Không tìm thấy hồ sơ này.";
        if (claim.Status is ShieldStatus.Settled or ShieldStatus.Rejected)
            return "Hồ sơ này đã được xử lý.";
        if (claim.Status == ShieldStatus.Appealed && claim.DecidedByUserId == admin.Id)
            return "Khiếu nại phải do người khác xét (docs/06 §6).";

        var booking = claim.Booking!;
        var now = DateTime.UtcNow;

        if (!req.Approve)
        {
            claim.Status = ShieldStatus.Rejected;
            claim.Decision = (req.Reason ?? "").Trim();
            claim.DecidedByUserId = admin.Id;
            claim.DecidedAt = now;
            Move(claim, ShieldStatus.Rejected, $"admin:{admin.Id}", claim.Decision);

            await db.SaveChangesAsync(ct);
            await NotifyDecisionAsync(claim, booking, ct);
            return null;
        }

        return claim.Side == ShieldSide.Guest
            ? await SettleGuestAsync(admin, claim, booking, req, now, ct)
            : await SettleHostAsync(admin, claim, booking, req, now, ct);
    }

    private async Task<string?> SettleGuestAsync(
        User admin, ShieldClaim claim, Booking booking, DecideShieldRequest req, DateTime now,
        CancellationToken ct)
    {
        var remedy = Enum.TryParse<ShieldRemedy>(req.Remedy, true, out var parsed) ? parsed : ShieldRemedy.Refunded;

        var outcome = Shield.SettleGuest(
            claim.Kind, booking.Total, booking.HostPayout, booking.Nights,
            req.NightsUnused ?? booking.Nights,
            claim.ExpensesClaimed, claim.RehousingDifference, remedy);

        claim.Remedy = remedy;
        claim.Approved = outcome.Refund + outcome.Expenses + outcome.RehousingTopUp;
        claim.CreditGranted = outcome.Credit;
        claim.PaidFromFund = outcome.FromFund;

        // The host loses their share of what went back; the fund carries the rest.
        db.LedgerEntries.AddRange(Ledger.PayFromShield(claim, outcome.FromFund, 0m, now));
        RecordFund(claim, -outcome.FromFund, FundMovementKind.Payout,
            $"Chi hồ sơ {claim.Reference} ({Shield.CaseLabel(claim.Kind)})", now);

        if (outcome.Credit > 0 && booking.GuestUserId is { } guestId)
        {
            wallet.Add(guestId, outcome.Credit, CreditReason.Goodwill,
                $"Staylio Shield · {Shield.CaseLabel(claim.Kind)}", booking.Id);
            db.LedgerEntries.AddRange(
                Ledger.GrantCredit(booking, outcome.Credit, "Số dư Staylio Shield", now));
        }

        Finish(claim, admin, req.Reason, outcome.Summary, now);
        await db.SaveChangesAsync(ct);
        await NotifyDecisionAsync(claim, booking, ct);
        return null;
    }

    private async Task<string?> SettleHostAsync(
        User admin, ShieldClaim claim, Booking booking, DecideShieldRequest req, DateTime now,
        CancellationToken ct)
    {
        var hostUserId = await HostUserIdAsync(booking, ct);
        var yearAgo = now.AddYears(-1);

        // docs/06 §3.2 C-B — the yearly ceiling counts what this host already had.
        var paidThisYear = await db.ShieldClaims
            .Where(c => c.Side == ShieldSide.Host && c.Id != claim.Id
                        && c.Status == ShieldStatus.Settled && c.DecidedAt >= yearAgo
                        && c.Booking!.Listing!.Host!.UserId == hostUserId)
            .SumAsync(c => (decimal?)c.Approved, ct) ?? 0m;

        // docs/06 §3.3 — what the guest and the host settled between themselves
        // first, then the fund for whatever is left.
        //
        // The customer settled this on 17/08/2026: damage is paid in cash at the
        // door, host to guest, while the guest is still there. So this number is
        // a record of a conversation the platform was not part of, not money it
        // collected — and the ledger below must not pretend otherwise.
        var paidInCash = Math.Max(0m, req.RecoverFromGuest ?? 0m);
        var thirdParty = Shield.IsThirdParty(claim.Kind);

        // docs/06 §3.3 (17/08/2026) — the fund does not pay for damage. If the
        // guest walks out without settling, the host carries it; Staylio rules
        // on the amount and stops there.
        var outcome = Shield.SettleHost(
            req.ApprovedAmount ?? claim.Claimed, req.DepositAvailable ?? 0m, paidInCash, paidThisYear,
            thirdParty: thirdParty, fundCovers: Shield.FundCovers(claim.Kind));

        claim.Approved = outcome.Approved;
        claim.Deductible = outcome.Deductible;
        claim.RecoveredFromCounterparty = outcome.FromDeposit + outcome.FromGuest;
        claim.PaidFromFund = outcome.FromFund;

        // Cash that passed from one person to another at a front door is not a
        // movement on this platform's books, and posting it would be inventing
        // one: ChargeCounterparty debits GuestFunds — money Staylio is holding —
        // and by checkout there is none of it left to debit. Recording it here
        // would have credited the host a second time for money they already had
        // in their hand.
        //
        // A third party's claim (C4) is different and still runs through the
        // platform: the injured party is not on the booking, has no way to be
        // paid at the door, and the money comes from the fund rather than from
        // the guest's pocket.
        if (thirdParty && claim.RecoveredFromCounterparty > 0)
            db.LedgerEntries.AddRange(
                Ledger.ChargeForThirdParty(claim, claim.RecoveredFromCounterparty, now));

        if (outcome.FromFund > 0)
        {
            db.LedgerEntries.AddRange(thirdParty
                ? Ledger.PayFromShield(claim, 0m, 0m, now, outcome.FromFund)
                : Ledger.PayFromShield(claim, 0m, outcome.FromFund, now));

            RecordFund(claim, -outcome.FromFund, FundMovementKind.Payout,
                $"Chi hồ sơ {claim.Reference} ({Shield.CaseLabel(claim.Kind)})", now);
        }

        Finish(claim, admin, req.Reason, outcome.Summary, now);
        await db.SaveChangesAsync(ct);
        await NotifyDecisionAsync(claim, booking, ct);
        return null;
    }

    /// <summary>docs/06 §5 — money chased down later goes back into the fund.</summary>
    public async Task<string?> RecoverAsync(int claimId, decimal amount, CancellationToken ct)
    {
        var claim = await LoadAsync(claimId, ct);
        if (claim is null) return "Không tìm thấy hồ sơ này.";
        if (claim.PaidFromFund <= 0) return "Hồ sơ này không chi từ quỹ.";

        var take = Math.Clamp(amount, 0m, claim.PaidFromFund - claim.RecoveredLater);
        if (take <= 0) return "Đã thu hồi hết phần quỹ đã chi.";

        var now = DateTime.UtcNow;
        claim.RecoveredLater += take;

        db.LedgerEntries.AddRange(Ledger.RecoverToShield(claim, take, now));
        RecordFund(claim, take, FundMovementKind.Recovery, $"Thu hồi hồ sơ {claim.Reference}", now);

        db.ShieldEvents.Add(new ShieldEvent
        {
            ClaimId = claim.Id,
            FromStatus = claim.Status,
            ToStatus = claim.Status,
            Actor = "system",
            Note = $"Thu hồi {Vnd.Format(take)} về quỹ."
        });

        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>docs/06 §6 — one appeal, and a different person has to look at it.</summary>
    public async Task<string?> AppealAsync(User user, int claimId, string? reason, CancellationToken ct)
    {
        var claim = await LoadAsync(claimId, ct);
        if (claim is null) return "Không tìm thấy hồ sơ này.";
        if (claim.OpenedByUserId != user.Id) return "Chỉ người mở hồ sơ mới khiếu nại được.";

        if (!Shield.CanAppeal(claim.Status, claim.DecidedAt, claim.Appealed, DateTime.UtcNow))
            return claim.Appealed
                ? "Mỗi hồ sơ chỉ được khiếu nại một lần."
                : "Chỉ khiếu nại được trong 7 ngày kể từ khi có quyết định.";

        claim.Appealed = true;
        claim.AppealReviewerUserId = null;
        Move(claim, ShieldStatus.Appealed, $"{claim.Side.ToString().ToLower()}:{user.Id}", (reason ?? "").Trim());

        await db.SaveChangesAsync(ct);
        return null;
    }

    /* ------------------------------------------------------------- sweep */

    /// <summary>
    /// docs/06 §6 — silence for 24 hours sends a case to a person. Also tops the
    /// fund up once a month out of what the service fee actually earned (§5).
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var lapsed = await db.ShieldClaims
            .Where(c => c.Status == ShieldStatus.Open && c.RespondBy <= now)
            .ToListAsync(ct);

        foreach (var claim in lapsed)
            Move(claim, ShieldStatus.UnderReview, "system", "Quá 24 giờ không có phản hồi.");

        var moved = lapsed.Count + await TopUpFundAsync(now, ct);
        if (moved > 0) await db.SaveChangesAsync(ct);

        return moved;
    }

    /// <summary>
    /// docs/06 §5 — the month's set-aside, worked out from the service-fee
    /// revenue actually posted to the ledger and written once per month.
    /// </summary>
    private async Task<int> TopUpFundAsync(DateTime now, CancellationToken ct)
    {
        var period = new DateOnly(now.Year, now.Month, 1);
        if (await db.ShieldFundMovements.AnyAsync(
                m => m.Period == period && m.Kind == FundMovementKind.Contribution, ct))
            return 0;

        var from = period.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = from.AddMonths(1);

        var revenue = await db.LedgerEntries
            .Where(e => (e.Account == LedgerAccount.GuestServiceFeeRevenue
                         || e.Account == LedgerAccount.HostServiceFeeRevenue)
                        && e.CreatedAt >= from && e.CreatedAt < to)
            .SumAsync(e => (decimal?)(e.Direction == LedgerDirection.Credit ? e.Amount : -e.Amount), ct) ?? 0m;

        var contribution = Shield.FundContribution(revenue);
        if (contribution <= 0) return 0;

        db.LedgerEntries.AddRange(Ledger.FundShield(contribution, $"{period:MM/yyyy}", now));
        RecordFund(null, contribution, FundMovementKind.Contribution,
            $"Trích {Shield.FundContribution(1m) * 100:0.#}% phí dịch vụ tháng {period:MM/yyyy}", now);

        log.LogInformation("Staylio Shield fund topped up by {Amount} for {Period}.", contribution, period);
        return 1;
    }

    /* ------------------------------------------------------------ AT-06-08 */

    /// <summary>
    /// docs/06 AT-06-08 and section 2.3 level 1 — somewhere equivalent, in the same
    /// area, free for the nights the guest still has, big enough for the party
    /// that booked. The difference is shown against what they already paid for
    /// those nights, so support can see at a glance which options fall inside
    /// what the platform will cover.
    /// </summary>
    public async Task<RehousingDto?> RehousingAsync(
        int claimId, CatalogService catalog, string sessionId, CancellationToken ct)
    {
        var claim = await db.ShieldClaims
            .Include(c => c.Booking!).ThenInclude(b => b.Listing)
            .FirstOrDefaultAsync(c => c.Id == claimId, ct);
        if (claim?.Booking?.Listing is null) return null;

        var booking = claim.Booking;
        var original = booking.Listing!;

        // Only the nights still ahead need replacing; a night already slept in
        // is not something anybody can re-book.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = booking.CheckIn > today ? booking.CheckIn : today;
        var to = booking.CheckOut;
        if (to <= from) return null;

        var nights = to.DayNumber - from.DayNumber;
        var party = new PartySize(booking.Adults, booking.Children, booking.Infants, booking.Pets);

        var settings = ShieldSettings.Current;
        var paidForThose = booking.Nights > 0
            ? Math.Round(booking.Total * nights / booking.Nights, 0, MidpointRounding.AwayFromZero)
            : booking.Total;
        var ceiling = Math.Round(booking.Total * settings.RehousingTopUpRate, 0, MidpointRounding.AwayFromZero);

        // The ordinary search already knows how to keep taken dates out and how
        // to price a card exactly as checkout would.
        var query = new CatalogService.SearchQuery(
            Q: original.City, Category: null, MinPrice: null, MaxPrice: null,
            Guests: Math.Max(1, booking.Guests), Amenities: [], Sort: "rating",
            RoomType: null, Bedrooms: null, Beds: null, Bathrooms: null,
            SuperhostOnly: false, GuestFavoriteOnly: false, InstantBookOnly: false,
            FreeCancellationOnly: false, Page: 1, PageSize: 12,
            CheckIn: from, CheckOut: to);

        var results = await catalog.SearchAsync(query, sessionId, ct);

        var options = results.Items
            .Where(l => l.Id != original.Id && l.MaxGuests >= booking.Guests)
            .Select(l => new RehousingOptionDto(
                l.Id, l.Slug, l.Title, l.City, l.TypeLabel, l.MaxGuests, l.Bedrooms,
                l.Rating, l.ReviewCount, l.Images.FirstOrDefault(),
                l.StayTotal ?? 0m,
                Math.Max(0m, (l.StayTotal ?? 0m) - paidForThose),
                Math.Max(0m, (l.StayTotal ?? 0m) - paidForThose) <= ceiling,
                Math.Round(ServiceRules.DistanceKm(
                    original.Latitude, original.Longitude, l.Latitude, l.Longitude), 1)))
            .OrderBy(o => o.WithinTopUp ? 0 : 1)
            .ThenBy(o => o.Difference)
            .ToList();

        return new RehousingDto(
            claim.Id, claim.Reference, original.Title, original.City,
            from, to, nights, booking.Guests, paidForThose, ceiling, options);
    }

    /* ------------------------------------------------------------- reads */

    public async Task<ShieldFundDto> FundAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var period = new DateOnly(now.Year, now.Month, 1);

        var movements = await db.ShieldFundMovements.ToListAsync(ct);
        var thisMonth = movements.Where(m => m.Period == period).ToList();

        var contributed = thisMonth.Where(m => m.Kind == FundMovementKind.Contribution).Sum(m => m.Amount);
        var spent = -thisMonth.Where(m => m.Kind == FundMovementKind.Payout).Sum(m => m.Amount);
        var recovered = thisMonth.Where(m => m.Kind == FundMovementKind.Recovery).Sum(m => m.Amount);

        var byKind = await db.ShieldClaims
            .GroupBy(c => c.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count(), Paid = g.Sum(x => x.PaidFromFund) })
            .ToListAsync(ct);

        var settings = ShieldSettings.Current;

        return new ShieldFundDto(
            movements.Sum(m => m.Amount),
            contributed, spent, recovered,
            Shield.FundAlarm(spent, contributed),
            settings.FundContributionRate,
            settings.FundAlarmRate,
            byKind
                .OrderByDescending(x => x.Paid)
                .Select(x => new ShieldCaseTotalDto(
                    x.Kind.ToString(), Shield.CaseLabel(x.Kind), x.Count, x.Paid))
                .ToList());
    }

    public async Task<IReadOnlyList<ShieldClaimDto>> MineAsync(int userId, CancellationToken ct)
    {
        var hostProfile = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == userId, ct);

        var claims = await Detailed()
            .Where(c => c.OpenedByUserId == userId
                        || c.Booking!.GuestUserId == userId
                        || (hostProfile != null && c.Booking!.Listing!.HostId == hostProfile.Id))
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return claims.Select(c => ToDto(c, userId)).ToList();
    }

    public async Task<IReadOnlyList<ShieldClaimDto>> QueueAsync(bool includeClosed, CancellationToken ct)
    {
        var query = Detailed();
        if (!includeClosed)
            query = query.Where(c => c.Status != ShieldStatus.Settled && c.Status != ShieldStatus.Rejected);

        var claims = await query
            .OrderBy(c => c.Status == ShieldStatus.UnderReview ? 0 : 1)
            .ThenBy(c => c.DecisionDueAt)
            .Take(200)
            .ToListAsync(ct);

        return claims.Select(c => ToDto(c, null)).ToList();
    }

    /// <summary>
    /// One case, for somebody who is part of it — the same three parties
    /// <see cref="MineAsync"/> lists — or for an admin. It used to answer any
    /// signed-in user for any id, so counting upwards read every case's story,
    /// evidence photos and a third party's contact details.
    /// </summary>
    public async Task<ShieldClaimDto?> OneAsync(int id, User viewer, CancellationToken ct)
    {
        var claim = await Detailed().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null) return null;

        if (viewer.Role != UserRole.Admin
            && claim.OpenedByUserId != viewer.Id
            && claim.Booking?.GuestUserId != viewer.Id)
        {
            var hostProfile = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == viewer.Id, ct);
            if (hostProfile is null || claim.Booking?.Listing?.HostId != hostProfile.Id) return null;
        }

        return ToDto(claim, viewer.Id);
    }

    private IQueryable<ShieldClaim> Detailed() =>
        db.ShieldClaims
            .Include(c => c.Booking!).ThenInclude(b => b.Listing)
            .Include(c => c.OpenedByUser)
            .Include(c => c.Evidence)
            .Include(c => c.Items)
            .Include(c => c.Events)
            .AsSplitQuery();

    private static ShieldClaimDto ToDto(ShieldClaim c, int? viewerId) => new(
        c.Id, c.Reference, c.BookingId, c.Booking?.Reference ?? "",
        c.Booking?.Listing?.Title ?? "", c.Booking?.Listing?.Slug ?? "",
        c.Side.ToString(), c.Kind.ToString(), Shield.CaseLabel(c.Kind),
        c.Status.ToString(), Shield.StatusLabel(c.Status), Shield.StatusBadge(c.Status),
        c.Description, c.Claimed, c.ExpensesClaimed, c.RehousingDifference,
        c.Remedy.ToString(), c.Approved, c.Deductible, c.CreditGranted,
        c.PaidFromFund, c.RecoveredFromCounterparty, c.RecoveredLater,
        c.ThirdPartyName, c.ThirdPartyContact, c.ThirdPartyKind,
        c.Decision, c.DecidedAt, c.Appealed,
        c.NeedsManualReview, c.RespondBy, c.FirstResponseDueAt, c.DecisionDueAt, c.CreatedAt,
        c.OpenedByUser?.FullName ?? "",
        viewerId is { } v && c.OpenedByUserId == v,
        c.Evidence.OrderBy(e => e.Id)
            .Select(e => new ShieldEvidenceDto(e.Id, e.Url, e.Caption, e.Kind)).ToList(),
        c.Items.OrderBy(i => i.Id)
            .Select(i => new ShieldItemDto(i.Id, i.Name, i.Value, i.DeclaredOnListing, i.Allowed)).ToList(),
        c.Events.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new ShieldEventDto(
                e.Id,
                e.FromStatus?.ToString(),
                e.ToStatus.ToString(),
                Shield.StatusLabel(e.ToStatus),
                e.Actor, e.Note, e.CreatedAt))
            .ToList());

    /* ----------------------------------------------------------- helpers */

    private Task<ShieldClaim?> LoadAsync(int id, CancellationToken ct) =>
        db.ShieldClaims
            .Include(c => c.Booking!).ThenInclude(b => b.Listing)
            .Include(c => c.Items)
            .Include(c => c.Events)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    private static void Move(ShieldClaim claim, ShieldStatus to, string actor, string note)
    {
        claim.Events.Add(new ShieldEvent
        {
            ClaimId = claim.Id,
            FromStatus = claim.Status,
            ToStatus = to,
            Actor = actor,
            Note = note
        });
        claim.Status = to;
    }

    private void Finish(ShieldClaim claim, User admin, string? reason, string summary, DateTime now)
    {
        claim.Decision = string.IsNullOrWhiteSpace(reason) ? summary : $"{reason!.Trim()} — {summary}";
        claim.DecidedByUserId = admin.Id;
        claim.DecidedAt = now;
        claim.SettledAt = now;
        if (claim.Appealed) claim.AppealReviewerUserId = admin.Id;

        Move(claim, ShieldStatus.Settled, $"admin:{admin.Id}", claim.Decision);
    }

    private void RecordFund(ShieldClaim? claim, decimal amount, FundMovementKind kind, string memo, DateTime at) =>
        db.ShieldFundMovements.Add(new ShieldFundMovement
        {
            Kind = kind,
            Amount = amount,
            ClaimId = claim?.Id,
            Memo = memo,
            Period = new DateOnly(at.Year, at.Month, 1)
        });

    private Task<int?> HostUserIdAsync(Booking booking, CancellationToken ct) =>
        db.Listings
            .Where(l => l.Id == booking.ListingId)
            .Select(l => l.Host!.UserId)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// docs/06 §2.2 — the record of talking to the other side has to be in this
    /// inbox. Anything said elsewhere does not count.
    /// </summary>
    private async Task<DateTime?> FirstMessageAtAsync(Booking booking, int senderId, CancellationToken ct) =>
        await db.Messages
            .Where(m => m.SenderUserId == senderId && !m.IsSystem
                        && m.Thread!.ListingId == booking.ListingId
                        && m.Thread.GuestUserId == booking.GuestUserId)
            .OrderBy(m => m.SentAt)
            .Select(m => (DateTime?)m.SentAt)
            .FirstOrDefaultAsync(ct);

    private async Task NotifyAsync(int? userId, string title, string body, string link, CancellationToken ct)
    {
        if (userId is not { } id) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        await notifications.QueueWithEmailAsync(user, NotificationKind.System, title, body, link, ct);
    }

    private async Task NotifyDecisionAsync(ShieldClaim claim, Booking booking, CancellationToken ct)
    {
        var settled = claim.Status == ShieldStatus.Settled;

        await NotifyAsync(claim.OpenedByUserId,
            settled ? "Hồ sơ Staylio Shield đã được xử lý" : "Hồ sơ Staylio Shield không được chấp nhận",
            settled
                ? $"{claim.Decision} Số tiền {Vnd.Format(claim.Approved)} · đơn {booking.Reference}."
                : claim.Decision ?? "Xem lý do trong hồ sơ.",
            "/shield", ct);

        await db.SaveChangesAsync(ct);
    }
}

internal static class ShieldSideExtensions
{
    /// <summary>Who has to answer a case: the other side from whoever filed it.</summary>
    public static string Opposite(this ShieldSide side) =>
        side == ShieldSide.Guest ? "host" : "guest";
}
