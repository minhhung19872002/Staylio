namespace StayHost.Domain;

/// <summary>docs/01 MR-05 — how a service charges for itself.</summary>
public enum ServicePricing
{
    /// <summary>One price for the visit, however long it takes.</summary>
    PerSession = 0,
    PerHour = 1,
    PerPerson = 2,
    /// <summary>Per item, per bag, per trip — anything counted.</summary>
    PerOrder = 3
}

public enum ServiceBookingStatus
{
    Requested = 0,
    Confirmed = 1,
    Completed = 2,
    CancelledByGuest = 3,
    CancelledByProvider = 4,
    /// <summary>
    /// docs/07 §2.3 — booked by bank transfer, holding its place in the
    /// provider's day while the money makes its way over. Nothing captured, and
    /// the provider has not been told to turn up.
    /// </summary>
    AwaitingPayment = 5,
    /// <summary>The transfer window ran out and the slot went back.</summary>
    PaymentExpired = 6,

    /// <summary>
    /// docs/09 §3.6 (DV-D) — the provider turned up and the place was not what
    /// the guest declared: no kitchen for the chef, nowhere to set up. The job
    /// does not happen, but it is not a cancellation by either side and must not
    /// be recorded as one: the provider travelled and turned other work away.
    /// </summary>
    ConditionsMisdeclared = 7
}

/// <summary>
/// docs/00 §2 and docs/01 MR-05 — a chef, a photographer, an airport transfer.
/// Sold by the session rather than by the night, and often at the guest's
/// address rather than the provider's.
/// </summary>
public class ServiceOffering
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>chef, photo, massage, fitness, transfer, luggage, groceries, car…</summary>
    public string Category { get; set; } = "";

    public string City { get; set; } = "";
    public string Country { get; set; } = "Việt Nam";

    public int HostId { get; set; }
    public HostProfile? Host { get; set; }

    public string Summary { get; set; } = "";
    public string Description { get; set; } = "";

    public ServicePricing Pricing { get; set; } = ServicePricing.PerSession;
    public decimal BasePrice { get; set; }

    /// <summary>The smallest booking the provider will take, in whatever the price is per.</summary>
    public int MinQuantity { get; set; } = 1;
    public int MaxQuantity { get; set; } = 10;

    /// <summary>Roughly how long one booking blocks, so two do not collide.</summary>
    public int DurationMinutes { get; set; } = 120;

    /// <summary>docs/01 MR-05 — how far the provider will travel, and whether they travel at all.</summary>
    public bool TravelsToGuest { get; set; } = true;
    public int ServiceRadiusKm { get; set; } = 10;

    /// <summary>Where the provider is based; the radius is measured from here.</summary>
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>Hours of the day the provider takes bookings, in local time.</summary>
    public int OpensAtHour { get; set; } = 8;
    public int ClosesAtHour { get; set; } = 20;

    // --- docs/01 MR-07: run by somebody else, with the platform taking a cut.
    public bool IsPartner { get; set; }
    public string? PartnerName { get; set; }

    /// <summary>What the platform keeps from a partner booking. Ignored for a host's own service.</summary>
    public decimal CommissionRate { get; set; } = 0.15m;

    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public bool IsPublished { get; set; }

    // --- docs/09 §3.3 (MR-S-04): work outside the radius, charged for rather
    // than refused. Zero per km means "we do not travel further", which is the
    // old behaviour and stays the default.
    public decimal TravelFeePerKm { get; set; }
    /// <summary>How far past the free radius the provider will still go.</summary>
    public int MaxTravelKm { get; set; }

    // --- docs/09 §3.4 (MR-S-05): the working week and how tightly it may pack.
    /// <summary>Bitmask, Monday = 1 &lt;&lt; 0 … Sunday = 1 &lt;&lt; 6. 127 = every day.</summary>
    public int WorkingDaysMask { get; set; } = 127;
    /// <summary>
    /// docs/09 §3.5 — "đặt ngay hoặc chờ nhà cung cấp xác nhận — cho nhà cung cấp
    /// chọn". Off means instant booking, the behaviour before this existed.
    /// </summary>
    public bool RequiresConfirmation { get; set; }

    /// <summary>Rest and clean-up between two jobs; 0 falls back to the platform default.</summary>
    public int BufferMinutes { get; set; }
    /// <summary>0 means no daily cap.</summary>
    public int MaxJobsPerDay { get; set; }

    // --- docs/09 §3.3 (MR-S-07): what the place must have, one per line. The
    // guest confirms these before the job can be booked.
    public string OnSiteRequirements { get; set; } = "";

    // --- docs/09 §3.2 (MR-S-02): the practising certificate and its expiry.
    // A lapsed certificate takes the listing down on its own; the provider is
    // warned thirty days ahead, once.
    public string? CertificateName { get; set; }
    public DateOnly? CertificateExpiresOn { get; set; }
    public DateOnly? CertificateReminderSentOn { get; set; }

    /// <summary>Set when the sweep hid this listing, so it can say why.</summary>
    public bool HiddenByExpiredCertificate { get; set; }

    public double Rating { get; set; }
    public int ReviewCount { get; set; }

    public string SearchText { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ServiceImage> Images { get; set; } = [];

    /// <summary>docs/09 §3.3 (MR-S-03) — the paid extras this service offers.</summary>
    public List<ServiceAddOn> AddOns { get; set; } = [];

    /// <summary>The conditions the place must meet, as the guest sees them listed.</summary>
    public IReadOnlyList<string> RequirementList =>
        OnSiteRequirements.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void RefreshSearchText() =>
        SearchText = StayHost.Domain.SearchText.Normalize(
            string.Join(' ', Title, Category, City, Country, Summary, Description, PartnerName ?? ""));
}

