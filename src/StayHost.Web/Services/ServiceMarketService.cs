using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;

namespace StayHost.Web.Services;

/// <summary>
/// docs/01 MR-05 → MR-07. A chef, a photographer, an airport transfer: sold by
/// the session, often at the guest's address, and sometimes run by a partner
/// the platform takes a commission from rather than a host.
/// </summary>
public class ServiceMarketService(
    StayHostDbContext db, CatalogService catalog, NotificationService notifications,
    PaymentGateway gateway, Gateways.PspRouter router, BankTransferSettings bank,
    RefundGateway refunds, ILogger<ServiceMarketService> log)
{
    /// <summary>
    /// The statuses that have given the provider's time back. Everything else —
    /// including a booking still waiting for its bank transfer — is holding that
    /// slot, and must keep the next guest out of it.
    ///
    /// Kept in one place because it is read by both the picker and the check
    /// that refuses a booking, and those two disagreeing means the picker offers
    /// a time the server then refuses.
    /// </summary>
    private static readonly ServiceBookingStatus[] LetGo =
    [
        ServiceBookingStatus.CancelledByGuest,
        ServiceBookingStatus.CancelledByProvider,
        ServiceBookingStatus.PaymentExpired,
        // The provider went, so the hour is spent — but the job is over, and the
        // slot must not keep blocking the calendar behind it.
        ServiceBookingStatus.ConditionsMisdeclared
    ];

    public async Task<IReadOnlyList<ServiceCardDto>> BrowseAsync(
        string? q, string? category, string? city, CancellationToken ct)
    {
        var query = db.ServiceOfferings.Where(o => o.IsPublished);

        foreach (var term in SearchText.Terms(q))
        {
            var t = term;
            query = query.Where(o => EF.Functions.Like(o.SearchText, $"%{t}%"));
        }

        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(o => o.Category == category);
        if (!string.IsNullOrWhiteSpace(city)) query = query.Where(o => o.City == city);

        return await query
            .OrderByDescending(o => o.Rating).ThenBy(o => o.Id)
            .Select(o => new ServiceCardDto(
                o.Id, o.Slug, o.Title, o.Category, o.City, o.Summary,
                o.BasePrice, o.Pricing.ToString(), ServiceRules.PricingLabel(o.Pricing),
                ServiceRules.UnitLabel(o.Pricing),
                o.TravelsToGuest, o.ServiceRadiusKm,
                o.IsPartner, o.PartnerName,
                o.Rating, o.ReviewCount, o.Host!.Name,
                o.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).ToList(),
                o.DurationMinutes, o.Host.AvatarUrl))
            .ToListAsync(ct);
    }

    /// <summary>
    /// docs/09 §4 (MR-C-02) — the cross-sell from a booked stay. A service has no
    /// fixed sessions the way an experience does: docs/09 §3.4 gives a provider
    /// working hours and a diary, so any published provider covering that city can
    /// be asked for a slot during the stay. City is therefore the whole test, and
    /// the dates are settled later on the service's own booking form, which is
    /// where the buffer and travel-time rules of §3.4 live.
    ///
    /// Shaped by <see cref="BrowseAsync"/> on purpose: one projection of a service
    /// card, so a change to what a card says lands on the browse page and on the
    /// trip page together.
    /// </summary>
    public async Task<IReadOnlyList<ServiceCardDto>> SuggestForStayAsync(
        string city, int take, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(city)
            ? []
            : (await BrowseAsync(null, null, city, ct)).Take(take).ToList();

    public async Task<ServiceDetailDto?> DetailAsync(string idOrSlug, CancellationToken ct)
    {
        var query = db.ServiceOfferings
            .Include(o => o.Host).Include(o => o.Images).Include(o => o.AddOns);

        var o = int.TryParse(idOrSlug, out var id)
            ? await query.FirstOrDefaultAsync(x => x.Id == id, ct)
            : await query.FirstOrDefaultAsync(x => x.Slug == idOrSlug, ct);
        if (o is null) return null;

        // Two months of taken slots, so the picker can grey them out. It used to
        // be a fortnight, which was exactly as far as the picker looked; now the
        // dialog offers a month rail and a month grid, and a job outside the
        // window would have shown as free right up until the server refused it.
        var from = DateTime.UtcNow;
        var to = from.AddDays(60);
        var busy = await db.ServiceBookings
            .Where(b => b.OfferingId == o.Id
                        && !LetGo.Contains(b.Status)
                        && b.StartsAt < to && b.StartsAt >= from.AddDays(-1))
            .Select(b => new { b.StartsAt, b.DurationMinutes })
            .ToListAsync(ct);

        return new ServiceDetailDto(
            o.Id, o.Slug, o.Title, o.Category, o.City, o.Country, o.Summary, o.Description,
            o.BasePrice, o.Pricing.ToString(), ServiceRules.PricingLabel(o.Pricing),
            ServiceRules.UnitLabel(o.Pricing),
            o.MinQuantity, o.MaxQuantity, o.DurationMinutes,
            o.TravelsToGuest, o.ServiceRadiusKm, o.Latitude, o.Longitude,
            o.OpensAtHour, o.ClosesAtHour,
            o.IsPartner, o.PartnerName, o.IsPublished,
            o.Rating, o.ReviewCount, o.Host?.Name ?? "", o.Host?.Initials ?? "",
            o.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).ToList(),
            busy.Select(b => new BusySlotDto(b.StartsAt, b.StartsAt.AddMinutes(b.DurationMinutes))).ToList(),
            ServiceRules.RequiredNote(o.Category) is var kind && kind != ServiceRules.NoteKind.None
                ? ServiceRules.NoteLabel(kind) : null,
            o.AddOns.Where(a => a.IsActive).OrderBy(a => a.SortOrder)
                .Select(a => new ServiceAddOnDto(a.Id, a.Name, a.Price)).ToList(),
            o.RequirementList,
            // Normalised on the way out too: the picker draws the working week
            // from this, so a stored 0 would grey out every day on the page even
            // once the server had stopped refusing the booking.
            o.TravelFeePerKm, o.MaxTravelKm, ServiceRules.WorkingDays(o.WorkingDaysMask), o.MaxJobsPerDay,
            o.CertificateName, o.CertificateExpiresOn, o.BufferMinutes,
            o.Host?.AvatarUrl, o.Host?.YearsHosting ?? 0, o.Host?.Bio,
            o.Host?.IsSuperhost ?? false, o.Host?.UserId);
    }

    /// <summary>
    /// The hour a request asked for, as an instant.
    ///
    /// The browser's picker sends <c>toISOString()</c>, which carries a Z and
    /// binds as UTC, so this never bit a guest. Anything else — a partner, a
    /// mobile client, curl — sends "2026-08-17T02:00:00", which binds as
    /// <see cref="DateTimeKind.Unspecified"/>, and the first EF query holding it
    /// threw <c>ArgumentException</c> out of the controller: an HTTP 500 with a
    /// stack trace where a booking should have been. Somebody had already met
    /// this and pinned the Kind on the row being *written*, but the availability
    /// check reads first, so the fix never ran.
    ///
    /// Unspecified is read as UTC because that is what every other clock in this
    /// codebase means; the provider's own opening hours are converted from it by
    /// <c>ServiceRules.LocalTime</c>.
    /// </summary>
    private static DateTime AsInstant(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public async Task<ServiceQuoteDto?> QuoteAsync(int offeringId, QuoteServiceRequest req, CancellationToken ct)
    {
        var offering = await db.ServiceOfferings.FirstOrDefaultAsync(o => o.Id == offeringId, ct);
        if (offering is null) return null;

        req = req with { StartsAt = AsInstant(req.StartsAt) };

        var check = ServiceRules.CanBook(await BuildCheckAsync(offering, req.StartsAt, req.Quantity,
            req.Address, req.Latitude, req.Longitude, ct, req.ConditionsConfirmed));

        var price = Pricing.QuoteService(new Pricing.ServiceRequest
        {
            Offering = offering,
            Quantity = req.Quantity,
            StartsAt = req.StartsAt,
            TaxRules = await catalog.ActiveTaxRulesAsync(ct),
            AddOns = await ChosenAddOnsAsync(offering.Id, req.AddOnIds, ct),
            DistanceKm = DistanceFor(offering, req.Latitude, req.Longitude)
        });

        return new ServiceQuoteDto(
            offering.Id, req.StartsAt, offering.DurationMinutes, price.Quantity,
            price.Subtotal, price.GuestServiceFee, price.Tax, price.Total,
            price.Lines.Select(l => new PriceLineDto(l.Key, l.Label, l.Amount)).ToList(),
            check.Ok, check.Ok ? null : check.Message);
    }

    /* ------------------------------------------------ MR-S-01, the provider */

    /// <summary>
    /// docs/09 §3.2 (MR-S-01) — an individual provider lists through their own
    /// host account; the partner business account of MR-S-09 is a later phase.
    /// Like an experience, a service is submitted rather than published: a
    /// category that needs a practising certificate cannot go on sale without
    /// one, and a certificate that has already lapsed is no certificate at all.
    /// </summary>
    public async Task<(int? Id, string? Error)> SaveOfferingAsync(
        User user, SaveServiceRequest req, CancellationToken ct)
    {
        var profile = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == user.Id, ct);
        if (profile is null) return (null, "Bạn cần có hồ sơ chủ nhà trước.");

        var offering = req.Id is { } id
            ? await db.ServiceOfferings
                .Include(o => o.Images).Include(o => o.AddOns)
                .FirstOrDefaultAsync(o => o.Id == id && o.HostId == profile.Id, ct)
            : new ServiceOffering { HostId = profile.Id };
        if (offering is null) return (null, "Không tìm thấy dịch vụ này.");

        var title = (req.Title ?? "").Trim();
        if (title.Length < 4) return (null, "Tên dịch vụ quá ngắn.");
        if (req.BasePrice <= 0) return (null, "Giá phải lớn hơn 0.");
        if (req.MinQuantity < 1 || req.MaxQuantity < req.MinQuantity)
            return (null, "Số lượng nhận việc chưa hợp lệ.");
        if (!Enum.TryParse<ServicePricing>(req.Pricing, true, out var pricing))
            return (null, "Mô hình giá không hợp lệ.");

        offering.Title = title;
        offering.Category = (req.Category ?? "").Trim().ToLowerInvariant();
        offering.City = (req.City ?? "").Trim();
        offering.Summary = (req.Summary ?? "").Trim();
        offering.Description = (req.Description ?? "").Trim();
        offering.Pricing = pricing;
        offering.BasePrice = req.BasePrice;
        offering.MinQuantity = req.MinQuantity;
        offering.MaxQuantity = req.MaxQuantity;
        offering.DurationMinutes = Math.Clamp(req.DurationMinutes, 15, 60 * 12);
        offering.TravelsToGuest = req.TravelsToGuest;
        offering.ServiceRadiusKm = Math.Clamp(req.ServiceRadiusKm, 0, 200);
        offering.Latitude = req.Latitude;
        offering.Longitude = req.Longitude;
        offering.OpensAtHour = Math.Clamp(req.OpensAtHour, 0, 23);
        offering.ClosesAtHour = Math.Clamp(req.ClosesAtHour, 1, 24);

        // docs/09 §3.3–§3.4 — the journey, the working week and the place itself.
        offering.TravelFeePerKm = Math.Max(0, req.TravelFeePerKm);
        offering.MaxTravelKm = Math.Clamp(req.MaxTravelKm, 0, 200);
        offering.WorkingDaysMask = req.WorkingDaysMask is > 0 and < 128 ? req.WorkingDaysMask : 127;
        offering.BufferMinutes = Math.Clamp(req.BufferMinutes, 0, 240);
        offering.MaxJobsPerDay = Math.Clamp(req.MaxJobsPerDay, 0, 20);
        offering.OnSiteRequirements = string.Join('\n', req.OnSiteRequirements ?? []);

        // docs/09 §3.2 — the practising certificate this category demands.
        offering.CertificateName = string.IsNullOrWhiteSpace(req.CertificateName)
            ? null : req.CertificateName.Trim();
        offering.CertificateExpiresOn = req.CertificateExpiresOn;

        if (offering.Id == 0)
        {
            offering.Slug = Slugify(title);
            db.ServiceOfferings.Add(offering);
        }

        if (req.Images is { Count: > 0 })
        {
            db.ServiceImages.RemoveRange(offering.Images);
            offering.Images = req.Images
                .Select((url, i) => new ServiceImage { Url = url, SortOrder = i })
                .ToList();
        }

        if (req.AddOns is not null)
        {
            db.ServiceAddOns.RemoveRange(offering.AddOns);
            offering.AddOns = req.AddOns
                .Where(a => !string.IsNullOrWhiteSpace(a.Name) && a.Price >= 0)
                .Select((a, i) => new ServiceAddOn
                {
                    Name = a.Name!.Trim(), Price = a.Price, SortOrder = i, IsActive = true
                })
                .ToList();
        }

        if (req.Publish)
        {
            if (offering.Images.Count == 0) return (null, "Cần ít nhất một ảnh thật trước khi mở bán.");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (ServiceRules.NeedsCertificate(offering.Category))
            {
                if (string.IsNullOrWhiteSpace(offering.CertificateName))
                    return (null, "Danh mục này bắt buộc có chứng chỉ hành nghề.");
                if (ServiceRules.CertificateLapsed(offering.CertificateExpiresOn, today))
                    return (null, "Chứng chỉ đã hết hạn, cần gia hạn trước khi mở bán.");
            }

            offering.IsPublished = true;
            offering.HiddenByExpiredCertificate = false;
        }

        offering.RefreshSearchText();
        await db.SaveChangesAsync(ct);
        return (offering.Id, null);
    }

    /// <summary>The provider's own services, for their console.</summary>
    public async Task<IReadOnlyList<ServiceDetailDto>> MineAsync(int userId, CancellationToken ct)
    {
        var profile = await db.Hosts.FirstOrDefaultAsync(h => h.UserId == userId, ct);
        if (profile is null) return [];

        var ids = await db.ServiceOfferings
            .Where(o => o.HostId == profile.Id)
            .Select(o => o.Slug)
            .ToListAsync(ct);

        var list = new List<ServiceDetailDto>();
        foreach (var slug in ids)
            if (await DetailAsync(slug, ct) is { } dto) list.Add(dto);

        return list;
    }

    private static string Slugify(string title)
    {
        var normalized = SearchText.Normalize(title);
        var slug = string.Concat(normalized.Select(c => char.IsLetterOrDigit(c) ? c : '-'))
            .Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";
    }

    private async Task<ServiceRules.Request> BuildCheckAsync(
        ServiceOffering offering, DateTime startsAt, int quantity,
        string? address, double? lat, double? lng, CancellationToken ct,
        bool conditionsConfirmed = false)
    {
        var day = startsAt.Date;
        var busy = await db.ServiceBookings
            .Where(b => b.OfferingId == offering.Id
                        && !LetGo.Contains(b.Status)
                        && b.StartsAt >= day.AddDays(-1) && b.StartsAt < day.AddDays(2))
            .Select(b => new { b.StartsAt, b.DurationMinutes, b.Latitude, b.Longitude })
            .ToListAsync(ct);

        return new ServiceRules.Request
        {
            Offering = offering,
            StartsAt = DateTime.SpecifyKind(startsAt, DateTimeKind.Utc),
            Now = DateTime.UtcNow,
            Quantity = quantity,
            Address = address,
            Latitude = lat,
            Longitude = lng,
            Busy = busy.Select(b => new ServiceRules.BusyJob(
                b.StartsAt, b.StartsAt.AddMinutes(b.DurationMinutes), b.Latitude, b.Longitude)).ToList(),
            ConditionsConfirmed = conditionsConfirmed
        };
    }

    /// <summary>
    /// The extras the guest ticked, read back from the offering rather than
    /// trusted from the request — a price that arrives from the browser is a
    /// price the guest chose for themselves.
    /// </summary>
    private async Task<List<ServiceAddOn>> ChosenAddOnsAsync(
        int offeringId, IReadOnlyList<int>? ids, CancellationToken ct) =>
        ids is not { Count: > 0 }
            ? []
            : await db.ServiceAddOns
                .Where(a => a.OfferingId == offeringId && a.IsActive && ids.Contains(a.Id))
                .ToListAsync(ct);

    /// <summary>How far the job is from the provider's base, for the travel fee.</summary>
    private static double DistanceFor(ServiceOffering o, double? lat, double? lng) =>
        o.TravelsToGuest && lat is { } la && lng is { } ln
            ? ServiceRules.DistanceKm(o.Latitude, o.Longitude, la, ln)
            : 0;

    public async Task<(ServiceBooking? Booking, string? Error)> BookAsync(
        User user, int offeringId, BookServiceRequest req, CancellationToken ct)
    {
        var offering = await db.ServiceOfferings.FirstOrDefaultAsync(o => o.Id == offeringId, ct);
        if (offering is null) return (null, "Không tìm thấy dịch vụ này.");

        // docs/09 §3.2 — the certificate sweep hides a listing by unpublishing
        // it; a saved link still reached this and booked the job anyway.
        if (!offering.IsPublished) return (null, "Dịch vụ này hiện không nhận đặt.");

        // Before the first query touches it — see AsInstant.
        req = req with { StartsAt = AsInstant(req.StartsAt) };

        var check = ServiceRules.CanBook(await BuildCheckAsync(
            offering, req.StartsAt, req.Quantity, req.Address, req.Latitude, req.Longitude, ct,
            req.ConditionsConfirmed));
        if (!check.Ok) return (null, check.Message);

        // docs/09 §3.5 (scenario 10) — a chef/massage/fitness job cannot be sent
        // without the safety note its category demands.
        if (ServiceRules.NoteMissing(offering.Category, req.Note))
            return (null, $"Cần điền {ServiceRules.NoteLabel(ServiceRules.RequiredNote(offering.Category))} trước khi đặt.");

        var chosen = await ChosenAddOnsAsync(offering.Id, req.AddOnIds, ct);
        var price = Pricing.QuoteService(new Pricing.ServiceRequest
        {
            Offering = offering,
            Quantity = req.Quantity,
            StartsAt = req.StartsAt,
            TaxRules = await catalog.ActiveTaxRulesAsync(ct),
            AddOns = chosen,
            DistanceKm = DistanceFor(offering, req.Latitude, req.Longitude)
        });

        // docs/07 §2.3 — a bank transfer is not charged here. The job is written
        // down holding its place in the provider's day, and stays unconfirmed
        // until the money is found on a statement. Nothing is captured, so the
        // provider is not told to turn up for a job nobody has paid for.
        //
        // docs/07 §13 — a method a licensed gateway serves waits the same way,
        // until PspCheckout hears the money moved (ConfirmPaidAsync).
        var (road, refusal) = ProductCheckout.Choose(router, bank, req.PaymentMethod);
        if (refusal is not null) return (null, refusal);
        var waits = road != ProductCheckout.Road.StandIn;

        if (!waits)
        {
            var attempt = gateway.Charge(
                price.Total, ProductCheckout.Normalise(req.PaymentMethod), req.CardLast4);
            if (!attempt.Ok) return (null, attempt.Reason);
        }

        var booking = new ServiceBooking
        {
            Reference = $"SV{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            OfferingId = offering.Id,
            GuestUserId = user.Id,
            // Already an instant by the time it gets here (AsInstant, above).
            StartsAt = req.StartsAt,
            DurationMinutes = offering.DurationMinutes,
            Quantity = price.Quantity,
            Address = (req.Address ?? "").Trim(),
            Latitude = req.Latitude ?? 0,
            Longitude = req.Longitude ?? 0,
            Note = req.Note?.Trim(),
            Subtotal = price.Subtotal,
            ServiceFee = price.GuestServiceFee,
            Tax = price.Tax,
            Total = price.Total,
            PlatformCut = price.PlatformCut,
            ProviderPayout = price.ProviderPayout,
            // docs/09 §3.3 — the receipt keeps its own copy of each extra's name
            // and price, so retiring one later never rewrites an old booking.
            AddOnsTotal = price.AddOnsTotal,
            TravelFee = price.TravelFee,
            ConditionsConfirmed = req.ConditionsConfirmed,
            Status = waits ? ServiceBookingStatus.AwaitingPayment : ServiceBookingStatus.Confirmed,
            AddOns = chosen
                .Select(a => new ServiceBookingAddOn { AddOnId = a.Id, Name = a.Name, Price = a.Price })
                .ToList()
        };

        db.ServiceBookings.Add(booking);
        await db.SaveChangesAsync(ct);

        // Nothing else happens yet: no capture, no notification to the provider.
        // BankTransferService or PspCheckout does both the moment the money is found.
        if (waits) return (booking, null);

        db.LedgerEntries.AddRange(Ledger.CaptureService(booking, DateTime.UtcNow));
        await db.SaveChangesAsync(ct);

        await notifications.QueueWithEmailAsync(
            user, NotificationKind.BookingConfirmed,
            "Đã đặt dịch vụ",
            $"{offering.Title} · {booking.StartsAt:dd/MM HH:mm} · mã {booking.Reference}.",
            "/services/bookings", ct);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Service {Reference} booked.", booking.Reference);
        return (booking, null);
    }

    /// <summary>
    /// docs/07 §2.3 — the money for a transfer-booked job has been found on a
    /// statement. This is the rest of BookAsync, run late: the capture and the
    /// notifications that were deliberately not done at booking time.
    /// </summary>
    /// <summary>
    /// docs/07 §13 — a gateway says the money for this job moved. False when the
    /// job stopped waiting for it, which tells the caller to send it back.
    /// </summary>
    public async Task<bool> ConfirmPaidAsync(int bookingId, CancellationToken ct)
    {
        var booking = await db.ServiceBookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null || booking.Status != ServiceBookingStatus.AwaitingPayment) return false;

        await ConfirmTransferAsync(booking, ct, "Đã đặt dịch vụ");
        return true;
    }

    public async Task ConfirmTransferAsync(
        ServiceBooking booking, CancellationToken ct, string title = "Đã nhận được chuyển khoản")
    {
        if (booking.Status != ServiceBookingStatus.AwaitingPayment) return;

        booking.Status = ServiceBookingStatus.Confirmed;
        db.LedgerEntries.AddRange(Ledger.CaptureService(booking, DateTime.UtcNow));
        await db.SaveChangesAsync(ct);

        var offering = await db.ServiceOfferings.FirstOrDefaultAsync(o => o.Id == booking.OfferingId, ct);
        var guest = await db.Users.FirstOrDefaultAsync(u => u.Id == booking.GuestUserId, ct);

        await notifications.QueueWithEmailAsync(
            guest, NotificationKind.BookingConfirmed,
            title,
            $"{offering?.Title} · {booking.StartsAt:dd/MM HH:mm} · mã {booking.Reference}.",
            "/services/bookings", ct);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Service {Reference} confirmed after payment.", booking.Reference);
    }

    /// <summary>
    /// docs/07 §2.3 — jobs whose transfer window ran out. The slot goes back to
    /// the provider's day; nothing was captured, so there is nothing to refund.
    /// Runs on the lifecycle tick alongside the other sweeps.
    /// </summary>
    public async Task<int> ExpireAwaitingTransfersAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - BankTransfers.Window;
        var gatewayCutoff = DateTime.UtcNow - PaymentSession.Window * 2;

        var stale = await db.ServiceBookings
            .Where(b => b.Status == ServiceBookingStatus.AwaitingPayment
                        && (b.CreatedAt <= cutoff
                            || (b.CreatedAt <= gatewayCutoff
                                && db.PaymentSessions.Any(s => s.ServiceBookingId == b.Id)
                                && !db.PaymentSessions.Any(s => s.ServiceBookingId == b.Id
                                                                && s.Status == PaymentSessionStatus.Paid))))
            .Take(200)
            .ToListAsync(ct);
        if (stale.Count == 0) return 0;

        foreach (var booking in stale)
        {
            booking.Status = ServiceBookingStatus.PaymentExpired;
            booking.CancelReason = "Hết hạn chờ thanh toán.";
            booking.CancelledAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Huỷ {Count} đơn dịch vụ hết hạn chờ chuyển khoản.", stale.Count);
        return stale.Count;
    }

    public async Task<string?> CancelAsync(int userId, int bookingId, CancellationToken ct)
    {
        var booking = await db.ServiceBookings
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.GuestUserId == userId, ct);
        if (booking is null) return "Không tìm thấy đơn dịch vụ này.";
        // docs/07 §2.3 — a guest walking away before they transfer. No money has
        // moved, so there is nothing to refund and no ledger entry to make; the
        // slot simply goes back rather than running the cancellation tiers over
        // an amount that was never paid.
        if (booking.Status == ServiceBookingStatus.AwaitingPayment)
        {
            booking.Status = ServiceBookingStatus.PaymentExpired;
            booking.CancelReason = "Khách không chuyển khoản nữa.";
            booking.CancelledAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return null;
        }

        if (booking.Status is not (ServiceBookingStatus.Confirmed or ServiceBookingStatus.Requested))
            return "Đơn này không còn hiệu lực.";

        var refund = ServiceRules.GuestRefund(booking, DateTime.UtcNow);

        booking.Status = ServiceBookingStatus.CancelledByGuest;
        booking.RefundedAmount = refund;
        booking.CancelReason = refund > 0
            ? "Khách huỷ trước 24 giờ."
            : "Khách huỷ sát giờ, không hoàn tiền.";
        booking.CancelledAt = DateTime.UtcNow;

        db.LedgerEntries.AddRange(Ledger.RefundService(booking, refund, DateTime.UtcNow));
        await db.SaveChangesAsync(ct);
        await SendRefundAsync(booking, refund, ct);

        return null;
    }

    /// <summary>
    /// docs/07 §10 — the money goes back the way the job was paid; what the card
    /// will not take becomes the guest's balance. The books used to record the
    /// refund with no call to anybody.
    /// </summary>
    private async Task SendRefundAsync(ServiceBooking booking, decimal refund, CancellationToken ct)
    {
        var sent = await refunds.SendForAsync(
            s => s.ServiceBookingId == booking.Id, refund, booking.Reference, "card", null, "system", ct);
        if (sent.Bounced <= 0) return;

        db.LedgerEntries.AddRange(Ledger.RefundKeptAsCredit(
            null, booking.Id, booking.Reference, sent.Bounced, DateTime.UtcNow));
        db.CreditEntries.Add(CreditLedger.Grant(
            booking.GuestUserId, sent.Bounced, CreditReason.Returned,
            $"Hoàn dịch vụ {booking.Reference} — thẻ không nhận được", DateTime.UtcNow));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// docs/09 §3.5 — the jobs somebody has been booked for.
    ///
    /// The provider console listed the services they *sell* and nothing about
    /// the work itself, so the mandatory allergy and health notes a guest is
    /// forced to write went into the database and were never read by the person
    /// they were written for.
    ///
    /// The guest's phone follows the same gate as a stay (docs/03 §10): it
    /// appears once the job is confirmed, not while it is still a request or
    /// waiting on a transfer.
    /// </summary>
    public async Task<IReadOnlyList<ProviderJobDto>> JobsAsync(int providerUserId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var rows = await db.ServiceBookings
            .Where(b => b.Offering!.Host!.UserId == providerUserId)
            .OrderByDescending(b => b.StartsAt)
            .Take(200)
            .Select(b => new
            {
                b.Id, b.Reference, b.StartsAt, b.DurationMinutes, b.Quantity,
                b.Address, b.Note, b.Total, b.ProviderPayout, b.Status, b.CancelReason,
                Title = b.Offering!.Title,
                Pricing = b.Offering.Pricing,
                Guest = b.GuestUser!.DisplayName ?? b.GuestUser.FullName ?? "Khách",
                Phone = b.GuestUser.Phone
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var confirmed = r.Status == ServiceBookingStatus.Confirmed;
            return new ProviderJobDto(
                r.Id, r.Reference, r.Title, r.Guest,
                r.StartsAt, r.StartsAt.AddMinutes(r.DurationMinutes),
                r.Quantity, ServiceRules.UnitLabel(r.Pricing),
                r.Address, r.Note,
                confirmed ? r.Phone : null,
                r.Total, r.ProviderPayout,
                r.Status.ToString(),
                ServiceRules.StatusLabel(r.Status),
                ServiceRules.StatusBadge(r.Status),
                r.CancelReason,
                // The same three conditions ReportMisdeclaredAsync enforces, so a
                // button is never offered that the server would then refuse.
                CanReportMisdeclared:
                r.Status is ServiceBookingStatus.Confirmed or ServiceBookingStatus.Requested
                && now >= r.StartsAt);
        }).ToList();
    }

    /// <summary>
    /// docs/09 §3.6 (DV-D) — the provider arrived and the site was not what the
    /// guest declared. "Đầu bếp tới nơi mới biết nhà không có bếp thì không được
    /// để họ mất trắng."
    ///
    /// This had no way in at all: <c>ProviderShareOnMisdeclared</c> was written
    /// and tested when the parameter was locked, and nothing ever called it, so
    /// the one case the document singles out ended with the provider paid
    /// nothing or the guest charged in full, depending on who cancelled first.
    ///
    /// Only the provider may report it, only once the hour has started — before
    /// that they cannot have seen the place — and only on a job that was paid
    /// for. Their word settles it here; a guest who disagrees has the resolution
    /// centre, which is where a dispute belongs.
    /// </summary>
    public async Task<string?> ReportMisdeclaredAsync(
        int providerUserId, int bookingId, string? note, CancellationToken ct)
    {
        var booking = await db.ServiceBookings
            .Include(b => b.Offering).ThenInclude(o => o!.Host)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return "Không tìm thấy đơn dịch vụ này.";

        if (booking.Offering?.Host?.UserId != providerUserId)
            return "Bạn không phải người nhận đơn này.";

        if (booking.Status is not (ServiceBookingStatus.Confirmed or ServiceBookingStatus.Requested))
            return "Đơn này không còn hiệu lực.";

        var now = DateTime.UtcNow;
        if (now < booking.StartsAt)
            return "Chỉ báo được sau giờ hẹn — trước đó bạn chưa tới nơi.";

        var refund = ServiceRules.GuestRefundOnMisdeclared(booking.Total);

        booking.Status = ServiceBookingStatus.ConditionsMisdeclared;
        booking.RefundedAmount = refund;
        booking.CancelReason = string.IsNullOrWhiteSpace(note)
            ? "Điều kiện tại chỗ không đúng như khách khai."
            : $"Điều kiện tại chỗ không đúng như khách khai: {note.Trim()}";
        booking.CancelledAt = now;

        // Half the order goes back; the half that stays settles the ordinary way,
        // so the provider is paid out of it by the usual sweep rather than by a
        // second, special path that could disagree with the first.
        db.LedgerEntries.AddRange(Ledger.RefundService(booking, refund, now));
        await db.SaveChangesAsync(ct);
        await SendRefundAsync(booking, refund, ct);

        await notifications.QueueWithEmailAsync(
            await db.Users.FirstAsync(u => u.Id == booking.GuestUserId, ct),
            NotificationKind.System,
            "Dịch vụ không thực hiện được tại chỗ",
            $"Nhà cung cấp đến nơi nhưng điều kiện không như bạn khai cho đơn {booking.Reference}. " +
            $"Bạn được hoàn {refund:#,##0}₫; phần còn lại trả cho công đi lại của họ. " +
            "Nếu bạn thấy chưa đúng, mở khiếu nại ở trung tâm giải quyết.",
            "/services/bookings", ct);

        return null;
    }

    public async Task<IReadOnlyList<ServiceBookingDto>> MyBookingsAsync(int userId, CancellationToken ct) =>
        await db.ServiceBookings
            .Where(b => b.GuestUserId == userId)
            .OrderByDescending(b => b.StartsAt)
            .Select(b => new ServiceBookingDto(
                b.Id, b.Reference, b.OfferingId, b.Offering!.Title, b.Offering.Slug,
                b.Offering.Category, b.Offering.City,
                b.StartsAt, b.DurationMinutes, b.Quantity,
                ServiceRules.UnitLabel(b.Offering.Pricing),
                b.Address, b.Note,
                b.Subtotal, b.ServiceFee, b.Tax, b.Total, b.RefundedAmount,
                b.Status.ToString(), ServiceRules.StatusLabel(b.Status), ServiceRules.StatusBadge(b.Status),
                b.CancelReason, b.CreatedAt,
                db.ServiceReviews.Any(r => r.BookingId == b.Id)))
            .ToListAsync(ct);

    /// <summary>
    /// docs/09 §3.2 (MR-S-02, scenario 9) — watches practising certificates: warns
    /// the provider thirty days before one runs out, and takes the listing down by
    /// itself the day it lapses. A masseur whose certificate expired is not
    /// somebody the platform may keep selling, and waiting for a human to notice
    /// is how that goes wrong.
    /// </summary>
    public async Task<(int Hidden, int Reminded)> CertificateSweepAsync(
        CancellationToken ct, DateOnly? asOf = null)
    {
        var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        int hidden = 0, reminded = 0;

        var watched = await db.ServiceOfferings
            .Include(o => o.Host!).ThenInclude(h => h.User)
            .Where(o => o.CertificateExpiresOn != null && o.IsPublished)
            .Take(500)
            .ToListAsync(ct);

        foreach (var o in watched)
        {
            if (ServiceRules.CertificateLapsed(o.CertificateExpiresOn, today))
            {
                o.IsPublished = false;
                o.HiddenByExpiredCertificate = true;
                hidden++;

                if (o.Host?.User is { } owner)
                    await notifications.QueueWithEmailAsync(owner, NotificationKind.ListingRejected,
                        "Tin dịch vụ đã tạm ẩn",
                        $"{o.CertificateName ?? "Chứng chỉ hành nghề"} của \"{o.Title}\" đã hết hạn ngày " +
                        $"{o.CertificateExpiresOn:dd/MM/yyyy}, nên tin tạm ẩn khỏi tìm kiếm. " +
                        "Gia hạn rồi nộp lại là tin hiện lại.",
                        "/hosting", ct);
                continue;
            }

            // One reminder per certificate, not one a day.
            if (ServiceRules.CertificateExpiringSoon(o.CertificateExpiresOn, today)
                && o.CertificateReminderSentOn != o.CertificateExpiresOn)
            {
                o.CertificateReminderSentOn = o.CertificateExpiresOn;
                reminded++;

                if (o.Host?.User is { } owner)
                    await notifications.QueueWithEmailAsync(owner, NotificationKind.ListingApproved,
                        "Chứng chỉ sắp hết hạn",
                        $"{o.CertificateName ?? "Chứng chỉ hành nghề"} của \"{o.Title}\" hết hạn ngày " +
                        $"{o.CertificateExpiresOn:dd/MM/yyyy}. Gia hạn trước ngày đó để tin không bị tạm ẩn.",
                        "/hosting", ct);
            }
        }

        if (hidden + reminded > 0)
        {
            await db.SaveChangesAsync(ct);
            log.LogInformation("Chứng chỉ dịch vụ {Today}: {Hidden} tin tạm ẩn, {Reminded} lời nhắc.",
                today, hidden, reminded);
        }

        return (hidden, reminded);
    }

    public async Task<ServiceBookingDto?> BookingDtoAsync(int id, CancellationToken ct) =>
        await db.ServiceBookings
            .Where(b => b.Id == id)
            .Select(b => new ServiceBookingDto(
                b.Id, b.Reference, b.OfferingId, b.Offering!.Title, b.Offering.Slug,
                b.Offering.Category, b.Offering.City,
                b.StartsAt, b.DurationMinutes, b.Quantity,
                ServiceRules.UnitLabel(b.Offering.Pricing),
                b.Address, b.Note,
                b.Subtotal, b.ServiceFee, b.Tax, b.Total, b.RefundedAmount,
                b.Status.ToString(), ServiceRules.StatusLabel(b.Status), ServiceRules.StatusBadge(b.Status),
                b.CancelReason, b.CreatedAt,
                db.ServiceReviews.Any(r => r.BookingId == b.Id)))
            .FirstOrDefaultAsync(ct);

    /* --------------------------------------------------- docs/09 §5, the review */

    /// <summary>
    /// docs/09 §5 — a service is scored on four headings of its own. The
    /// offering's headline rating is recomputed from the reviews themselves
    /// rather than nudged, so the number on the card is always the average of
    /// what is written underneath it.
    /// </summary>
    public async Task<string?> WriteReviewAsync(
        User user, int bookingId, SubmitServiceReviewRequest req, CancellationToken ct)
    {
        var booking = await db.ServiceBookings
            .Include(b => b.Offering)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.GuestUserId == user.Id, ct);
        if (booking?.Offering is null) return "Không tìm thấy đơn dịch vụ này.";

        if (!ServiceReviews.CanReview(booking, DateTime.UtcNow))
            return booking.Status is ServiceBookingStatus.Confirmed or ServiceBookingStatus.Completed
                ? "Buổi này chưa kết thúc nên chưa đánh giá được."
                : "Đơn đã huỷ thì không đánh giá được.";

        int[] scores = [req.Skill, req.AsDescribed, req.Punctuality, req.Value];
        if (scores.Any(s => !ServiceReviews.ScoreInRange(s)))
            return "Mỗi tiêu chí chấm từ 1 đến 5 sao.";

        if (await db.ServiceReviews.AnyAsync(r => r.BookingId == bookingId, ct))
            return "Bạn đã đánh giá đơn này rồi.";

        db.ServiceReviews.Add(new ServiceReview
        {
            BookingId = booking.Id,
            OfferingId = booking.OfferingId,
            AuthorUserId = user.Id,
            SkillScore = req.Skill,
            AsDescribedScore = req.AsDescribed,
            PunctualityScore = req.Punctuality,
            ValueScore = req.Value,
            Comment = (req.Comment ?? "").Trim()
        });
        await db.SaveChangesAsync(ct);

        var all = await db.ServiceReviews
            .Where(r => r.OfferingId == booking.OfferingId)
            .Select(r => new { r.SkillScore, r.AsDescribedScore, r.PunctualityScore, r.ValueScore })
            .ToListAsync(ct);

        var offering = booking.Offering;
        offering.ReviewCount = all.Count;
        offering.Rating = all.Count == 0
            ? 0
            : Math.Round(all.Average(r => ServiceReviews.Average(
                r.SkillScore, r.AsDescribedScore, r.PunctualityScore, r.ValueScore)), 2);

        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>What people who actually had the job done wrote, newest first.</summary>
    public async Task<IReadOnlyList<ServiceReviewDto>> ReviewsAsync(int offeringId, CancellationToken ct) =>
        await db.ServiceReviews
            .Where(r => r.OfferingId == offeringId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Select(r => new ServiceReviewDto(
                r.Id,
                r.AuthorUser!.DisplayName ?? r.AuthorUser.FullName,
                r.AuthorUser.AvatarUrl,
                r.SkillScore, r.AsDescribedScore, r.PunctualityScore, r.ValueScore,
                r.Comment, r.CreatedAt))
            .ToListAsync(ct);
}
