using StayHost.Domain;

namespace StayHost.Domain.Tests;

/// <summary>docs/01 MR-05 → MR-07 — services booked by the slot, at an address.</summary>
public class ServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static ServiceOffering Make(
        ServicePricing pricing = ServicePricing.PerSession, decimal price = 1_000_000m,
        bool travels = true, int radius = 10, bool partner = false, decimal commission = 0.15m) =>
        new()
        {
            Id = 1, Title = "Đầu bếp tại nhà", City = "Đà Nẵng", Country = "Việt Nam",
            Pricing = pricing, BasePrice = price, MinQuantity = 1, MaxQuantity = 8,
            DurationMinutes = 120, TravelsToGuest = travels, ServiceRadiusKm = radius,
            Latitude = 16.0544, Longitude = 108.2022, OpensAtHour = 8, ClosesAtHour = 20,
            IsPartner = partner, CommissionRate = commission,
            // The hour arithmetic in these tests is written in UTC, and opening
            // hours are read on the provider's own clock, so the two only line up
            // if the provider keeps UTC. The Vietnamese default is exercised on
            // its own below.
            TimeZoneId = "UTC"
        };

    private static ServiceRules.Request Ask(
        ServiceOffering offering, int hoursAhead = 24, int quantity = 1,
        string? address = "12 Trần Phú, Đà Nẵng", double lat = 16.06, double lng = 108.21,
        params (DateTime From, DateTime To)[] busy) =>
        new()
        {
            Offering = offering,
            StartsAt = Now.AddHours(hoursAhead),
            Now = Now,
            Quantity = quantity,
            Address = address,
            Latitude = lat,
            Longitude = lng,
            // The diary jobs sit at the same place as this one unless a test says
            // otherwise, so only the buffer (not travel) shapes these cases.
            Busy = busy.Select(b => new ServiceRules.BusyJob(b.From, b.To, lat, lng)).ToArray()
        };

    /* ------------------------------------------------------------- MR-06 */

    [Fact]
    public void An_ordinary_slot_can_be_booked()
    {
        Assert.True(ServiceRules.CanBook(Ask(Make())).Ok);
    }

    [Fact]
    public void Nothing_can_be_booked_for_two_hours_from_now()
    {
        Assert.Equal(
            ServiceRules.Refusal.TooSoon,
            ServiceRules.CanBook(Ask(Make(), hoursAhead: 2)).Reason);
    }

    [Fact]
    public void A_slot_outside_working_hours_is_refused()
    {
        // 24 hours on takes us back to 08:00; 37 lands at 21:00, past closing.
        Assert.Equal(
            ServiceRules.Refusal.OutsideHours,
            ServiceRules.CanBook(Ask(Make(), hoursAhead: 37)).Reason);
    }

    [Fact]
    public void A_job_that_would_run_past_closing_time_is_refused()
    {
        // Starts at 19:00 and runs two hours, which ends an hour after closing.
        Assert.Equal(
            ServiceRules.Refusal.OutsideHours,
            ServiceRules.CanBook(Ask(Make(), hoursAhead: 35)).Reason);
    }

    [Fact]
    public void Two_jobs_need_a_buffer_between_them()
    {
        var start = Now.AddHours(24);

        // Overlapping is refused outright.
        Assert.Equal(
            ServiceRules.Refusal.AlreadyBooked,
            ServiceRules.CanBook(Ask(Make(), busy: (start.AddMinutes(60), start.AddMinutes(180)))).Reason);

        // docs/09 §3.4 — butting straight up against the next job (this one ends at
        // +120, the next starts at +120) leaves no rest/clean-up buffer.
        Assert.Equal(
            ServiceRules.Refusal.TooTight,
            ServiceRules.CanBook(Ask(Make(), busy: (start.AddMinutes(120), start.AddMinutes(240)))).Reason);

        // A comfortable gap at the same place is fine: the 30-minute buffer is met
        // and there is no travel between two jobs at one address.
        Assert.True(ServiceRules.CanBook(Ask(Make(), busy: (start.AddMinutes(180), start.AddMinutes(300)))).Ok);
    }

    [Fact]
    public void A_job_too_far_to_reach_in_the_gap_is_blocked()   // scenario 8
    {
        var start = Now.AddHours(24);

        // A job 30 minutes after this one ends, but ~20 km away (0.18° of latitude).
        // At 25 km/h that is ~48 minutes of travel — plus the buffer, far more than
        // the 30-minute gap, so the chef cannot make it.
        var farBusy = new ServiceRules.BusyJob(start.AddMinutes(150), start.AddMinutes(270), 16.06 + 0.18, 108.21);
        var req = new ServiceRules.Request
        {
            Offering = Make(), StartsAt = start, Now = Now, Quantity = 1,
            Address = "12 Trần Phú, Đà Nẵng", Latitude = 16.06, Longitude = 108.21,
            Busy = [farBusy]
        };

        Assert.Equal(ServiceRules.Refusal.TooTight, ServiceRules.CanBook(req).Reason);

        // The same job right next door (same coordinates) clears with only the buffer.
        var nearBusy = new ServiceRules.BusyJob(start.AddMinutes(150), start.AddMinutes(270), 16.06, 108.21);
        Assert.True(ServiceRules.CanBook(req with { Busy = [nearBusy] }).Ok);
    }

    /* ------------------------------------------------------------- MR-05 */

    [Fact]
    public void An_address_beyond_the_service_radius_is_refused()
    {
        // Hội An is about 25 km from Đà Nẵng, well past a 10 km radius.
        var check = ServiceRules.CanBook(Ask(Make(radius: 10), lat: 15.8801, lng: 108.3380));

        Assert.Equal(ServiceRules.Refusal.OutOfRange, check.Reason);
        Assert.Contains("10 km", check.Message);
    }

    [Fact]
    public void The_same_address_is_fine_for_a_provider_who_travels_further()
    {
        Assert.True(ServiceRules.CanBook(Ask(Make(radius: 40), lat: 15.8801, lng: 108.3380)).Ok);
    }

    [Fact]
    public void A_provider_who_travels_needs_somewhere_to_go()
    {
        Assert.Equal(
            ServiceRules.Refusal.NoAddress,
            ServiceRules.CanBook(Ask(Make(), address: null)).Reason);
    }

    [Fact]
    public void A_provider_who_does_not_travel_needs_no_address_at_all()
    {
        Assert.True(ServiceRules.CanBook(Ask(Make(travels: false), address: null)).Ok);
    }

    [Fact]
    public void Distance_between_two_known_cities_is_about_right()
    {
        var km = ServiceRules.DistanceKm(16.0544, 108.2022, 15.8801, 108.3380);

        Assert.InRange(km, 20, 32);
    }

    [Fact]
    public void A_quantity_outside_what_the_provider_takes_is_refused()
    {
        Assert.Equal(
            ServiceRules.Refusal.QuantityOutOfRange,
            ServiceRules.CanBook(Ask(Make(), quantity: 20)).Reason);
    }

    /* ---------------------------------------------------------- pricing */

    [Fact]
    public void A_flat_rate_visit_is_one_price_however_many_are_named()
    {
        var one = Pricing.QuoteService(new Pricing.ServiceRequest
        { Offering = Make(), Quantity = 1, StartsAt = Now.AddDays(1) });
        var four = Pricing.QuoteService(new Pricing.ServiceRequest
        { Offering = Make(), Quantity = 4, StartsAt = Now.AddDays(1) });

        Assert.Equal(one.Subtotal, four.Subtotal);
    }

    [Fact]
    public void An_hourly_service_multiplies()
    {
        var price = Pricing.QuoteService(new Pricing.ServiceRequest
        {
            Offering = Make(ServicePricing.PerHour, 500_000m), Quantity = 3, StartsAt = Now.AddDays(1)
        });

        Assert.Equal(1_500_000m, price.Subtotal);
        Assert.Equal(0m, price.GuestServiceFee);        // docs/09 §3.3 — no guest service fee on services
        Assert.Equal(1_500_000m, price.Total);
    }

    [Fact]
    public void A_chef_massage_or_trainer_job_needs_its_mandatory_note()
    {
        // docs/09 §3.5 (scenario 10) — the safety note is required for these…
        Assert.True(ServiceRules.NoteMissing("chef", null));
        Assert.True(ServiceRules.NoteMissing("chef", "   "));
        Assert.True(ServiceRules.NoteMissing("massage", ""));
        Assert.True(ServiceRules.NoteMissing("fitness", null));
        Assert.False(ServiceRules.NoteMissing("chef", "Dị ứng hải sản"));

        // …and optional for everything else.
        Assert.False(ServiceRules.NoteMissing("photo", null));
        Assert.False(ServiceRules.NoteMissing("transfer", null));
        Assert.Equal(ServiceRules.NoteKind.FoodAllergy, ServiceRules.RequiredNote("chef"));
    }

    [Fact]
    public void A_lapsed_practising_certificate_hides_the_listing()   // scenario 9
    {
        var today = new DateOnly(2026, 9, 1);

        // docs/09 §3.2 — expired yesterday: down it comes.
        Assert.True(ServiceRules.CertificateLapsed(today.AddDays(-1), today));
        // Expiring today is still valid; the provider has the day.
        Assert.False(ServiceRules.CertificateLapsed(today, today));
        // No certificate on file means the category never demanded one.
        Assert.False(ServiceRules.CertificateLapsed(null, today));

        // Warned thirty days out, not before, and not once it has already lapsed.
        Assert.True(ServiceRules.CertificateExpiringSoon(today.AddDays(30), today));
        Assert.False(ServiceRules.CertificateExpiringSoon(today.AddDays(31), today));
        Assert.False(ServiceRules.CertificateExpiringSoon(today.AddDays(-1), today));

        Assert.True(ServiceRules.NeedsCertificate("massage"));
        Assert.False(ServiceRules.NeedsCertificate("luggage"));
    }

    /* ------------------------------------- docs/09 §3.3–§3.4 (MR-S-03..07) */

    [Fact]
    public void Past_the_free_radius_a_provider_may_charge_instead_of_refusing()
    {
        var flat = Make(radius: 10);                      // no travel fee set
        var willing = Make(radius: 10);
        willing.TravelFeePerKm = 15_000m;
        willing.MaxTravelKm = 20;

        // Inside the radius nothing changes for either.
        Assert.True(ServiceRules.WillTravelTo(flat, 8));
        Assert.Equal(0m, ServiceRules.TravelFee(willing, 8));

        // docs/09 §3.3 — the one who never set a fee still refuses…
        Assert.False(ServiceRules.WillTravelTo(flat, 18));
        // …the one who did will go, and charges for the extra 8 km only.
        Assert.True(ServiceRules.WillTravelTo(willing, 18));
        Assert.Equal(120_000m, ServiceRules.TravelFee(willing, 18));

        // Past what they are willing to do, it is a refusal again.
        Assert.False(ServiceRules.WillTravelTo(willing, 31));
    }

    [Fact]
    public void A_provider_works_the_days_they_said_and_no_others()
    {
        var o = Make();
        o.WorkingDaysMask = 0b0011111;                    // Monday–Friday

        Assert.True(ServiceRules.WorksOn(o, DayOfWeek.Monday));
        Assert.True(ServiceRules.WorksOn(o, DayOfWeek.Friday));
        Assert.False(ServiceRules.WorksOn(o, DayOfWeek.Saturday));
        Assert.False(ServiceRules.WorksOn(o, DayOfWeek.Sunday));

        // The default is every day, so an offering that never said keeps working.
        Assert.True(ServiceRules.WorksOn(Make(), DayOfWeek.Sunday));
    }

    [Fact]
    public void A_job_with_conditions_cannot_be_booked_until_the_guest_confirms_them()
    {
        var o = Make();
        o.OnSiteRequirements = "Có bếp nấu được\nBàn cho 6 người";

        // docs/09 §3.3 (MR-S-07) — this is what makes DV-D fair: they were asked.
        Assert.True(ServiceRules.ConditionsUnconfirmed(o, confirmed: false));
        Assert.False(ServiceRules.ConditionsUnconfirmed(o, confirmed: true));
        Assert.Equal(2, o.RequirementList.Count);

        // A service that asks for nothing never blocks on this.
        Assert.False(ServiceRules.ConditionsUnconfirmed(Make(), confirmed: false));

        Assert.Equal(
            ServiceRules.Refusal.ConditionsNotConfirmed,
            ServiceRules.CanBook(Ask(o)).Reason);
        Assert.True(ServiceRules.CanBook(Ask(o) with { ConditionsConfirmed = true }).Ok);
    }

    [Fact]
    public void Extras_and_the_journey_are_part_of_what_is_being_sold()
    {
        var o = Make(ServicePricing.PerSession, 1_000_000m);
        o.TravelFeePerKm = 10_000m;
        o.MaxTravelKm = 20;

        var price = Pricing.QuoteService(new Pricing.ServiceRequest
        {
            Offering = o, Quantity = 1, StartsAt = Now.AddDays(1),
            AddOns = [new ServiceAddOn { Id = 1, Name = "Thực đơn 5 món", Price = 300_000m }],
            DistanceKm = 15                                  // 5 km past the radius
        });

        Assert.Equal(300_000m, price.AddOnsTotal);
        Assert.Equal(50_000m, price.TravelFee);
        // docs/09 §3.3 — both sit in the subtotal, so the 15% provider fee follows.
        Assert.Equal(1_350_000m, price.Subtotal);
        Assert.Equal(202_500m, price.PlatformCut);
        Assert.Equal(price.Total, price.Lines.Sum(l => l.Amount));

        // Each extra is shown, not folded quietly into the base line.
        Assert.Contains(price.Lines, l => l.Label == "Thực đơn 5 món");
        Assert.Contains(price.Lines, l => l.Key == "travel-fee");
    }

    /* ------------------------------------------------------------- MR-07 */

    [Fact]
    public void A_partner_job_pays_commission_where_a_host_job_pays_the_host_fee()
    {
        var own = Pricing.QuoteService(new Pricing.ServiceRequest
        { Offering = Make(), Quantity = 1, StartsAt = Now.AddDays(1) });
        var partner = Pricing.QuoteService(new Pricing.ServiceRequest
        { Offering = Make(partner: true, commission: 0.18m), Quantity = 1, StartsAt = Now.AddDays(1) });

        Assert.Equal(150_000m, own.PlatformCut);       // docs/09 §3.3 — 15% provider fee (DV-F)
        Assert.Equal(180_000m, partner.PlatformCut);   // 18% commission

        // The guest pays the same either way; only the split behind it changes.
        Assert.Equal(own.Total, partner.Total);
        Assert.Equal(850_000m, own.ProviderPayout);
        Assert.Equal(820_000m, partner.ProviderPayout);
    }

    [Fact]
    public void The_lines_shown_add_up_to_the_total()
    {
        var price = Pricing.QuoteService(new Pricing.ServiceRequest
        {
            Offering = Make(ServicePricing.PerPerson, 400_000m), Quantity = 3, StartsAt = Now.AddDays(1),
            TaxRules = [new TaxRule
            {
                Id = 1, Country = "Việt Nam", Name = "VAT",
                Method = TaxMethod.Percentage, Value = 0.08m, Base = TaxBase.Subtotal
            }]
        });

        Assert.Equal(price.Total, price.Lines.Sum(l => l.Amount));
    }

    /* --------------------------------------------------------- refunds */

    [Fact]
    public void Capturing_and_refunding_a_job_leaves_every_account_flat()
    {
        var price = Pricing.QuoteService(new Pricing.ServiceRequest
        {
            Offering = Make(partner: true), Quantity = 1, StartsAt = Now.AddDays(1),
            TaxRules = [new TaxRule
            {
                Id = 1, Country = "Việt Nam", Name = "VAT",
                Method = TaxMethod.Percentage, Value = 0.08m, Base = TaxBase.Subtotal
            }]
        });

        var booking = new ServiceBooking
        {
            Id = 1, Reference = "SV1",
            Subtotal = price.Subtotal, ServiceFee = price.GuestServiceFee, Tax = price.Tax,
            Total = price.Total, PlatformCut = price.PlatformCut, ProviderPayout = price.ProviderPayout
        };

        var captured = Ledger.CaptureService(booking, Now);
        var refunded = Ledger.RefundService(booking, booking.Total, Now);

        Assert.Equal(0m, Ledger.Imbalance(captured));
        Assert.Equal(0m, Ledger.Imbalance(refunded));

        foreach (var account in Enum.GetValues<LedgerAccount>())
            Assert.Equal(0m, captured.Concat(refunded).Where(e => e.Account == account).Sum(e => e.Signed));
    }

    [Fact]
    public void The_service_cancellation_ladder_is_its_own()
    {
        var booking = new ServiceBooking { Total = 1_140_000m, StartsAt = Now.AddHours(73) };

        // docs/09 §3.6 — everything back at least 72 hours out (DV-C)…
        Assert.Equal(1_140_000m, ServiceRules.GuestRefund(booking, Now));

        // …half between 24 and 72 hours…
        booking.StartsAt = Now.AddHours(25);
        Assert.Equal(570_000m, ServiceRules.GuestRefund(booking, Now));

        // …and nothing inside the last day: the ingredients are bought by then.
        booking.StartsAt = Now.AddHours(23);
        Assert.Equal(0m, ServiceRules.GuestRefund(booking, Now));
    }

    [Fact]
    public void A_provider_who_finds_the_conditions_misdeclared_still_gets_half()
    {
        // docs/09 §3.6 (DV-D) — the chef who arrives to find no kitchen travelled
        // and turned other work away, so they are not left with nothing.
        Assert.Equal(570_000m, ServiceRules.ProviderShareOnMisdeclared(1_140_000m));
        Assert.Equal(0m, ServiceRules.ProviderShareOnMisdeclared(0m));
    }

    [Fact]
    public void Opening_hours_are_read_on_the_providers_clock_not_on_UTC()
    {
        // docs/09 §3.4 — a chef in Đà Nẵng who takes work from 9:00 means nine in
        // the morning where they live. Instants travel as UTC, so a 10:00 job
        // arrives as 03:00Z: judged against the raw UTC hour it was refused as
        // "outside working hours", and every time the picker offered was a time
        // the server would not take.
        var o = Make();
        o.TimeZoneId = "Asia/Ho_Chi_Minh";
        o.OpensAtHour = 9;
        o.ClosesAtHour = 21;
        o.DurationMinutes = 90;

        // 03:00Z on a Wednesday is 10:00 on Wednesday in Đà Nẵng: inside hours.
        var tenLocal = new DateTime(2026, 9, 2, 3, 0, 0, DateTimeKind.Utc);
        Assert.Equal(10, ServiceRules.LocalTime(o, tenLocal).Hour);
        Assert.True(ServiceRules.CanBook(Ask(o) with { StartsAt = tenLocal, Now = tenLocal.AddDays(-2) }).Ok);

        // 22:00Z is 05:00 the next morning there, which is before they open —
        // and it is a different weekday, which the day rule has to see too.
        var beforeOpening = new DateTime(2026, 9, 2, 22, 0, 0, DateTimeKind.Utc);
        Assert.Equal(5, ServiceRules.LocalTime(o, beforeOpening).Hour);
        Assert.Equal(DayOfWeek.Thursday, ServiceRules.LocalTime(o, beforeOpening).DayOfWeek);
        Assert.Equal(ServiceRules.Refusal.OutsideHours,
            ServiceRules.CanBook(Ask(o) with { StartsAt = beforeOpening, Now = beforeOpening.AddDays(-2) }).Reason);

        // A provider who works Mondays only is shut at midday on their Sunday.
        // The hour is deliberately inside opening hours, so only the weekday rule
        // can be doing the refusing — both rules answer OutsideHours, and the
        // sentence is the only thing that tells them apart.
        o.WorkingDaysMask = 1;
        var sundayNoon = new DateTime(2026, 9, 6, 5, 0, 0, DateTimeKind.Utc);
        var there = ServiceRules.LocalTime(o, sundayNoon);
        Assert.Equal(DayOfWeek.Sunday, there.DayOfWeek);
        Assert.Equal(12, there.Hour);
        Assert.Equal("Ngày này nhà cung cấp không nhận việc.",
            ServiceRules.CanBook(Ask(o) with { StartsAt = sundayNoon, Now = sundayNoon.AddDays(-2) }).Message);

        // …and open at the same hour on the Monday after it.
        Assert.True(ServiceRules.CanBook(
            Ask(o) with { StartsAt = sundayNoon.AddDays(1) }).Ok);

        // An id nobody can resolve must not take the listing off sale.
        o.TimeZoneId = "Mars/Olympus_Mons";
        Assert.Equal(tenLocal, ServiceRules.LocalTime(o, tenLocal));
    }

    [Fact]
    public void A_working_week_of_zero_reads_as_every_day_not_as_no_day()
    {
        // docs/09 §3.4 — the column arrived with defaultValue 0 on a table that
        // already had rows, so every service on sale at the time came out of the
        // migration working no day of the week: CanBook refused every date, the
        // picker offered nothing, and nothing anywhere said why.
        var o = Make();
        o.WorkingDaysMask = 0;

        foreach (var day in Enum.GetValues<DayOfWeek>())
            Assert.True(ServiceRules.WorksOn(o, day));

        Assert.True(ServiceRules.CanBook(Ask(o)).Ok);

        // Out of range the other way is just as meaningless, and reads the same.
        o.WorkingDaysMask = 999;
        Assert.True(ServiceRules.WorksOn(o, DayOfWeek.Monday));

        // A week the provider really did choose is left exactly as they set it:
        // bit 0 is Monday, so 1 is Mondays only.
        o.WorkingDaysMask = 1;
        Assert.True(ServiceRules.WorksOn(o, DayOfWeek.Monday));
        Assert.False(ServiceRules.WorksOn(o, DayOfWeek.Tuesday));
        Assert.False(ServiceRules.WorksOn(o, DayOfWeek.Sunday));

        // …and Sunday is bit 6, the far end of the week rather than the near one.
        o.WorkingDaysMask = 1 << 6;
        Assert.True(ServiceRules.WorksOn(o, DayOfWeek.Sunday));
        Assert.False(ServiceRules.WorksOn(o, DayOfWeek.Monday));
    }

    /* ------------------------------------------------------- §5, the review */

    [Fact]
    public void A_service_is_judged_on_four_criteria_of_its_own()
    {
        // docs/09 §5 — four, and not the experience's four: nobody is being led,
        // and "tổ chức và an toàn" is not what a haircut is judged on.
        Assert.Equal(4, ServiceReviews.Criteria.Count);
        Assert.Equal(["skill", "asDescribed", "punctuality", "value"],
            ServiceReviews.Criteria.Select(c => c.Key).ToArray());
        Assert.NotEqual(
            ExperienceReviews.Criteria.Select(c => c.Key).ToArray(),
            ServiceReviews.Criteria.Select(c => c.Key).ToArray());

        Assert.Equal(4.5, ServiceReviews.Average(5, 4, 5, 4));
        Assert.True(ServiceReviews.ScoreInRange(1));
        Assert.True(ServiceReviews.ScoreInRange(5));
        Assert.False(ServiceReviews.ScoreInRange(0));
        Assert.False(ServiceReviews.ScoreInRange(6));
    }

    [Fact]
    public void Only_a_job_that_went_ahead_and_is_over_can_be_reviewed()
    {
        var booking = new ServiceBooking
        {
            StartsAt = Now, DurationMinutes = 120, Status = ServiceBookingStatus.Confirmed
        };
        var ends = booking.EndsAt;

        Assert.True(ServiceReviews.CanReview(booking, ends));
        Assert.True(ServiceReviews.CanReview(booking, ends.AddDays(1)));
        // A job still running is not something the guest can have an opinion of.
        Assert.False(ServiceReviews.CanReview(booking, ends.AddMinutes(-1)));

        // A completed job counts; a cancelled one never does, whoever cancelled it.
        booking.Status = ServiceBookingStatus.Completed;
        Assert.True(ServiceReviews.CanReview(booking, ends.AddDays(1)));

        booking.Status = ServiceBookingStatus.CancelledByGuest;
        Assert.False(ServiceReviews.CanReview(booking, ends.AddDays(1)));

        booking.Status = ServiceBookingStatus.CancelledByProvider;
        Assert.False(ServiceReviews.CanReview(booking, ends.AddDays(1)));

        // Still only a request, so nothing was received either.
        booking.Status = ServiceBookingStatus.Requested;
        Assert.False(ServiceReviews.CanReview(booking, ends.AddDays(1)));
    }
}