/// <summary>
/// docs/09 §3.3 (MR-S-03) — an extra with a price of its own: a set menu, a
/// longer session, more edited photos. Added to the subtotal, so the platform
/// fee and the provider's share both follow it.
/// </summary>
public class ServiceAddOn
{
    public int Id { get; set; }
    public int OfferingId { get; set; }
    public ServiceOffering? Offering { get; set; }

    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>One extra chosen on one booking, priced as it was on the day.</summary>
public class ServiceBookingAddOn
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public ServiceBooking? Booking { get; set; }

    public int AddOnId { get; set; }
    public ServiceAddOn? AddOn { get; set; }

    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

/// <summary>
/// docs/09 §5 — "Tiêu chí đánh giá … dịch vụ: 4 mục riêng". A stay is scored on
/// six headings and an experience on four of its own; a service is scored on
/// four again, but not the experience's four. There is no "người dẫn" when
/// nobody is being led and no "tổ chức và an toàn" for a haircut — what a guest
/// can actually judge is the craft, whether it matched the advert, whether the
/// provider turned up on time, and whether it was worth the money.
/// </summary>
public class ServiceReview
{
    public int Id { get; set; }

    public int BookingId { get; set; }
    public ServiceBooking? Booking { get; set; }

    public int OfferingId { get; set; }
    public ServiceOffering? Offering { get; set; }

    public int AuthorUserId { get; set; }
    public User? AuthorUser { get; set; }

    /// <summary>Tay nghề.</summary>
    public int SkillScore { get; set; }
    /// <summary>Đúng như mô tả.</summary>
    public int AsDescribedScore { get; set; }
    /// <summary>Đúng giờ.</summary>
    public int PunctualityScore { get; set; }
    /// <summary>Đáng giá tiền.</summary>
    public int ValueScore { get; set; }

    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public double Average =>
        ServiceReviews.Average(SkillScore, AsDescribedScore, PunctualityScore, ValueScore);
}

/// <summary>The four headings a service is judged on, and who may use them.</summary>
public static class ServiceReviews
{
    public static readonly IReadOnlyList<(string Key, string Label)> Criteria =
    [
        ("skill", "Tay nghề"),
        ("asDescribed", "Đúng như mô tả"),
        ("punctuality", "Đúng giờ"),
        ("value", "Đáng giá tiền")
    ];

    public static bool ScoreInRange(int score) => score is >= 1 and <= 5;

