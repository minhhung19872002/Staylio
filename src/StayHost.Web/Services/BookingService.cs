using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;

namespace StayHost.Web.Services;

/// <summary>
/// The booking rules that need the database: the nine availability checks of
/// docs/03 §2 and the clock transitions of docs/03 §3. Kept out of the
/// controllers so the background worker can run the same code.
/// </summary>
public class BookingService(StayHostDbContext db)
{
    /// <summary>Now, in the listing's own time zone (docs/03 §3).</summary>
    public static DateTime LocalNow(Listing listing, DateTime? utcNow = null)
    {
        var utc = utcNow ?? DateTime.UtcNow;
        try
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(listing.TimeZoneId));
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // A bad zone id must not stop a booking; fall back to the platform's own.
            return TimeZoneInfo.ConvertTimeFromUtc(utc, VietnamTime);
        }
    }

    /// <summary>
    /// The instant a stay begins: check-in day at the listing's own check-in
    /// hour, in the listing's own time zone.
    /// </summary>
    public static DateTime CheckInUtc(Listing listing, DateOnly checkIn)
    {
        var local = checkIn.ToDateTime(listing.CheckInFrom, DateTimeKind.Unspecified);
        try
        {
            return TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.FindSystemTimeZoneById(listing.TimeZoneId));
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return TimeZoneInfo.ConvertTimeToUtc(local, VietnamTime);
        }
    }

    private static readonly TimeZoneInfo VietnamTime = ResolveVietnamTime();

    private static TimeZoneInfo ResolveVietnamTime()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { /* try the next spelling */ }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Staylio-ICT", TimeSpan.FromHours(7), "ICT", "ICT");
    }

    /// <summary>
    /// Loads everything the nine checks need and runs them. Returns the first
    /// failure with its own message, exactly as the spec asks.
    /// </summary>
    public async Task<Availability.Result> CheckAsync(
        Listing listing, DateOnly checkIn, DateOnly checkOut, PartySize party, CancellationToken ct,
        int? ignoreBookingId = null, int? roomTypeId = null)
    {
        // A window either side of the stay, wide enough for the turnover check.
        var from = checkIn.AddDays(-Math.Max(1, listing.TurnoverDays));
        var to = checkOut.AddDays(Math.Max(1, listing.TurnoverDays));

        // docs/01 MR-08 — a hotel sells rooms of a kind, so an existing booking
        // only stands in the way when it took the same kind of room and the
        // property has run out of them. Availability is counted, not exclusive.
        if (listing.IsHotel)
            return await CheckHotelAsync(listing, checkIn, checkOut, party, roomTypeId, ignoreBookingId, ct);

        var stays = await db.Bookings
            .Where(b => b.ListingId == listing.Id
                        && b.Id != (ignoreBookingId ?? 0)
                        && BookingLifecycle.BlocksDates.Contains(b.Status)
                        && b.CheckIn < to && from < b.CheckOut)
            .Select(b => new { b.CheckIn, b.CheckOut })
            .ToListAsync(ct);

        var blocks = await db.CalendarBlocks
            .Where(b => b.ListingId == listing.Id && b.From <= to && from <= b.To)
            .Select(b => new { b.From, b.To })
            .ToListAsync(ct);

        var minNights = await db.PriceRules
            .Where(r => r.ListingId == listing.Id && r.MinNights != null
                        && r.From <= checkOut && checkIn <= r.To)
            .Select(r => new { r.From, r.To, r.MinNights })
            .ToListAsync(ct);

        var byDay = new Dictionary<DateOnly, int>();
        foreach (var rule in minNights)
        {
            for (var d = rule.From; d <= rule.To; d = d.AddDays(1))
                byDay[d] = Math.Max(byDay.GetValueOrDefault(d), rule.MinNights ?? 0);
        }

        return Availability.Check(new Availability.Request
        {
            Listing = listing,
            CheckIn = checkIn,
            CheckOut = checkOut,
            Party = party,
            LocalNow = LocalNow(listing),
            Occupied =
            [
                .. stays.Select(s => new Availability.Occupied(s.CheckIn, s.CheckOut, false)),
                .. blocks.Select(b => new Availability.Occupied(b.From, b.To, true))
            ],
            MinNightsByDay = byDay
        });
    }

    /// <summary>
    /// docs/01 MR-08 and MR-09 — the same nine checks, except that "is it
    /// taken" becomes "are all the rooms of this kind taken on some night of
    /// the stay". Everything else about a hotel booking is an ordinary booking.
    /// </summary>
    private async Task<Availability.Result> CheckHotelAsync(
        Listing listing, DateOnly checkIn, DateOnly checkOut, PartySize party,
        int? roomTypeId, int? ignoreBookingId, CancellationToken ct)
    {
        var rooms = await db.RoomTypes.Where(r => r.ListingId == listing.Id).ToListAsync(ct);
        var room = rooms.FirstOrDefault(r => r.Id == roomTypeId);

        var blocks = await db.CalendarBlocks
            .Where(b => b.ListingId == listing.Id && b.From < checkOut && checkIn <= b.To)
            .Select(b => new { b.From, b.To })
            .ToListAsync(ct);

        // Run the ordinary checks first, with no stays: notice, horizon, party
        // size, night count and closed weekdays all still apply to a hotel.
        var basic = Availability.Check(new Availability.Request
        {
            Listing = listing,
            CheckIn = checkIn,
            CheckOut = checkOut,
            Party = party,
            LocalNow = LocalNow(listing),
            Occupied = [.. blocks.Select(b => new Availability.Occupied(b.From, b.To, true))],
            MinNightsByDay = new Dictionary<DateOnly, int>()
        });
        if (!basic.Ok) return basic;

        var taken = await db.Bookings
            .Where(b => b.ListingId == listing.Id
                        && b.RoomTypeId == roomTypeId
                        && b.Id != (ignoreBookingId ?? 0)
                        && BookingLifecycle.BlocksDates.Contains(b.Status)
                        && b.CheckIn < checkOut && checkIn < b.CheckOut)
            .Select(b => new { b.CheckIn, b.CheckOut })
            .ToListAsync(ct);

        var peak = HotelRules.PeakOccupancy(
            checkIn, checkOut, taken.Select(t => (t.CheckIn, t.CheckOut)).ToList());

        var check = HotelRules.CanBook(room, party.Counted, peak);
        return check.Ok
            ? Availability.Result.Pass
            : Availability.Result.Fail(
                check.Reason == HotelRules.Refusal.SoldOut
                    ? Availability.Reason.DatesTaken
                    : Availability.Reason.OverCapacity,
                check.Message);
    }

    /// <summary>
    /// docs/01 TM-05 and TĐ-09: the nightly rate on every date cell, and — when
    /// the guest's dates are gone — the next few runs of free nights.
    /// </summary>
    public async Task<ListingCalendarDto?> CalendarAsync(
        int listingId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, ct);
        if (listing is null) return null;

        // Cap the window so a bad query cannot ask for a decade of nights.
        if (to <= from) to = from.AddMonths(3);
        if (to.DayNumber - from.DayNumber > 400) to = from.AddDays(400);

        var rules = await db.PriceRules
            .Where(r => r.ListingId == listingId && r.From <= to && from <= r.To)
            .ToListAsync(ct);

        var stays = await db.Bookings
            .Where(b => b.ListingId == listingId
                        && BookingLifecycle.BlocksDates.Contains(b.Status)
                        && b.CheckIn < to && from < b.CheckOut)
            .Select(b => new { b.CheckIn, b.CheckOut })
            .ToListAsync(ct);

        var blocks = await db.CalendarBlocks
            .Where(b => b.ListingId == listingId && b.From <= to && from <= b.To)
            .Select(b => new { b.From, b.To })
            .ToListAsync(ct);

        var taken = new HashSet<DateOnly>();
        foreach (var s in stays)
            for (var d = s.CheckIn; d < s.CheckOut; d = d.AddDays(1)) taken.Add(d);
        foreach (var b in blocks)
            for (var d = b.From; d <= b.To; d = d.AddDays(1)) taken.Add(d);

        var today = DateOnly.FromDateTime(LocalNow(listing));

        var nights = new List<CalendarNightDto>();
        for (var d = from; d < to; d = d.AddDays(1))
        {
            var rate = Pricing.RateFor(listing, d, rules);
            var perDayMin = rules
                .Where(r => r.MinNights != null && r.From <= d && d <= r.To)
                .Select(r => r.MinNights!.Value)
                .DefaultIfEmpty(0)
                .Max();

            nights.Add(new CalendarNightDto(
                d, rate.Rate, rate.Source,
                d >= today && !taken.Contains(d),
                perDayMin > 0 ? perDayMin : listing.MinNights));
        }

        return new ListingCalendarDto(listingId, from, to, nights, NextOpenings(nights, listing.MinNights));
    }

    /// <summary>
    /// The three longest runs of consecutive free nights, soonest first. Offered
    /// when the dates the guest picked are already sold (docs/01 TĐ-09).
    /// </summary>
    private static List<OpeningDto> NextOpenings(List<CalendarNightDto> nights, int minNights)
    {
        var openings = new List<OpeningDto>();
        DateOnly? runStart = null;

        foreach (var n in nights)
        {
            if (n.Available)
            {
                runStart ??= n.Date;
                continue;
            }

            if (runStart is { } start) openings.Add(Run(start, n.Date));
            runStart = null;
        }

        if (runStart is { } tail && nights.Count > 0)
            openings.Add(Run(tail, nights[^1].Date.AddDays(1)));

        return openings
            .Where(o => o.Nights >= Math.Max(1, minNights))
            .Take(3)
            .ToList();

        static OpeningDto Run(DateOnly start, DateOnly endExclusive) =>
            new(start, endExclusive, endExclusive.DayNumber - start.DayNumber);
    }

    /// <summary>
    /// The timers and clock transitions of docs/03 §3, run by the background
    /// worker. Returns a short summary of what moved.
    /// </summary>
    public async Task<SweepResult> SweepAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var result = new SweepResult();

        // Holds that ran out: the dates go back on the market.
        var staleHolds = await db.Bookings
            .Include(b => b.Payment)
            .Where(b => b.Status == BookingStatus.PendingPayment && b.HoldExpiresAt != null && b.HoldExpiresAt < now)
            .ToListAsync(ct);

        foreach (var b in staleHolds)
        {
            // docs/07 §2.3 — a transfer gets a longer window than a card, so the
            // line in the history has to say which one ran out.
            var byTransfer = !PaymentMethods.ChargesOnBooking(b.Payment?.Method);

            db.BookingEvents.Add(BookingLifecycle.Transition(
                b, BookingStatus.PaymentFailed, "system",
                byTransfer
                    ? "Hết hạn chờ chuyển khoản mà tiền chưa về."
                    : "Hết 15 phút giữ chỗ mà chưa thanh toán xong."));
            result.HoldsExpired++;
        }

        // Requests the host never answered.
        var staleRequests = await db.Bookings
            .Where(b => b.Status == BookingStatus.PendingHostApproval
                        && b.RequestExpiresAt != null && b.RequestExpiresAt < now)
            .ToListAsync(ct);

        foreach (var b in staleRequests)
        {
            db.BookingEvents.Add(BookingLifecycle.Transition(
                b, BookingStatus.Expired, "system", "Chủ nhà không trả lời trong 24 giờ."));
            result.RequestsExpired++;
        }

        // docs/01 ĐP-17 — private offers nobody acted on inside 24 hours lapse, so
        // a guest is not shown a live "book at this price" for a price the host no
        // longer stands behind.
        var lapsedOffers = await db.SpecialOffers
            .Where(o => o.Status == SpecialOfferStatus.Pending && o.ExpiresAt < now)
            .ToListAsync(ct);
        foreach (var o in lapsedOffers)
        {
            o.Status = SpecialOfferStatus.Expired;
            o.RespondedAt = now;
        }

        // docs/01 TC-09 — a hold or request that lapsed hands its promo code back
        // to the campaign, so a limited run is not eaten by stays that fell
        // through. Marked, not deleted: the redemption row is history.
        var endedIds = staleHolds.Select(b => b.Id).Concat(staleRequests.Select(b => b.Id)).ToList();
        if (endedIds.Count > 0)
        {
            var toVoid = await db.CouponRedemptions
                .Where(r => endedIds.Contains(r.BookingId) && !r.Voided)
                .ToListAsync(ct);
            foreach (var r in toVoid) r.Voided = true;
        }

        // Check-in and check-out roll over in the listing's own time zone, so
        // these two need the listing rather than a single server-side date.
        var movable = await db.Bookings
            .Include(b => b.Listing)
            .Where(b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.InProgress)
            .ToListAsync(ct);

        foreach (var b in movable)
        {
            var localToday = DateOnly.FromDateTime(LocalNow(b.Listing!, now));

            if (b.Status == BookingStatus.Confirmed && localToday >= b.CheckIn)
            {
                db.BookingEvents.Add(BookingLifecycle.Transition(
                    b, BookingStatus.InProgress, "system", "Đã tới ngày nhận phòng."));
                result.StartedStays++;
            }

            if (b.Status == BookingStatus.InProgress && localToday >= b.CheckOut)
            {
                db.BookingEvents.Add(BookingLifecycle.Transition(
                    b, BookingStatus.Completed, "system", "Đã tới ngày trả phòng."));
                result.CompletedStays++;

                if (b.Payment is not null || await db.Payments.AnyAsync(p => p.BookingId == b.Id, ct))
                    result.PayoutsDue++;
            }
        }

        if (result.Any) await db.SaveChangesAsync(ct);
        return result;
    }

    public sealed class SweepResult
    {
        public int HoldsExpired { get; set; }
        public int RequestsExpired { get; set; }
        public int StartedStays { get; set; }
        public int CompletedStays { get; set; }
        public int PayoutsDue { get; set; }

        public bool Any => HoldsExpired + RequestsExpired + StartedStays + CompletedStays > 0;

        public override string ToString() =>
            $"{HoldsExpired} giữ chỗ hết hạn, {RequestsExpired} yêu cầu hết hạn, " +
            $"{StartedStays} bắt đầu lưu trú, {CompletedStays} hoàn tất";
    }
}

