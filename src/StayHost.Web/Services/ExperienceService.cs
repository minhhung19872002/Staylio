using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;

namespace StayHost.Web.Services;

/// <summary>
/// docs/01 MR-01 → MR-04. Everything a session does: quoting a seat, selling
/// one, giving the money back, and calling a session off when too few people
/// came forward.
/// </summary>
public class ExperienceService(
    StayHostDbContext db, CatalogService catalog, NotificationService notifications,
    PaymentGateway gateway, Gateways.PspRouter router, BankTransferSettings bank,
    RefundGateway refunds, ILogger<ExperienceService> log)
{
    /* ------------------------------------------------------------ reading */

    public async Task<ExperienceDetailDto?> DetailAsync(string idOrSlug, CancellationToken ct)
    {
        var query = db.Experiences
            .Include(x => x.Host)
            .Include(x => x.Images)
            .Include(x => x.Slots)
            .Include(x => x.Itinerary)
            .AsSplitQuery();

        var experience = int.TryParse(idOrSlug, out var id)
            ? await query.FirstOrDefaultAsync(x => x.Id == id, ct)
            : await query.FirstOrDefaultAsync(x => x.Slug == idOrSlug, ct);

        return experience is null ? null : ToDetail(experience, HostView: false);
    }

    public async Task<IReadOnlyList<ExperienceDetailDto>> MineAsync(int userId, CancellationToken ct)
    {
        var profile = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == userId, ct);
        if (profile is null) return [];

        var mine = await db.Experiences
            .Where(x => x.HostId == profile.Id)
            .Include(x => x.Host).Include(x => x.Images).Include(x => x.Slots).Include(x => x.Itinerary)
            .AsSplitQuery()
            .ToListAsync(ct);

        return mine.Select(x => ToDetail(x, HostView: true)).ToList();
    }

    /// <summary>
    /// docs/09 §2.9 — a guest is shown sessions they could still sit in, so a
    /// finished one drops off almost at once. A host takes the register after the
    /// session, not always the same hour, so their own list keeps a fortnight of
    /// finished sessions reachable.
    /// </summary>
    private static ExperienceDetailDto ToDetail(Experience x, bool HostView)
    {
        var now = DateTime.UtcNow;
        var since = HostView
            ? now.AddDays(-14)
            : now.AddHours(-x.DurationMinutes / 60.0 - 1);

        return new ExperienceDetailDto(
            x.Id, x.Slug, x.Title, x.City, x.Country, x.Summary, x.Description,
            x.DurationMinutes, x.MaxGroup, x.MinGuests, x.LanguageList, x.MinAge,
            x.MeetingPoint, x.Latitude, x.Longitude, x.IncludedList,
            x.PricePerPerson, x.PrivateGroupPrice, x.IsPublished,
            x.Rating, x.ReviewCount, x.Host?.Name ?? "", x.Host?.Initials ?? "",
            x.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).ToList(),
            x.Slots
                .Where(s => s.StartsAt > since)
                .OrderBy(s => s.StartsAt)
                .Select(s => new ExperienceSlotDto(
                    s.Id, s.StartsAt, s.Capacity, s.SeatsTaken, s.SeatsLeft,
                    s.IsPrivate, s.Status.ToString(), s.CancelReason))
                .ToList(),
            x.Category, x.AllowsChildren,
            x.LicenceName, x.LicenceExpiresOn,
            x.InsurancePolicy, x.InsuranceExpiresOn,
            x.SafetyPlan, x.EmergencyPhone,
            x.ModerationStatus.ToString(), x.ReviewerNote, x.SubmittedForReviewAt,
            x.Itinerary.OrderBy(i => i.SortOrder)
                .Select(i => new ExperienceStepDto(i.Title, i.Description, i.ImageUrl))
                .ToList());
    }

    /* ------------------------------------------- docs/09 §4 (MR-C-02) */

    /// <summary>
    /// docs/09 §4 — the cross-sell from a booked stay: experiences in that city
    /// with a session the guest could actually sit in, which means one inside the
    /// nights they are there and with a seat still going.
    ///
    /// Both gates of docs/09 §2.2 are asked for rather than <see cref="Experience.IsPublished"/>
    /// alone: a suggestion is the platform putting an activity in front of someone
    /// who did not go looking for it, so an experience that a person never approved
    /// must not travel that way even if a flag were left on by some other path.
    /// A private session is skipped — it belongs to whoever took it.
    /// </summary>
    public async Task<IReadOnlyList<ExperienceCardDto>> SuggestForStayAsync(
        string city, DateTime from, DateTime to, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(city) || to <= from) return [];

        // A session that has already begun is no use to anybody, so the window
        // starts at whichever comes later: the check-in or right now.
        var opens = from > DateTime.UtcNow ? from : DateTime.UtcNow;
        if (to <= opens) return [];

        return await db.Experiences
            .Where(x => x.IsPublished
                        && x.ModerationStatus == ExperienceModeration.Approved
                        && x.City == city
                        && x.Slots.Any(s => s.Status == SlotStatus.Open
                                            && !s.IsPrivate
                                            && s.SeatsTaken < s.Capacity
                                            && s.StartsAt >= opens
                                            && s.StartsAt < to))
            .OrderByDescending(x => x.Rating).ThenBy(x => x.Id)
            .Take(take)
            .Select(x => new ExperienceCardDto(
                x.Id, x.Slug, x.Title, x.City, x.Summary,
                x.DurationMinutes, x.MaxGroup, x.PricePerPerson,
                x.Rating, x.ReviewCount,
                x.Host!.Name,
                x.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).ToList(),
                // "Còn 3 suất" here means three during their stay, not three ever —
                // the count answers the question the card is being shown for.
                x.Slots.Count(s => s.Status == SlotStatus.Open
                                   && !s.IsPrivate
                                   && s.SeatsTaken < s.Capacity
                                   && s.StartsAt >= opens
                                   && s.StartsAt < to)))
            .ToListAsync(ct);
    }

    /* ------------------------------------------------------------ pricing */

    public async Task<ExperienceQuoteDto?> QuoteAsync(int slotId, int seats, bool wantsPrivate, CancellationToken ct)
    {
        var slot = await db.ExperienceSlots
            .Include(s => s.Experience)
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot?.Experience is null) return null;

        var check = ExperienceRules.CanBook(slot.Experience, slot, seats, wantsPrivate, DateTime.UtcNow);
        var price = Pricing.QuoteExperience(new Pricing.ExperienceRequest
        {
            Experience = slot.Experience,
            Seats = seats,
            Private = wantsPrivate,
            StartsAt = slot.StartsAt,
            TaxRules = await catalog.ActiveTaxRulesAsync(ct)
        });

        return new ExperienceQuoteDto(
            slot.Id, slot.StartsAt, price.Seats, wantsPrivate,
            price.PerSeat, price.Subtotal, price.GuestServiceFee, price.Tax, price.Total,
            price.Lines.Select(l => new PriceLineDto(l.Key, l.Label, l.Amount)).ToList(),
            check.Ok, check.Ok ? null : check.Message);
    }

    /* ------------------------------------------------------------ booking */

    /* ------------------------------------------------- MR-E-06, the ten minutes */

    /// <summary>
    /// docs/09 §2.7 — takes the seats off the session while the guest is paying,
    /// for ten minutes. Uses the same conditional UPDATE the booking path does, so
    /// two guests reaching for the last seats still cannot both succeed; the
    /// difference is only that these seats are given back on a timer as well as
    /// on a refused card.
    /// </summary>
    public async Task<(ExperienceHold? Hold, string? Error)> HoldAsync(
        User user, int slotId, int seats, bool wantsPrivate, CancellationToken ct)
    {
        var slot = await db.ExperienceSlots
            .Include(s => s.Experience)
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot?.Experience is null) return (null, "Không tìm thấy suất này.");
        if (!slot.Experience.IsPublished || slot.Experience.ModerationStatus != ExperienceModeration.Approved)
            return (null, "Trải nghiệm này hiện không nhận đặt.");

        seats = Math.Max(1, seats);

        // One hold per guest per session: opening checkout again (a reload, a
        // changed seat count) hands the earlier seats back first instead of
        // stacking a second claim on top.
        var earlier = await db.ExperienceHolds
            .Where(h => h.UserId == user.Id && h.SlotId == slotId)
            .ToListAsync(ct);
        foreach (var h in earlier)
        {
            if (h.IsLive(DateTime.UtcNow)) await ReleaseSeatsAsync(slotId, h.Seats, h.IsPrivate, ct);
            db.ExperienceHolds.Remove(h);
        }
        if (earlier.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            await db.Entry(slot).ReloadAsync(ct);
        }

        var check = ExperienceRules.CanBook(slot.Experience, slot, seats, wantsPrivate, DateTime.UtcNow);
        if (!check.Ok) return (null, check.Message);

        var (claimed, left) = await ClaimSeatsAsync(slotId, seats, wantsPrivate, ct);
        if (!claimed)
            return (null, left > 0
                ? $"Vừa có người đặt trước bạn — chỉ còn {left} chỗ cho suất này."
                : "Vừa có người đặt hết chỗ của suất này.");

        var hold = new ExperienceHold
        {
            SlotId = slotId,
            UserId = user.Id,
            Seats = seats,
            IsPrivate = wantsPrivate,
            ExpiresAt = DateTime.UtcNow + ExperienceRules.HoldWindow
        };
        db.ExperienceHolds.Add(hold);
        await db.SaveChangesAsync(ct);

        return (hold, null);
    }

    /// <summary>
    /// docs/09 §2.7 — hands the seats back when the guest walks away from
    /// checkout. Runs on the lifecycle tick alongside the other sweeps.
    /// </summary>
    public async Task<int> ReleaseExpiredHoldsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var stale = await db.ExperienceHolds
            .Where(h => h.ClaimedAt == null && h.ExpiresAt <= now)
            .Take(200)
            .ToListAsync(ct);
        if (stale.Count == 0) return 0;

        foreach (var hold in stale)
        {
            await ReleaseSeatsAsync(hold.SlotId, hold.Seats, hold.IsPrivate, ct);
            db.ExperienceHolds.Remove(hold);
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Trả lại {Count} lượt giữ chỗ trải nghiệm đã hết hạn.", stale.Count);
        return stale.Count;
    }

    /// <summary>
    /// The one place seats are taken. The WHERE carries the arithmetic, so the
    /// database decides who wins a race rather than the application guessing.
    /// </summary>
    private async Task<(bool Claimed, int Left)> ClaimSeatsAsync(
        int slotId, int seats, bool wantsPrivate, CancellationToken ct)
    {
        var rows = wantsPrivate
            ? await db.ExperienceSlots
                .Where(s => s.Id == slotId && !s.IsPrivate && s.SeatsTaken == 0)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.SeatsTaken, s => s.Capacity)
                    .SetProperty(s => s.IsPrivate, true), ct)
            : await db.ExperienceSlots
                .Where(s => s.Id == slotId && !s.IsPrivate && s.SeatsTaken + seats <= s.Capacity)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.SeatsTaken, s => s.SeatsTaken + seats), ct);

        if (rows > 0) return (true, 0);

        var left = await db.ExperienceSlots.AsNoTracking()
            .Where(s => s.Id == slotId)
            .Select(s => s.Capacity - s.SeatsTaken)
            .FirstOrDefaultAsync(ct);

        return (false, left);
    }

    private async Task ReleaseSeatsAsync(int slotId, int seats, bool wasPrivate, CancellationToken ct)
    {
        if (wasPrivate)
            await db.ExperienceSlots.Where(s => s.Id == slotId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.SeatsTaken, 0)
                    .SetProperty(s => s.IsPrivate, false), ct);
        else
            await db.ExperienceSlots.Where(s => s.Id == slotId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.SeatsTaken, s => s.SeatsTaken - seats), ct);
    }

    public async Task<(ExperienceBooking? Booking, string? Error)> BookAsync(
        User user, int slotId, BookExperienceRequest req, CancellationToken ct)
    {
        var slot = await db.ExperienceSlots
            .Include(s => s.Experience)
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot?.Experience is null) return (null, "Không tìm thấy suất này.");

        // docs/09 §2.2 — on sale only once approved and published. A slot id is
        // enough to reach this, so a rejected or withdrawn experience still sold.
        if (!slot.Experience.IsPublished || slot.Experience.ModerationStatus != ExperienceModeration.Approved)
            return (null, "Trải nghiệm này hiện không nhận đặt.");

        var seats = Math.Max(1, req.Seats);

        // docs/09 §2.7 (MR-E-06) — a guest paying against their own hold already
        // has these seats off the count. Checking as if they were somebody else's
        // would refuse the very booking the hold exists to protect, so the held
        // seats are put back for the check only.
        var hold = req.HoldId is { } holdId
            ? await db.ExperienceHolds.FirstOrDefaultAsync(
                h => h.Id == holdId && h.UserId == user.Id && h.SlotId == slotId, ct)
            : null;

        var heldAlready = hold is not null && hold.IsLive(DateTime.UtcNow) && hold.Seats == seats;

        var asIfFree = heldAlready
            ? new ExperienceSlot
            {
                Id = slot.Id, ExperienceId = slot.ExperienceId, StartsAt = slot.StartsAt,
                Capacity = slot.Capacity, Status = slot.Status,
                SeatsTaken = slot.SeatsTaken - (hold!.IsPrivate ? slot.Capacity : hold.Seats),
                IsPrivate = hold.IsPrivate ? false : slot.IsPrivate
            }
            : slot;

        var check = ExperienceRules.CanBook(slot.Experience, asIfFree, seats, req.Private, DateTime.UtcNow);
        if (!check.Ok) return (null, check.Message);

        // Decided before any seat is claimed, so a method nobody can pay with
        // never holds one.
        var (road, refusal) = ProductCheckout.Choose(router, bank, req.PaymentMethod);
        if (refusal is not null) return (null, refusal);
        var method = ProductCheckout.Normalise(req.PaymentMethod);

        var price = Pricing.QuoteExperience(new Pricing.ExperienceRequest
        {
            Experience = slot.Experience,
            Seats = seats,
            Private = req.Private,
            StartsAt = slot.StartsAt,
            TaxRules = await catalog.ActiveTaxRulesAsync(ct)
        });

        // docs/09 §2.7 (scenario 2, MR-E-06) — the seats leave the count BEFORE the
        // card is charged, so two guests going for the last two both pass CanBook
        // above but only one claim succeeds. A guest who came through a hold has
        // already taken theirs; claiming again would charge them for seats twice
        // over. Either way a refused card gives them straight back.
        var taken = req.Private ? slot.Capacity : seats;

        if (!heldAlready)
        {
            // A hold that lapsed between checkout and payment is gone; the guest
            // has to win the seats again like anybody else.
            if (hold is not null) db.ExperienceHolds.Remove(hold);

            var (claimed, left) = await ClaimSeatsAsync(slotId, seats, req.Private, ct);
            if (!claimed)
                return (null, left > 0
                    ? $"Vừa có người đặt trước bạn — chỉ còn {left} chỗ cho suất này."
                    : "Vừa có người đặt hết chỗ của suất này.");
        }

        // docs/07 §2.3 — a bank transfer is not charged here. The ticket is
        // written down holding its seats and stays unconfirmed until the money is
        // found on a statement; the seats come back on the timer if it never is.
        //
        // docs/07 §13 — a method a licensed gateway serves waits the same way:
        // the guest leaves for the gateway's page and the ticket is confirmed by
        // ConfirmPaidAsync once the gateway says the money moved.
        var byTransfer = road == ProductCheckout.Road.Transfer;
        var waits = road != ProductCheckout.Road.StandIn;

        if (!waits)
        {
            var attempt = gateway.Charge(price.Total, method, req.CardLast4);

            if (!attempt.Ok)
            {
                // Give the seats straight back; a refused card must not hold a seat.
                await ReleaseSeatsAsync(slotId, taken, req.Private, ct);
                if (heldAlready && hold is not null) db.ExperienceHolds.Remove(hold);
                await db.SaveChangesAsync(ct);

                return (null, attempt.Reason);
            }
        }

        // The hold has done its job; the booking now owns the seats.
        if (heldAlready && hold is not null) db.ExperienceHolds.Remove(hold);

        var booking = new ExperienceBooking
        {
            Reference = $"XP{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            SlotId = slot.Id,
            GuestUserId = user.Id,
            Seats = seats,
            IsPrivate = req.Private,
            Subtotal = price.Subtotal,
            ServiceFee = price.GuestServiceFee,
            Tax = price.Tax,
            Total = price.Total,
            HostServiceFee = price.HostServiceFee,
            HostPayout = price.HostPayout,
            Status = waits ? ExperienceBookingStatus.AwaitingPayment : ExperienceBookingStatus.Confirmed
        };

        db.ExperienceBookings.Add(booking);
        await db.SaveChangesAsync(ct);

        // Nothing else happens yet: no capture, no word to the host. Both are
        // done the moment the money is found — by BankTransferService for a
        // transfer, by PspCheckout for a gateway.
        if (waits) return (booking, null);

        db.LedgerEntries.AddRange(Ledger.CaptureExperience(booking, DateTime.UtcNow));
        await db.SaveChangesAsync(ct);

        await notifications.QueueWithEmailAsync(
            user, NotificationKind.BookingConfirmed,
            "Đã đặt trải nghiệm",
            $"{slot.Experience.Title} · {seats} chỗ · mã {booking.Reference}.",
            "/experiences", ct);
        await db.SaveChangesAsync(ct);

        return (booking, null);
    }

    /// <summary>
    /// docs/07 §2.3 — the money for a transfer-booked ticket has been found on a
    /// statement. This is the rest of BookAsync, run late.
    /// </summary>
    /// <summary>
    /// docs/07 §13 — a gateway says the money for this ticket moved. False when
    /// the ticket stopped waiting for it (it lapsed, or another visit paid it),
    /// which tells the caller to send the money back.
    /// </summary>
    public async Task<bool> ConfirmPaidAsync(int bookingId, CancellationToken ct)
    {
        var booking = await db.ExperienceBookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null || booking.Status != ExperienceBookingStatus.AwaitingPayment) return false;

        await ConfirmTransferAsync(booking, ct, "Đã đặt trải nghiệm");
        return true;
    }

    public async Task ConfirmTransferAsync(
        ExperienceBooking booking, CancellationToken ct, string title = "Đã nhận được chuyển khoản")
    {
        if (booking.Status != ExperienceBookingStatus.AwaitingPayment) return;

        booking.Status = ExperienceBookingStatus.Confirmed;
        db.LedgerEntries.AddRange(Ledger.CaptureExperience(booking, DateTime.UtcNow));
        await db.SaveChangesAsync(ct);

        var slot = await db.ExperienceSlots
            .Include(s => s.Experience)
            .FirstOrDefaultAsync(s => s.Id == booking.SlotId, ct);
        var guest = await db.Users.FirstOrDefaultAsync(u => u.Id == booking.GuestUserId, ct);

        await notifications.QueueWithEmailAsync(
            guest, NotificationKind.BookingConfirmed,
            title,
            $"{slot?.Experience?.Title} · {booking.Seats} chỗ · mã {booking.Reference}.",
            "/experiences", ct);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Experience {Reference} confirmed after payment.", booking.Reference);
    }

    /// <summary>
    /// docs/07 §2.3 — tickets whose transfer window ran out. The seats go back to
    /// the session, the same way a lapsed hold gives them back. Nothing was
    /// captured, so there is nothing to refund.
    /// </summary>
    public async Task<int> ExpireAwaitingTransfersAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - BankTransfers.Window;

        // A gateway visit is over well before a transfer window is: the seats
        // come back once the gateway's own window has passed twice over. Money
        // that still lands afterwards is returned by PspCheckout.
        var gatewayCutoff = DateTime.UtcNow - PaymentSession.Window * 2;

        var stale = await db.ExperienceBookings
            .Where(b => b.Status == ExperienceBookingStatus.AwaitingPayment
                        && (b.CreatedAt <= cutoff
                            || (b.CreatedAt <= gatewayCutoff
                                && db.PaymentSessions.Any(s => s.ExperienceBookingId == b.Id)
                                && !db.PaymentSessions.Any(s => s.ExperienceBookingId == b.Id
                                                                && s.Status == PaymentSessionStatus.Paid))))
            .Take(200)
            .ToListAsync(ct);
        if (stale.Count == 0) return 0;

        foreach (var booking in stale)
        {
            await ReleaseSeatsAsync(booking.SlotId, booking.Seats, booking.IsPrivate, ct);
            booking.Status = ExperienceBookingStatus.PaymentExpired;
            booking.CancelReason = "Hết hạn chờ thanh toán.";
            booking.CancelledAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Huỷ {Count} vé trải nghiệm hết hạn chờ chuyển khoản.", stale.Count);
        return stale.Count;
    }

    public async Task<string?> CancelAsync(int userId, int bookingId, CancellationToken ct)
    {
        var booking = await db.ExperienceBookings
            .Include(b => b.Slot!).ThenInclude(s => s.Experience)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.GuestUserId == userId, ct);
        if (booking is null) return "Không tìm thấy vé này.";

        // docs/07 §2.3 — walking away before transferring. No money moved, so the
        // seats go back and no refund is computed over an amount never paid.
        if (booking.Status == ExperienceBookingStatus.AwaitingPayment)
        {
            await ReleaseSeatsAsync(booking.SlotId, booking.Seats, booking.IsPrivate, ct);
            booking.Status = ExperienceBookingStatus.PaymentExpired;
            booking.CancelReason = "Khách không chuyển khoản nữa.";
            booking.CancelledAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return null;
        }
        if (booking.Status != ExperienceBookingStatus.Confirmed) return "Vé này không còn hiệu lực.";

        var refund = ExperienceRules.GuestRefund(booking, booking.Slot!.StartsAt, DateTime.UtcNow);

        await ReleaseAsync(booking, refund, ExperienceBookingStatus.CancelledByGuest,
            refund > 0 ? "Khách huỷ trước 24 giờ." : "Khách huỷ sát giờ, không hoàn tiền.", ct);

        return null;
    }

    /// <summary>Frees the seats, posts the refund, and marks the ticket.</summary>
    private async Task ReleaseAsync(
        ExperienceBooking booking, decimal refund, ExperienceBookingStatus status, string reason,
        CancellationToken ct)
    {
        var slot = booking.Slot!;
        slot.SeatsTaken = Math.Max(0, slot.SeatsTaken - (booking.IsPrivate ? slot.Capacity : booking.Seats));
        if (booking.IsPrivate) slot.IsPrivate = false;

        booking.Status = status;
        booking.RefundedAmount = refund;
        booking.CancelReason = reason;
        booking.CancelledAt = DateTime.UtcNow;

        db.LedgerEntries.AddRange(Ledger.RefundExperience(booking, refund, DateTime.UtcNow));
        await db.SaveChangesAsync(ct);

        // docs/07 §10 — the refund goes back the way the ticket was paid. The
        // books used to say it had, with no call to any gateway.
        var sent = await refunds.SendForAsync(
            s => s.ExperienceBookingId == booking.Id, refund, booking.Reference, "card", null, "system", ct);

        if (sent.Bounced > 0)
        {
            db.LedgerEntries.AddRange(Ledger.RefundKeptAsCredit(
                booking.Id, null, booking.Reference, sent.Bounced, DateTime.UtcNow));
            db.CreditEntries.Add(CreditLedger.Grant(
                booking.GuestUserId, sent.Bounced, CreditReason.Returned,
                $"Hoàn vé {booking.Reference} — thẻ không nhận được", DateTime.UtcNow));
            await db.SaveChangesAsync(ct);
        }
    }

    /* --------------------------------------------------------- the host */

    public async Task<(int? Id, string? Error)> SaveAsync(User user, SaveExperienceRequest req, CancellationToken ct)
    {
        var profile = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == user.Id, ct);
        if (profile is null) return (null, "Bạn cần có hồ sơ chủ nhà trước.");

        var experience = req.Id is { } id
            ? await db.Experiences.Include(x => x.Images).Include(x => x.Itinerary)
                .FirstOrDefaultAsync(x => x.Id == id && x.HostId == profile.Id, ct)
            : new Experience { HostId = profile.Id };
        if (experience is null) return (null, "Không tìm thấy trải nghiệm này.");

        var title = (req.Title ?? "").Trim();
        if (title.Length < 4) return (null, "Tên trải nghiệm quá ngắn.");
        if (req.PricePerPerson <= 0) return (null, "Giá mỗi người phải lớn hơn 0.");
        if (req.MaxGroup < 1) return (null, "Nhóm tối đa phải từ 1 người.");
        if (req.MinGuests < 1 || req.MinGuests > req.MaxGroup)
            return (null, "Số người tối thiểu phải nằm trong nhóm tối đa.");

        experience.Title = title;
        experience.City = (req.City ?? "").Trim();
        experience.Summary = (req.Summary ?? "").Trim();
        experience.Description = (req.Description ?? "").Trim();
        experience.DurationMinutes = Math.Clamp(req.DurationMinutes, 30, 60 * 24);
        experience.MaxGroup = req.MaxGroup;
        experience.MinGuests = req.MinGuests;
        experience.Languages = string.Join(',', req.Languages ?? ["vi"]);
        experience.MinAge = Math.Clamp(req.MinAge, 0, 21);
        experience.MeetingPoint = (req.MeetingPoint ?? "").Trim();
        experience.Latitude = req.Latitude;
        experience.Longitude = req.Longitude;
        experience.Included = string.Join('\n', req.Included ?? []);
        experience.PricePerPerson = req.PricePerPerson;
        experience.PrivateGroupPrice = req.PrivateGroupPrice;

        // docs/09 §2.1–§2.3 — what the activity is decides what it must prove.
        experience.Category = (req.Category ?? "").Trim().ToLowerInvariant();
        experience.AllowsChildren = req.AllowsChildren;
        experience.LicenceName = Trimmed(req.LicenceName);
        experience.LicenceExpiresOn = req.LicenceExpiresOn;
        experience.InsurancePolicy = Trimmed(req.InsurancePolicy);
        experience.InsuranceExpiresOn = req.InsuranceExpiresOn;
        experience.SafetyPlan = Trimmed(req.SafetyPlan);
        experience.EmergencyPhone = Trimmed(req.EmergencyPhone);

        if (experience.Id == 0)
        {
            experience.Slug = Slugify(title);
            db.Experiences.Add(experience);
        }

        if (req.Images is { Count: > 0 })
        {
            db.ExperienceImages.RemoveRange(experience.Images);
            experience.Images = req.Images
                .Select((url, i) => new ExperienceImage { Url = url, SortOrder = i })
                .ToList();
        }

        // docs/01 MR-01 — the running order. Replaced wholesale rather than merged:
        // the editor sends the list it has, and reordering a step is the ordinary
        // case, which a merge by id would turn into the hardest one. Null means the
        // caller is not editing the itinerary at all, so what is there stays; an
        // empty list means the host cleared it.
        if (req.Itinerary is not null)
        {
            db.ExperienceSteps.RemoveRange(experience.Itinerary);
            experience.Itinerary = req.Itinerary
                .Where(step => !string.IsNullOrWhiteSpace(step.Title))
                .Select((step, i) => new ExperienceStep
                {
                    Title = step.Title.Trim(),
                    Description = (step.Description ?? "").Trim(),
                    ImageUrl = Trimmed(step.ImageUrl),
                    SortOrder = i
                })
                .ToList();
        }

        // docs/09 §2.2 (MR-E-03) — a host does not publish an experience; they
        // submit it, and a reviewer decides. "Publish" therefore means "send to
        // the queue", and the paperwork the risk band demands has to be in first.
        if (req.Publish)
        {
            if (experience.Images.Count == 0) return (null, "Cần ít nhất một ảnh thật của hoạt động trước khi nộp.");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var missing = ExperienceRules.PublishBlockers(experience, today);
            if (missing.Count > 0)
                return (null, $"Còn thiếu: {string.Join(", ", missing)}.");

            if (experience.ModerationStatus != ExperienceModeration.Approved)
            {
                experience.ModerationStatus = ExperienceModeration.PendingReview;
                experience.SubmittedForReviewAt = DateTime.UtcNow;
                experience.IsPublished = false;
            }
            else
            {
                experience.IsPublished = true;
            }
        }

        experience.RefreshSearchText();
        await db.SaveChangesAsync(ct);

        return (experience.Id, null);
    }

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /* --------------------------------------------- MR-E-09, the day itself */

    /// <summary>
    /// docs/09 §2.9 — who the host should expect: names, how many seats each
    /// booked, and any note they left. This is the sheet they take the register
    /// from, so it carries the attendance mark too.
    /// </summary>
    public async Task<(SessionRosterDto? Roster, string? Error)> RosterAsync(
        User user, int slotId, CancellationToken ct)
    {
        var slot = await db.ExperienceSlots
            .Include(s => s.Experience)
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot?.Experience is null) return (null, "Không tìm thấy suất này.");

        if (await OwnedAsync(user, slot.ExperienceId, ct) is null)
            return (null, "Bạn không có quyền với trải nghiệm này.");

        var rows = await db.ExperienceBookings
            .Where(b => b.SlotId == slotId
                        && b.Status != ExperienceBookingStatus.CancelledByGuest
                        && b.Status != ExperienceBookingStatus.CancelledWithSlot)
            .OrderBy(b => b.CreatedAt)
            .Select(b => new SessionGuestDto(
                b.Id, b.Reference,
                b.GuestUser!.DisplayName ?? b.GuestUser.FullName,
                b.GuestUserId, b.Seats, b.IsPrivate, b.Attended, b.AttendanceMarkedAt))
            .ToListAsync(ct);

        var ends = slot.StartsAt.AddMinutes(slot.Experience.DurationMinutes);

        return (new SessionRosterDto(
            slot.Id, slot.Experience.Title, slot.StartsAt, ends,
            slot.Capacity, slot.SeatsTaken,
            ExperienceAttendance.CanMark(slot.StartsAt, DateTime.UtcNow),
            (int)ExperienceAttendance.LateAllowance.TotalMinutes,
            rows), null);
    }

    /// <summary>
    /// docs/09 §2.9 — the host marks who came. A no-show keeps their money with
    /// the host: they did not cancel, they simply did not turn up.
    /// </summary>
    public async Task<string?> MarkAttendanceAsync(
        User user, int bookingId, bool attended, CancellationToken ct)
    {
        var booking = await db.ExperienceBookings
            .Include(b => b.Slot!).ThenInclude(s => s.Experience)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking?.Slot?.Experience is null) return "Không tìm thấy vé này.";

        if (await OwnedAsync(user, booking.Slot.ExperienceId, ct) is null)
            return "Bạn không có quyền với trải nghiệm này.";

        if (booking.Status is ExperienceBookingStatus.CancelledByGuest
            or ExperienceBookingStatus.CancelledWithSlot)
            return "Vé này đã huỷ nên không điểm danh được.";

        if (!ExperienceAttendance.CanMark(booking.Slot.StartsAt, DateTime.UtcNow))
            return "Chưa tới giờ bắt đầu nên chưa điểm danh được.";

        booking.Attended = attended;
        booking.AttendanceMarkedAt = DateTime.UtcNow;

        // Once the register is taken the ticket is spent, either way.
        booking.Status = ExperienceBookingStatus.Completed;

        await db.SaveChangesAsync(ct);
        return null;
    }

    /* ------------------------------------------------- MR-E-11, the review */

    /// <summary>
    /// docs/09 §2.10 — four criteria of its own, and only from somebody the host
    /// marked present. The experience's headline rating is recomputed from the
    /// reviews themselves rather than nudged, so it always matches what is shown.
    /// </summary>
    public async Task<string?> WriteReviewAsync(
        User user, int bookingId, SubmitExperienceReviewRequest req, CancellationToken ct)
    {
        var booking = await db.ExperienceBookings
            .Include(b => b.Slot!).ThenInclude(s => s.Experience)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.GuestUserId == user.Id, ct);
        if (booking?.Slot?.Experience is null) return "Không tìm thấy vé này.";

        var ends = booking.Slot.StartsAt.AddMinutes(booking.Slot.Experience.DurationMinutes);
        if (!ExperienceReviews.CanReview(booking, ends, DateTime.UtcNow))
            return booking.Attended == true
                ? "Buổi này chưa kết thúc nên chưa đánh giá được."
                : "Chỉ người có mặt trong buổi mới đánh giá được.";

        int[] scores = [req.Host, req.AsDescribed, req.Safety, req.Value];
        if (scores.Any(s => !ExperienceReviews.ScoreInRange(s)))
            return "Mỗi tiêu chí chấm từ 1 đến 5 sao.";

        if (await db.ExperienceReviews.AnyAsync(r => r.BookingId == bookingId, ct))
            return "Bạn đã đánh giá buổi này rồi.";

        db.ExperienceReviews.Add(new ExperienceReview
        {
            BookingId = booking.Id,
            ExperienceId = booking.Slot.ExperienceId,
            AuthorUserId = user.Id,
            HostScore = req.Host,
            AsDescribedScore = req.AsDescribed,
            SafetyScore = req.Safety,
            ValueScore = req.Value,
            Comment = (req.Comment ?? "").Trim()
        });
        await db.SaveChangesAsync(ct);

        var all = await db.ExperienceReviews
            .Where(r => r.ExperienceId == booking.Slot.ExperienceId)
            .Select(r => new { r.HostScore, r.AsDescribedScore, r.SafetyScore, r.ValueScore })
            .ToListAsync(ct);

        var experience = booking.Slot.Experience;
        experience.ReviewCount = all.Count;
        experience.Rating = all.Count == 0
            ? 0
            : Math.Round(all.Average(r =>
                ExperienceReviews.Average(r.HostScore, r.AsDescribedScore, r.SafetyScore, r.ValueScore)), 2);

        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>The reviews shown on an experience, newest first.</summary>
    public async Task<IReadOnlyList<ExperienceReviewDto>> ReviewsAsync(int experienceId, CancellationToken ct) =>
        await db.ExperienceReviews
            .Where(r => r.ExperienceId == experienceId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Select(r => new ExperienceReviewDto(
                r.Id,
                r.AuthorUser!.DisplayName ?? r.AuthorUser.FullName,
                r.AuthorUser.AvatarUrl,
                r.HostScore, r.AsDescribedScore, r.SafetyScore, r.ValueScore,
                r.Comment, r.CreatedAt))
            .ToListAsync(ct);

    /* ------------------------------------------------- MR-E-03, moderation */

    /// <summary>
    /// docs/09 §2.2 — the queue a reviewer works through, oldest first, with the
    /// risk band and what was submitted so the checklist has something to check.
    /// </summary>
    public async Task<IReadOnlyList<PendingExperienceDto>> ReviewQueueAsync(CancellationToken ct)
    {
        var rows = await db.Experiences
            .Where(x => x.ModerationStatus == ExperienceModeration.PendingReview)
            .OrderBy(x => x.SubmittedForReviewAt)
            .Select(x => new
            {
                x.Id, x.Slug, x.Title, x.City, x.Category, x.AllowsChildren,
                x.LicenceName, x.LicenceExpiresOn, x.InsurancePolicy, x.InsuranceExpiresOn,
                x.SafetyPlan, x.EmergencyPhone, x.SubmittedForReviewAt,
                HostName = x.Host!.Name, HostUserId = x.Host.UserId,
                Cover = x.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault()
            })
            .Take(100)
            .ToListAsync(ct);

        return rows.Select(x =>
        {
            var risk = ExperienceRules.RiskOf(x.Category, x.AllowsChildren);
            return new PendingExperienceDto(
                x.Id, x.Slug, x.Title, x.City, x.Category, ExperienceRules.RiskLabel(risk),
                x.AllowsChildren, x.LicenceName, x.LicenceExpiresOn, x.InsurancePolicy,
                x.InsuranceExpiresOn, x.SafetyPlan, x.EmergencyPhone,
                x.HostName ?? "", x.HostUserId ?? 0, x.Cover, x.SubmittedForReviewAt,
                ExperienceRules.ReviewWorkingDays);
        }).ToList();
    }

    /// <summary>
    /// docs/09 §2.2 — one of three answers: approved, sent back with specific
    /// things to fix, or refused with a reason. A bare "no" is not one of them.
    /// </summary>
    public async Task<string?> ReviewAsync(
        User reviewer, int experienceId, string decision, string? note, CancellationToken ct)
    {
        var x = await db.Experiences
            .Include(e => e.Host!).ThenInclude(h => h.User)
            .Include(e => e.Images)
            .FirstOrDefaultAsync(e => e.Id == experienceId, ct);
        if (x is null) return "Không tìm thấy trải nghiệm này.";

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var trimmed = Trimmed(note);

        switch ((decision ?? "").Trim().ToLowerInvariant())
        {
            case "approve":
                var missing = ExperienceRules.PublishBlockers(x, today);
                if (missing.Count > 0) return $"Chưa duyệt được, còn thiếu: {string.Join(", ", missing)}.";

                x.ModerationStatus = ExperienceModeration.Approved;
                x.IsPublished = true;
                break;

            case "changes":
                if (trimmed is null) return "Cần ghi rõ phải sửa gì.";
                x.ModerationStatus = ExperienceModeration.ChangesRequested;
                x.IsPublished = false;
                break;

            case "reject":
                if (trimmed is null) return "Cần ghi rõ lý do từ chối.";
                x.ModerationStatus = ExperienceModeration.Rejected;
                x.IsPublished = false;
                break;

            default:
                return "Quyết định không hợp lệ.";
        }

        x.ReviewedAt = DateTime.UtcNow;
        x.ReviewedByUserId = reviewer.Id;
        x.ReviewerNote = trimmed;

        if (x.Host?.User is { } owner)
        {
            var (title, body) = x.ModerationStatus switch
            {
                ExperienceModeration.Approved =>
                    ("Trải nghiệm đã được duyệt", $"\"{x.Title}\" đã lên sóng và nhận đặt chỗ được."),
                ExperienceModeration.ChangesRequested =>
                    ("Cần chỉnh lại trải nghiệm", $"\"{x.Title}\": {trimmed}"),
                _ => ("Trải nghiệm bị từ chối", $"\"{x.Title}\": {trimmed}")
            };

            await notifications.QueueWithEmailAsync(owner,
                x.ModerationStatus == ExperienceModeration.Approved
                    ? NotificationKind.ListingApproved
                    : NotificationKind.ListingRejected,
                title, body, "/hosting", ct);
        }

        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> AddSlotsAsync(User user, int experienceId, AddSlotsRequest req, CancellationToken ct)
    {
        var experience = await OwnedAsync(user, experienceId, ct);
        if (experience is null) return "Bạn không có quyền với trải nghiệm này.";

        // docs/09 §2.5 (MR-E-04) — a repeating pattern is expanded here into the
        // same list of concrete starts a host would otherwise pick by hand, so the
        // overlap rule below judges both the same way.
        var pattern = req.RepeatWeekdayMask > 0 && req.RepeatAt is { } repeatAt && req.RepeatWeeks > 0
            ? ExperienceRules.ExpandRecurrence(
                req.RepeatWeekdayMask, repeatAt,
                req.RepeatFrom ?? DateOnly.FromDateTime(DateTime.UtcNow),
                req.RepeatWeeks, DateTime.UtcNow, experience.TimeZoneId)
            : [];

        var starts = (req.StartsAt ?? []).Concat(pattern)
            .Distinct().OrderBy(s => s).Take(120).ToList();
        if (starts.Count == 0) return "Chọn ít nhất một giờ bắt đầu.";

        var existing = await db.ExperienceSlots
            .Where(s => s.ExperienceId == experienceId)
            .Select(s => s.StartsAt)
            .ToListAsync(ct);

        // docs/09 §2.5 (scenario 4) — a session that overlaps one already on the
        // calendar (or another in this same batch) is blocked, so the host cannot
        // promise to be in two places at once. An exact repeat is idempotent.
        var duration = experience.DurationMinutes;
        var accepted = new List<DateTime>();
        foreach (var at in starts)
        {
            var utc = DateTime.SpecifyKind(at, DateTimeKind.Utc);
            if (existing.Contains(utc)) continue;

            if (existing.Any(e => ExperienceRules.Overlaps(utc, e, duration))
                || accepted.Any(a => ExperienceRules.Overlaps(utc, a, duration)))
                return $"Suất {utc:HH:mm dd/MM} chồng giờ với một suất khác (mỗi buổi kéo dài {duration} phút).";

            accepted.Add(utc);
        }

        foreach (var utc in accepted)
        {
            db.ExperienceSlots.Add(new ExperienceSlot
            {
                ExperienceId = experienceId,
                StartsAt = utc,
                Capacity = req.Capacity is > 0 ? Math.Min(req.Capacity.Value, experience.MaxGroup) : experience.MaxGroup
            });
        }

        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> CancelSlotAsync(User user, int slotId, string reason, CancellationToken ct)
    {
        var slot = await db.ExperienceSlots
            .Include(s => s.Experience)
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null) return "Không tìm thấy suất này.";

        if (await OwnedAsync(user, slot.ExperienceId, ct) is null)
            return "Bạn không có quyền với trải nghiệm này.";

        await CallOffAsync(slot, reason, ct, byProvider: true);
        return null;
    }

    private async Task<Experience?> OwnedAsync(User user, int experienceId, CancellationToken ct)
    {
        var profile = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == user.Id, ct);
        return profile is null
            ? null
            : await db.Experiences.FirstOrDefaultAsync(x => x.Id == experienceId && x.HostId == profile.Id, ct);
    }

    /* ---------------------------------------------------------- MR-04 */

    /// <summary>
    /// Calls a session off and refunds everyone on it in full. Used both by the
    /// host and by the sweep that enforces the minimum party size.
    /// </summary>
    /// <param name="byProvider">
    /// docs/09 §2.8 — the host pulled out, so each guest also gets TN-D in
    /// balance. The sweep for too few people passes false: nobody is at fault.
    /// </param>
    public async Task CallOffAsync(ExperienceSlot slot, string reason, CancellationToken ct, bool byProvider = false)
    {
        var tickets = await db.ExperienceBookings
            .Include(b => b.Slot)
            .Include(b => b.GuestUser)
            .Where(b => b.SlotId == slot.Id && b.Status == ExperienceBookingStatus.Confirmed)
            .ToListAsync(ct);

        // docs/09 §2.8 (MR-E-08) — "gợi ý khách chuyển sang suất khác". Being told
        // the session is off is only half the message; the other half is when else
        // they could go.
        var siblings = await db.ExperienceSlots
            .Where(s => s.ExperienceId == slot.ExperienceId)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;

        foreach (var ticket in tickets)
        {
            // Nobody pays for a session that was called off, whatever the reason.
            await ReleaseAsync(ticket, ticket.Total, ExperienceBookingStatus.CancelledWithSlot, reason, ct);

            var credit = byProvider ? ExperienceRules.ProviderCancelCredit(ticket.Total) : 0m;
            if (credit > 0)
            {
                db.CreditEntries.Add(CreditLedger.Grant(
                    ticket.GuestUserId, credit, CreditReason.Goodwill,
                    $"Đền bù vì người dẫn huỷ suất — vé {ticket.Reference}", now));
                db.LedgerEntries.AddRange(Ledger.GrantCredit(
                    null, credit, $"Đền bù huỷ suất trải nghiệm {ticket.Reference}", now));
            }

            var others = ExperienceRules.AlternativesFor(siblings, slot.Id, ticket.Seats, now);
            var suggestion = others.Count == 0
                ? ""
                : " Còn suất khác: " + string.Join(", ", others.Select(s => $"{s.StartsAt:HH:mm dd/MM}")) + ".";

            await notifications.QueueWithEmailAsync(
                ticket.GuestUser, NotificationKind.BookingCancelled,
                "Suất trải nghiệm đã bị huỷ",
                $"{reason} Toàn bộ {ticket.Total:#,##0}₫ đã được hoàn lại." +
                (credit > 0 ? $" Bạn được tặng thêm {credit:#,##0}₫ số dư Staylio." : "") + suggestion,
                "/experiences", ct);
        }

        slot.Status = SlotStatus.Cancelled;
        slot.CancelReason = reason;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Experience slot {SlotId} called off: {Reason}", slot.Id, reason);
    }

    /// <summary>
    /// docs/01 MR-04 — sessions close to starting without enough people are
    /// called off automatically, and everyone gets their money back.
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var horizon = now + ExperienceRules.MinimumCheck;

        var due = await db.ExperienceSlots
            .Include(s => s.Experience)
            .Where(s => s.Status == SlotStatus.Open && s.StartsAt > now && s.StartsAt <= horizon)
            .ToListAsync(ct);

        var called = 0;
        foreach (var slot in due.Where(s => ExperienceRules.ShouldCallOff(s.Experience!, s, now)))
        {
            await CallOffAsync(slot,
                $"Không đủ {slot.Experience!.MinGuests} người tối thiểu nên suất này bị huỷ.", ct);
            called++;
        }

        return called;
    }

    /* ------------------------------------------------------------- DTOs */

    public async Task<ExperienceBookingDto?> BookingDtoAsync(int id, CancellationToken ct) =>
        (await BookingsAsync(b => b.Id == id, ct)).FirstOrDefault();

    public async Task<IReadOnlyList<ExperienceBookingDto>> MyBookingsAsync(int userId, CancellationToken ct) =>
        await BookingsAsync(b => b.GuestUserId == userId, ct);

    private async Task<List<ExperienceBookingDto>> BookingsAsync(
        System.Linq.Expressions.Expression<Func<ExperienceBooking, bool>> where, CancellationToken ct) =>
        await db.ExperienceBookings
            .Where(where)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new ExperienceBookingDto(
                b.Id, b.Reference,
                b.Slot!.ExperienceId, b.Slot.Experience!.Title, b.Slot.Experience.City,
                b.Slot.Experience.Slug, b.Slot.StartsAt, b.Slot.Experience.DurationMinutes,
                b.Seats, b.IsPrivate,
                b.Subtotal, b.ServiceFee, b.Tax, b.Total, b.RefundedAmount,
                b.Status.ToString(),
                ExperienceRules.StatusLabel(b.Status),
                ExperienceRules.StatusBadge(b.Status),
                b.CancelReason, b.CreatedAt,
                b.Attended,
                db.ExperienceReviews.Any(r => r.BookingId == b.Id)))
            .ToListAsync(ct);

    private static string Slugify(string title)
    {
        var normalised = SearchText.Normalize(title);
        var slug = new string(normalised.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return $"{slug.Trim('-')}-{Guid.NewGuid().ToString("N")[..6]}";
    }
}