    public static double Average(int skill, int asDescribed, int punctuality, int value) =>
        Math.Round((skill + asDescribed + punctuality + value) / 4.0, 2);

    /// <summary>
    /// A service has no register the way an experience does — nobody marks a
    /// haircut present — so the test is the booking itself: it must be one that
    /// went ahead, and the job must already be over. A cancelled job is not
    /// something the guest received, and a job still to come is not something
    /// they can have an opinion of yet.
    /// </summary>
    public static bool CanReview(ServiceBooking booking, DateTime now) =>
        now >= booking.EndsAt
        && booking.Status is ServiceBookingStatus.Confirmed or ServiceBookingStatus.Completed;
}

public class ServiceImage
{
    public int Id { get; set; }
    public int OfferingId { get; set; }
    public ServiceOffering? Offering { get; set; }
    public string Url { get; set; } = "";
    public int SortOrder { get; set; }
}

public class ServiceBooking
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";

    public int OfferingId { get; set; }
    public ServiceOffering? Offering { get; set; }

    public int GuestUserId { get; set; }
    public User? GuestUser { get; set; }

    public DateTime StartsAt { get; set; }
    public int DurationMinutes { get; set; }

    /// <summary>Hours, people or items — whatever this service charges by.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>docs/01 MR-06 — where the work happens.</summary>
    public string Address { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Note { get; set; }

    public decimal Subtotal { get; set; }
    public decimal ServiceFee { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }

    /// <summary>docs/09 §3.3 — the extras chosen, and what they came to.</summary>
    public decimal AddOnsTotal { get; set; }
    public List<ServiceBookingAddOn> AddOns { get; set; } = [];

    /// <summary>docs/09 §3.3 (MR-S-04) — charged when the address is past the free radius.</summary>
    public decimal TravelFee { get; set; }

    /// <summary>docs/09 §3.3 (MR-S-07) — the guest said the place has what it needs.</summary>
    public bool ConditionsConfirmed { get; set; }

    /// <summary>What the platform keeps: the host fee, or the commission on a partner job.</summary>
    public decimal PlatformCut { get; set; }
    public decimal ProviderPayout { get; set; }

    public ServiceBookingStatus Status { get; set; } = ServiceBookingStatus.Confirmed;
    public decimal RefundedAmount { get; set; }
    public string? CancelReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// docs/09 §3.5 — a paid job waiting on the provider's yes. Past this, the
    /// sweep declines it for them and the guest gets everything back.
    /// </summary>
    public DateTime? RespondBy { get; set; }

    // docs/09 §4 (MR-C-03) — the provider is paid a day after the session ends.
    public PayoutStatus PayoutStatus { get; set; } = PayoutStatus.Scheduled;
    public DateTime? PaidOutAt { get; set; }
    public string? PayoutReference { get; set; }

    public DateTime EndsAt => StartsAt.AddMinutes(DurationMinutes);
}

/// <summary>The rules a service booking obeys, kept where they can be tested.</summary>
public static class ServiceRules
{
    /// <summary>
    /// docs/09 §3.6 — the service cancellation ladder, its own and not the stay
    /// policy: everything back three days out (DV-C), half inside that, and
    /// nothing in the last day because by then the provider has bought the
    /// ingredients and turned other work away.
    /// </summary>
    public static readonly TimeSpan FullRefundLead = TimeSpan.FromHours(72);
    public static readonly TimeSpan HalfRefundLead = TimeSpan.FromHours(24);

    /// <summary>
    /// docs/09 §3.6 (DV-D) — what the provider still receives when they arrive and
    /// the on-site conditions were not what the guest declared: no kitchen, no
    /// room to work. They travelled and turned other work away, so they are not
    /// left with nothing.
    /// </summary>
    public const decimal MisdeclaredShare = 0.50m;