/// <summary>
/// Runs the sweep every minute. The two timers of docs/03 §2–§3 are minutes and
/// hours, so this is fine-grained enough without polling the database hard.
/// </summary>
public class BookingLifecycleWorker(IServiceProvider services, ILogger<BookingLifecycleWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = services.CreateAsyncScope();

                // docs/07 §5 — asks the gateway what really happened to guests who
                // never came back from their bank's page. It runs before the
                // lifecycle sweep because that one expires unpaid holds, and a
                // booking that was paid must be recognised before it is failed.
                var cardAuth = scope.ServiceProvider.GetRequiredService<CardAuthSweeper>();
                var authResult = await cardAuth.SweepAsync(stoppingToken);
                if (authResult.Any) log.LogInformation("Xác thực thẻ: {Result}.", authResult);

                // docs/07 §5, §13 — the same question asked of the real gateways.
                // It runs here, ahead of the lifecycle sweep, for the same reason:
                // a booking somebody paid for on VNPay must be recognised before
                // its hold is expired out from under it.
                var psp = scope.ServiceProvider.GetRequiredService<Gateways.PspSweeper>();
                var pspResult = await psp.SweepAsync(stoppingToken);
                if (pspResult.Any) log.LogInformation("Cổng thanh toán: {Result}.", pspResult);

                var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
                var result = await bookings.SweepAsync(stoppingToken);
                if (result.Any) log.LogInformation("Vòng đời đơn: {Result}.", result);

                // docs/03 §7 — the same tick publishes reviews whose 14-day
                // window has closed and sends the day 1 / 7 / 13 reminders.
                var reviews = scope.ServiceProvider.GetRequiredService<ReviewService>();
                var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
                var reviewResult = await reviews.SweepAsync(notifications, stoppingToken);
                if (reviewResult.Any) log.LogInformation("Đánh giá: {Result}.", reviewResult);

                // docs/01 ĐP-06 — the second half of part-paid bookings.
                var balances = scope.ServiceProvider.GetRequiredService<BalanceCollector>();
                var balanceResult = await balances.SweepAsync(stoppingToken);
                if (balanceResult.Any) log.LogInformation("Trả một phần: {Result}.", balanceResult);

                // docs/01 ĐP-07 — splits nobody finished paying.
                var splits = scope.ServiceProvider.GetRequiredService<SplitBillService>();
                var unwound = await splits.SweepAsync(stoppingToken);
                if (unwound > 0) log.LogInformation("Đã hoàn {Count} lượt chia hoá đơn hết hạn.", unwound);

                // docs/01 MR-04 — sessions that never reached their minimum.
                var experiences = scope.ServiceProvider.GetRequiredService<ExperienceService>();
                // docs/09 §2.7 — seats held for a checkout nobody finished.
                var lapsedHolds = await experiences.ReleaseExpiredHoldsAsync(stoppingToken);
                if (lapsedHolds > 0) log.LogInformation("Giữ chỗ trải nghiệm hết hạn: {Count}.", lapsedHolds);

                var calledOff = await experiences.SweepAsync(stoppingToken);
                if (calledOff > 0) log.LogInformation("Đã huỷ {Count} suất trải nghiệm thiếu người.", calledOff);

                // docs/07 §2.3 — tickets and jobs whose bank transfer never
                // arrived. The stay half is done by the lifecycle sweep above,
                // which already expires a hold that has run out.
                var lapsedTickets = await experiences.ExpireAwaitingTransfersAsync(stoppingToken);
                if (lapsedTickets > 0)
                    log.LogInformation("Vé trải nghiệm hết hạn chờ chuyển khoản: {Count}.", lapsedTickets);

                // docs/07 §12 — sends hosts their money, or says why not.
                var payouts = scope.ServiceProvider.GetRequiredService<PayoutService>();
                var payoutResult = await payouts.SweepAsync(stoppingToken);
                if (payoutResult.Any) log.LogInformation("Chuyển tiền: {Result}.", payoutResult);

                // docs/01 TC-03 — the monthly instalments for long stays.
                var instResult = await payouts.InstallmentSweepAsync(stoppingToken);
                if (instResult.Any) log.LogInformation("Trả theo tháng: {Result}.", instResult);

                // docs/09 §4 — experience/service providers, a day after each session ends.
                var sessionPayouts = await payouts.SweepSessionsAsync(stoppingToken);
                if (sessionPayouts.Any) log.LogInformation("Trả buổi: {Result}.", sessionPayouts);

                // docs/02 G8, docs/07 §19 — the shares carved off just above, in
                // transfers of their own. It runs after the payout sweep in the
                // same tick because that is what created the rows it groups.
                var coHostPayouts = scope.ServiceProvider.GetRequiredService<CoHostPayoutService>();
                var sharedOut = await coHostPayouts.SweepAsync(stoppingToken);
                if (sharedOut.Any) log.LogInformation("Chia đồng quản lý: {Result}.", sharedOut);

                // docs/07 §19.2 — a proposal nobody answered inside 14 ngày lapses.
                var lapsedTerms = await coHostPayouts.ExpireProposalsAsync(stoppingToken);
                if (lapsedTerms > 0) log.LogInformation("Đề nghị chia thu nhập hết hạn: {Count}.", lapsedTerms);

                // docs/07 §19.4 — stays whose earnings shrank after their shares
                // were decided. A sweep, not a hook: a refund reaches a booking
                // from seven directions and patching each is how one is missed.
                var redivided = await coHostPayouts.ReconcileSweepAsync(stoppingToken);
                if (redivided > 0) log.LogInformation("Chia lại sau hoàn tiền: {Count} đơn.", redivided);

                // docs/09 §3.2 — a lapsed practising certificate hides its listing.
                var certs = scope.ServiceProvider.GetRequiredService<ServiceMarketService>();
                var certResult = await certs.CertificateSweepAsync(stoppingToken);
                if (certResult.Hidden + certResult.Reminded > 0)
                    log.LogInformation("Chứng chỉ dịch vụ: {Hidden} tạm ẩn, {Reminded} nhắc.",
                        certResult.Hidden, certResult.Reminded);

                // docs/09 §3.5 — requests the provider never answered.
                var unanswered = await certs.ExpireRequestsAsync(stoppingToken);
                if (unanswered > 0)
                    log.LogInformation("Yêu cầu dịch vụ quá hạn xác nhận: {Count}.", unanswered);

                var lapsedJobs = await certs.ExpireAwaitingTransfersAsync(stoppingToken);
                if (lapsedJobs > 0)
                    log.LogInformation("Đơn dịch vụ hết hạn chờ chuyển khoản: {Count}.", lapsedJobs);

                // docs/07 §4 — a card about to expire with money still to come
                // off it, fourteen days ahead.
                var expiring = await Controllers.PaymentMethodsController.RemindExpiringAsync(
                    scope.ServiceProvider.GetRequiredService<StayHostDbContext>(), notifications, stoppingToken);
                if (expiring > 0) log.LogInformation("Đã nhắc {Count} thẻ sắp hết hạn.", expiring);

                // docs/03 §8 — grants and revokes the two titles. Cheap on every
                // other tick: rows already decided for this quarter or this week
                // are not even fetched.
                var badges = scope.ServiceProvider.GetRequiredService<BadgeService>();
                var badgeResult = await badges.SweepAsync(stoppingToken);
                if (badgeResult.HostsReviewed + badgeResult.ListingsReviewed > 0)
                    log.LogInformation("Danh hiệu: {Result}.", badgeResult);

                // Referrals pay out once the newcomer has actually travelled.
                var wallet = scope.ServiceProvider.GetRequiredService<WalletService>();
                var rewarded = await wallet.RewardCompletedStaysAsync(stoppingToken);
                if (rewarded > 0) log.LogInformation("Đã thưởng {Count} lượt giới thiệu.", rewarded);

                // docs/01 TC-07 — retires balance that has reached its expiry.
                // Returns immediately while no kind of grant expires, which is
                // the shipped default until the customer picks a lifetime.
                var lapsed = await wallet.ExpireLapsedCreditAsync(stoppingToken);
                if (lapsed > 0) log.LogInformation("Số dư hết hạn: {Count} khoản.", lapsed);

                // docs/06 §6 — cases nobody answered inside 24 hours, and the
                // monthly top-up of the Staylio Shield fund.
                var shield = scope.ServiceProvider.GetRequiredService<ShieldService>();
                var shieldMoved = await shield.SweepAsync(stoppingToken);
                if (shieldMoved > 0) log.LogInformation("Staylio Shield: {Count} thay đổi.", shieldMoved);

                // docs/01 TN-09 — milestone lines in the conversation itself.
                var messenger = scope.ServiceProvider.GetRequiredService<ThreadMessenger>();
                var posted = await messenger.SweepAsync(stoppingToken);
                if (posted > 0) log.LogInformation("Đã gửi {Count} tin nhắn tự động.", posted);

                // docs/01 TM-23 — new places for anyone watching a saved search.
                var savedSearch = scope.ServiceProvider.GetRequiredService<SavedSearchSweeper>();
                await savedSearch.SweepAsync(stoppingToken);

                // docs/01 YT-08 — the other half: a saved place running out of nights.
                var scarcity = scope.ServiceProvider.GetRequiredService<ScarcitySweeper>();
                await scarcity.SweepAsync(stoppingToken);

                // docs/03 §11 — 7 days / 24 hours before arrival, and check-out morning.
                var reminders = scope.ServiceProvider.GetRequiredService<StayReminderSweeper>();
                await reminders.SweepAsync(stoppingToken);

                // docs/08 §5.2–§5.3 — sanctions whose time is up end by themselves.
                var sanctions = scope.ServiceProvider.GetRequiredService<SanctionExpiry>();
                await sanctions.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad sweep must not take the worker down for the rest of the process.
                log.LogError(ex, "Không chạy được vòng quét vòng đời đơn.");
            }
        }
    }
}