    public static decimal ProviderShareOnMisdeclared(decimal total) =>
        Math.Round(total * MisdeclaredShare, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// What goes back to the guest when they misdeclared the site: everything
    /// the provider's half does not account for.
    ///
    /// The reading of DV-D here is that <see cref="MisdeclaredShare"/> of the
    /// order value stays on the provider's side of the deal and settles the
    /// ordinary way — the provider keeps their payout share of it, the platform
    /// its cut, the state its tax. If the customer meant the provider should
    /// net 50% of the order value after the platform cut, this is the one line
    /// to change, and <c>ServiceRulesTests</c> will show what moves with it.
    /// </summary>
    public static decimal GuestRefundOnMisdeclared(decimal total) =>
        Math.Max(0m, total - ProviderShareOnMisdeclared(total));

    /// <summary>Kept for the older call sites that ask "is this still free?".</summary>
    public static readonly TimeSpan FreeCancellation = TimeSpan.FromHours(72);

    /// <summary>
    /// docs/09 §7 DV-B — "Đóng đặt dịch vụ trước 24 giờ", settled by the
    /// customer. This read four hours, so a chef could be booked for tonight.
    /// </summary>
    public static readonly TimeSpan MinimumNotice = TimeSpan.FromHours(24);

    public enum Refusal
    {
        None = 0,
        TooSoon,
        OutsideHours,
        OutOfRange,
        QuantityOutOfRange,
        AlreadyBooked,
        NoAddress,
        DoesNotTravel,
        TooTight,
        ConditionsNotConfirmed,
        DayFull
    }

    /// <summary>A job already on the provider's diary, with where it happens.</summary>
    public sealed record BusyJob(DateTime From, DateTime To, double Lat, double Lng);

    /* ------------------------------------------ §3.3, distance and its price */

    /// <summary>
    /// docs/09 §3.3 (MR-S-04) — outside the free radius the spec offers a choice:
    /// refuse, or charge for the journey. A provider who set a per-km fee has
    /// chosen the second, up to however far they are willing to go.
    /// </summary>
    public static bool WillTravelTo(ServiceOffering o, double km) =>
        km <= o.ServiceRadiusKm || (o.TravelFeePerKm > 0 && km <= o.ServiceRadiusKm + o.MaxTravelKm);

    /// <summary>What the journey past the free radius costs; zero inside it.</summary>
    public static decimal TravelFee(ServiceOffering o, double km)
    {
        var extra = km - o.ServiceRadiusKm;
        return extra <= 0 || o.TravelFeePerKm <= 0
            ? 0m
            : Math.Round((decimal)extra * o.TravelFeePerKm, 0, MidpointRounding.AwayFromZero);
    }

    /* -------------------------------------------- §3.4, the working week */

    /// <summary>
    /// docs/09 §3.4 — the working week, normalised before anyone reads it.
    ///
    /// Zero does not mean "works no day". It is what the column defaulted to when
    /// the field was added to a table that already had rows in it, and a provider
    /// who works no day at all is a provider nobody can ever book: WorksOn says no
    /// to every date, so CanBook refuses every request and the slot picker offers
    /// nothing — with no error anywhere to say why. Anything outside 1–127 reads
    /// as the whole week, which is also what SaveOfferingAsync stores when the
    /// provider leaves the question blank.
    /// </summary>
    public static int WorkingDays(int mask) => mask is > 0 and < 128 ? mask : 127;

    /// <summary>docs/09 §3.4 — whether the provider works that weekday at all.</summary>
    public static bool WorksOn(ServiceOffering o, DayOfWeek day)
    {
        // Monday is bit 0, so Sunday (DayOfWeek 0) sits at the end of the week.
        var bit = day == DayOfWeek.Sunday ? 6 : (int)day - 1;
        return (WorkingDays(o.WorkingDaysMask) & (1 << bit)) != 0;
    }

    /// <summary>
    /// docs/09 §3.4 — "khung giờ nhận việc theo từng thứ trong tuần". A chef who
    /// says they work 9:00 to 21:00 means nine in the morning where they live,
    /// not nine UTC, and the weekday they work is their weekday. Instants are
    /// stored and compared in UTC everywhere else, so the conversion happens
    /// here, once, at the only place that reads a clock face rather than a
    /// moment. An unknown zone id falls back to UTC rather than throwing: a
    /// mistyped setting must not take a listing off sale.
    /// </summary>
    public static DateTime LocalTime(ServiceOffering o, DateTime instant)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(o.TimeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(instant, DateTimeKind.Utc), tz);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return instant;
        }
    }

    /// <summary>The provider's own gap between jobs, or the platform default.</summary>
    public static TimeSpan BufferFor(ServiceOffering o) =>
        o.BufferMinutes > 0 ? TimeSpan.FromMinutes(o.BufferMinutes) : BufferBetweenJobs;

    /* ------------------------------------------- §3.3, the place itself */

    /// <summary>
    /// docs/09 §3.3 (MR-S-07) — a job with conditions attached cannot be booked
    /// until the guest has said the place meets them. This is what makes the
    /// misdeclaration rule of §3.6 (DV-D) fair: they were asked.
    /// </summary>
    public static bool ConditionsUnconfirmed(ServiceOffering o, bool confirmed) =>
        o.RequirementList.Count > 0 && !confirmed;

    /// <summary>docs/09 §3.4 — the mandatory rest/clean-up gap between two jobs.</summary>
    public static readonly TimeSpan BufferBetweenJobs = TimeSpan.FromMinutes(30);

    /// <summary>A conservative city travel speed, used to turn distance into time.</summary>
    public const double TravelKmPerHour = 25;

    public static TimeSpan TravelTime(double km) => TimeSpan.FromHours(km / TravelKmPerHour);

    public readonly record struct Check(bool Ok, Refusal Reason, string Message)
    {
        public static Check Pass => new(true, Refusal.None, "");
        public static Check Fail(Refusal reason, string message) => new(false, reason, message);
    }

    public sealed record Request
    {
        public required ServiceOffering Offering { get; init; }
        public required DateTime StartsAt { get; init; }
        public required DateTime Now { get; init; }
        public required int Quantity { get; init; }
        public string? Address { get; init; }
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        /// <summary>Jobs already on this provider's diary, with their locations.</summary>
        public IReadOnlyCollection<BusyJob> Busy { get; init; } = [];
        /// <summary>docs/09 §3.3 (MR-S-07) — the guest ticked the on-site conditions.</summary>
        public bool ConditionsConfirmed { get; init; }
    }

    public static Check CanBook(Request req)
    {
        var o = req.Offering;

        if (req.StartsAt - req.Now < MinimumNotice)
            return Check.Fail(Refusal.TooSoon,
                $"Cần đặt trước ít nhất {MinimumNotice.TotalHours:0} giờ.");

        // The working week and the opening hours are the provider's own clock,
        // so everything below reads this rather than the UTC instant.
        var local = LocalTime(o, req.StartsAt);

        // docs/09 §3.4 (MR-S-05) — a day the provider does not work at all.
        if (!WorksOn(o, local.DayOfWeek))
            return Check.Fail(Refusal.OutsideHours, "Ngày này nhà cung cấp không nhận việc.");

        // docs/09 §3.3 (MR-S-07) — asked before booking, so §3.6's DV-D is fair.
        if (ConditionsUnconfirmed(o, req.ConditionsConfirmed))
            return Check.Fail(Refusal.ConditionsNotConfirmed,
                "Cần xác nhận nơi thực hiện có đủ điều kiện trước khi đặt.");

        // docs/09 §3.4 — a provider's day has a ceiling on how many jobs fit.
        // "A day" is their day, so the other jobs are counted on their calendar.
        if (o.MaxJobsPerDay > 0
            && req.Busy.Count(b => LocalTime(o, b.From).Date == local.Date) >= o.MaxJobsPerDay)
            return Check.Fail(Refusal.DayFull,
                $"Ngày này đã kín ({o.MaxJobsPerDay} đơn).");

        var hour = local.Hour;
        var endHour = local.AddMinutes(o.DurationMinutes).Hour;
        if (hour < o.OpensAtHour || hour >= o.ClosesAtHour || (endHour > o.ClosesAtHour && endHour != 0))
            return Check.Fail(Refusal.OutsideHours,
                $"Dịch vụ này chỉ nhận từ {o.OpensAtHour}:00 đến {o.ClosesAtHour}:00.");

        if (req.Quantity < o.MinQuantity || req.Quantity > o.MaxQuantity)
            return Check.Fail(Refusal.QuantityOutOfRange,
                $"Nhận từ {o.MinQuantity} đến {o.MaxQuantity} {UnitLabel(o.Pricing)}.");

        if (o.TravelsToGuest)
        {
            if (string.IsNullOrWhiteSpace(req.Address))
                return Check.Fail(Refusal.NoAddress, "Cần địa chỉ nơi thực hiện dịch vụ.");

            if (req.Latitude is { } lat && req.Longitude is { } lng)
            {
                // docs/09 §3.3 (MR-S-04) — past the free radius the spec allows a
                // travel charge instead of a refusal, so only a provider who never
                // set one (or a distance past what they will do) is turned away.
                var km = DistanceKm(o.Latitude, o.Longitude, lat, lng);
                if (!WillTravelTo(o, km))
                    return Check.Fail(Refusal.OutOfRange,
                        o.TravelFeePerKm > 0
                            ? $"Xa quá phạm vi nhận việc (cách khoảng {km:0} km)."
                            : $"Ngoài phạm vi phục vụ {o.ServiceRadiusKm} km (cách khoảng {km:0} km).");
            }
        }

        var from = req.StartsAt;
        var to = req.StartsAt.AddMinutes(o.DurationMinutes);

        // Where the provider is for this job: at the guest's address if they travel,
        // otherwise at their own premises.
        var hereLat = o.TravelsToGuest && req.Latitude is { } jlat ? jlat : o.Latitude;
        var hereLng = o.TravelsToGuest && req.Longitude is { } jlng ? jlng : o.Longitude;

        foreach (var b in req.Busy)
        {
            if (from < b.To && b.From < to)
                return Check.Fail(Refusal.AlreadyBooked, "Khung giờ này đã có người đặt.");

            // docs/09 §3.4 (scenario 8) — between two jobs the provider needs a
            // buffer plus the time to travel between the two places. A job 30
            // minutes but 20 km away is too tight to reach.
            var gap = from >= b.To ? from - b.To : b.From - to;
            var travel = (b.Lat != 0 || b.Lng != 0) && (hereLat != 0 || hereLng != 0)
                ? TravelTime(DistanceKm(hereLat, hereLng, b.Lat, b.Lng))
                : TimeSpan.Zero;

            if (gap < BufferFor(o) + travel)
                return Check.Fail(Refusal.TooTight,
                    "Quá sát một đơn khác — không đủ thời gian nghỉ và di chuyển giữa hai buổi.");
        }

        return Check.Pass;
    }

    /// <summary>Great-circle distance in kilometres.</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371;

        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    /* ------------------------------------------------ §3.2, the certificate */

    /// <summary>docs/09 §3.2 — how long before expiry the provider is warned.</summary>
    public const int CertificateReminderDays = 30;

    /// <summary>
    /// docs/09 §3.2 (scenario 9) — a practising certificate that has run out
    /// takes the listing down: a masseur whose certificate lapsed is not somebody
    /// the platform may keep selling. An offering with no certificate on file is
    /// one whose category never demanded one, so it is left alone.
    /// </summary>
    public static bool CertificateLapsed(DateOnly? expiresOn, DateOnly today) =>
        expiresOn is { } on && on < today;

    /// <summary>Inside the warning window but still valid — time to remind them.</summary>
    public static bool CertificateExpiringSoon(DateOnly? expiresOn, DateOnly today) =>
        expiresOn is { } on && on >= today && on <= today.AddDays(CertificateReminderDays);

    /// <summary>Which categories may not be sold at all without a certificate (§3.2).</summary>
    public static bool NeedsCertificate(string? category) => (category ?? "").Trim().ToLowerInvariant()
        is "chef" or "massage" or "fitness" or "hair" or "makeup" or "nails";

    /// <summary>The mandatory note a category demands before a job can be booked.</summary>
    public enum NoteKind { None = 0, FoodAllergy, HealthConditions, FitnessInjuries }

    /// <summary>
    /// docs/09 §3.5 (scenario 10) — some categories cannot be booked without a
    /// note the provider needs for safety: a chef needs food allergies, massage
    /// needs health conditions and areas to avoid, a trainer needs goals and past
    /// injuries. Every other category leaves the note optional.
    /// </summary>
    public static NoteKind RequiredNote(string? category) => (category ?? "").Trim().ToLowerInvariant() switch
    {
        "chef" => NoteKind.FoodAllergy,
        "massage" => NoteKind.HealthConditions,
        "fitness" => NoteKind.FitnessInjuries,
        _ => NoteKind.None
    };

    public static string NoteLabel(NoteKind kind) => kind switch
    {
        NoteKind.FoodAllergy => "Dị ứng thực phẩm",
        NoteKind.HealthConditions => "Tình trạng sức khoẻ và vùng cần tránh",
        NoteKind.FitnessInjuries => "Mục tiêu tập luyện và chấn thương cũ",
        _ => ""
    };

    /// <summary>Whether this booking is missing a note its category makes mandatory.</summary>
    public static bool NoteMissing(string? category, string? note) =>
        RequiredNote(category) != NoteKind.None && string.IsNullOrWhiteSpace(note);

    /// <summary>
    /// docs/09 §3.6 — a guest cancelling their own job: 100% at least 72 hours
    /// out, 50% between 24 and 72 hours, nothing inside the last day.
    /// </summary>
    /// <summary>docs/09 §3.5 — how long a provider has to accept a job that waits on them.</summary>
    public static readonly TimeSpan ConfirmationWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// docs/09 §3.6 — "Nhà cung cấp huỷ bất cứ lúc nào: hoàn 100% + số dư đền
    /// bù + bị phạt". No DV parameter is given for the balance, so it is the
    /// same share the experience table fixes (TN-D).
    /// </summary>
    public static decimal ProviderCancelCredit(decimal total) =>
        Math.Round(total * ExperienceRules.ProviderCancelCreditRate, 0, MidpointRounding.AwayFromZero);

    public static decimal GuestRefund(ServiceBooking booking, DateTime now)
    {
        // A job the provider never accepted was never theirs to lose.
        if (booking.Status == ServiceBookingStatus.Requested) return booking.Total;

        var lead = booking.StartsAt - now;

        if (lead >= FullRefundLead) return booking.Total;
        if (lead >= HalfRefundLead)
            return Math.Round(booking.Total * 0.5m, 0, MidpointRounding.AwayFromZero);

        return 0m;
    }

    public static string UnitLabel(ServicePricing pricing) => pricing switch
    {
        ServicePricing.PerHour => "giờ",
        ServicePricing.PerPerson => "người",
        ServicePricing.PerOrder => "phần",
        _ => "buổi"
    };

    public static string PricingLabel(ServicePricing pricing) => pricing switch
    {
        ServicePricing.PerHour => "Tính theo giờ",
        ServicePricing.PerPerson => "Tính theo người",
        ServicePricing.PerOrder => "Tính theo phần",
        _ => "Trọn buổi"
    };

    public static string StatusLabel(ServiceBookingStatus status) => status switch
    {
        ServiceBookingStatus.Requested => "Chờ xác nhận",
        ServiceBookingStatus.Confirmed => "Đã xác nhận",
        ServiceBookingStatus.Completed => "Đã hoàn tất",
        ServiceBookingStatus.CancelledByGuest => "Khách đã huỷ",
        ServiceBookingStatus.AwaitingPayment => "Chờ thanh toán",
        ServiceBookingStatus.PaymentExpired => "Hết hạn chờ thanh toán",
        ServiceBookingStatus.ConditionsMisdeclared => "Không đủ điều kiện tại chỗ",
        _ => "Bên cung cấp đã huỷ"
    };

    public static string StatusBadge(ServiceBookingStatus status) => status switch
    {
        ServiceBookingStatus.Confirmed or ServiceBookingStatus.Completed => "confirmed",
        ServiceBookingStatus.Requested or ServiceBookingStatus.AwaitingPayment => "pending",
        _ => "cancelled"
    };
}
