using StayHost.Domain;

namespace StayHost.Domain.Tests;

/// <summary>docs/01 MR-01 → MR-04 — sessions sold by the seat.</summary>
public class ExperienceTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static Experience Make(decimal price = 500_000m, decimal? priv = null, int min = 2, int max = 8) =>
        new()
        {
            Id = 1, Title = "Lớp nấu ăn", City = "Hội An", Country = "Việt Nam",
            PricePerPerson = price, PrivateGroupPrice = priv, MinGuests = min, MaxGroup = max
        };

    private static ExperienceSlot Slot(int capacity = 8, int taken = 0, int hoursAhead = 72, bool priv = false) =>
        new()
        {
            Id = 1, ExperienceId = 1, Capacity = capacity, SeatsTaken = taken,
            StartsAt = Now.AddHours(hoursAhead), IsPrivate = priv
        };

    /* ------------------------------------------------------------- MR-02 */

    [Fact]
    public void A_seat_can_be_taken_while_there_is_room()
    {
        Assert.True(ExperienceRules.CanBook(Make(), Slot(taken: 5), 3, false, Now).Ok);
    }

    [Fact]
    public void One_seat_too_many_is_refused_with_the_number_left()
    {
        var check = ExperienceRules.CanBook(Make(), Slot(taken: 6), 3, false, Now);

        Assert.False(check.Ok);
        Assert.Equal(ExperienceRules.Refusal.NotEnoughSeats, check.Reason);
        Assert.Contains("2", check.Message);
    }

    [Fact]
    public void A_session_that_already_started_takes_nobody()
    {
        Assert.Equal(
            ExperienceRules.Refusal.AlreadyStarted,
            ExperienceRules.CanBook(Make(), Slot(hoursAhead: -1), 1, false, Now).Reason);
    }

    [Fact]
    public void Booking_closes_a_day_before_the_session()
    {
        Assert.Equal(
            ExperienceRules.Refusal.BookingClosed,
            ExperienceRules.CanBook(Make(), Slot(hoursAhead: 23), 1, false, Now).Reason);
        Assert.True(ExperienceRules.CanBook(Make(), Slot(hoursAhead: 25), 1, false, Now).Ok);
    }

    [Fact]
    public void A_cancelled_session_takes_nobody_either()
    {
        var slot = Slot();
        slot.Status = SlotStatus.Cancelled;

        Assert.Equal(
            ExperienceRules.Refusal.SlotCancelled,
            ExperienceRules.CanBook(Make(), slot, 1, false, Now).Reason);
    }

    /* ------------------------------------------------------------- MR-03 */

    [Fact]
    public void A_private_booking_needs_the_host_to_offer_one()
    {
        Assert.Equal(
            ExperienceRules.Refusal.PrivateNotOffered,
            ExperienceRules.CanBook(Make(priv: null), Slot(), 4, true, Now).Reason);

        Assert.True(ExperienceRules.CanBook(Make(priv: 3_000_000m), Slot(), 4, true, Now).Ok);
    }

    [Fact]
    public void A_private_booking_cannot_join_a_session_that_already_has_people()
    {
        Assert.Equal(
            ExperienceRules.Refusal.PrivateNeedsEmptySlot,
            ExperienceRules.CanBook(Make(priv: 3_000_000m), Slot(taken: 1), 4, true, Now).Reason);
    }

    [Fact]
    public void Nobody_else_joins_a_session_that_was_taken_privately()
    {
        Assert.Equal(
            ExperienceRules.Refusal.PrivateNeedsEmptySlot,
            ExperienceRules.CanBook(Make(priv: 3_000_000m), Slot(priv: true), 1, false, Now).Reason);
    }

    [Fact]
    public void A_private_group_is_one_price_not_a_price_each()
    {
        var shared = Pricing.QuoteExperience(new Pricing.ExperienceRequest
        {
            Experience = Make(priv: 3_000_000m), Seats = 4, Private = false, StartsAt = Now.AddDays(3)
        });
        var priv = Pricing.QuoteExperience(new Pricing.ExperienceRequest
        {
            Experience = Make(priv: 3_000_000m), Seats = 4, Private = true, StartsAt = Now.AddDays(3)
        });

        Assert.Equal(2_000_000m, shared.Subtotal);
        Assert.Equal(3_000_000m, priv.Subtotal);
    }

    /* ------------------------------------------------------------ pricing */

    [Fact]
    public void A_ticket_carries_the_same_fee_rates_as_a_stay()
    {
        var price = Pricing.QuoteExperience(new Pricing.ExperienceRequest
        {
            Experience = Make(), Seats = 2, StartsAt = Now.AddDays(3)
        });

        Assert.Equal(1_000_000m, price.Subtotal);
        Assert.Equal(140_000m, price.GuestServiceFee);   // 14% of the subtotal
        Assert.Equal(30_000m, price.HostServiceFee);     //  3% of the subtotal
        Assert.Equal(970_000m, price.HostPayout);
        Assert.Equal(1_140_000m, price.Total);
    }

    [Fact]
    public void The_lines_shown_add_up_to_the_total()
    {
        var price = Pricing.QuoteExperience(new Pricing.ExperienceRequest
        {
            Experience = Make(), Seats = 3, StartsAt = Now.AddDays(3),
            TaxRules = [new TaxRule
            {
                Id = 1, Country = "Việt Nam", City = "Hội An", Name = "VAT",
                Method = TaxMethod.Percentage, Value = 0.08m, Base = TaxBase.Subtotal
            }]
        });

        Assert.Equal(price.Total, price.Lines.Sum(l => l.Amount));
        Assert.Equal(120_000m, price.Tax);
    }

    [Fact]
    public void A_nightly_levy_does_not_apply_to_something_sold_by_the_seat()
    {
        var price = Pricing.QuoteExperience(new Pricing.ExperienceRequest
        {
            Experience = Make(), Seats = 2, StartsAt = Now.AddDays(3),
            TaxRules = [new TaxRule
            {
                Id = 1, Country = "Việt Nam", Name = "Phí lưu trú mỗi đêm",
                Method = TaxMethod.PerNight, Value = 50_000m
            }]
        });

        Assert.Equal(0m, price.Tax);
    }

    /* ------------------------------------------------------------- MR-04 */

    [Fact]
    public void A_session_short_of_its_minimum_inside_the_window_is_called_off()
    {
        Assert.True(ExperienceRules.ShouldCallOff(Make(min: 4), Slot(taken: 2, hoursAhead: 24), Now));
    }

    [Fact]
    public void The_same_session_further_out_is_left_alone()
    {
        Assert.False(ExperienceRules.ShouldCallOff(Make(min: 4), Slot(taken: 2, hoursAhead: 96), Now));
    }

    [Fact]
    public void A_session_that_reached_its_minimum_runs()
    {
        Assert.False(ExperienceRules.ShouldCallOff(Make(min: 4), Slot(taken: 4, hoursAhead: 24), Now));
    }

    [Fact]
    public void A_private_session_never_needs_a_minimum()
    {
        Assert.False(ExperienceRules.ShouldCallOff(Make(min: 6), Slot(taken: 8, hoursAhead: 12, priv: true), Now));
    }

    /* ------------------------------------------------ §2.3 risk and vetting */

    [Fact]
    public void The_risk_band_follows_the_activity_not_the_hosts_preference()
    {
        Assert.Equal(ExperienceRisk.Low, ExperienceRules.RiskOf("walking"));
        Assert.Equal(ExperienceRisk.Low, ExperienceRules.RiskOf("food"));
        Assert.Equal(ExperienceRisk.Medium, ExperienceRules.RiskOf("motorbike"));
        Assert.Equal(ExperienceRisk.High, ExperienceRules.RiskOf("diving"));

        // docs/09 §2.3 puts "có trẻ em tham gia" in the top row, so children
        // lift a medium activity into high.
        Assert.Equal(ExperienceRisk.High, ExperienceRules.RiskOf("motorbike", allowsChildren: true));
        // A walking tour with children is still a walking tour.
        Assert.Equal(ExperienceRisk.Low, ExperienceRules.RiskOf("walking", allowsChildren: true));
    }

    [Fact]
    public void A_high_risk_experience_short_of_a_paper_cannot_be_published()
    {
        var today = new DateOnly(2026, 9, 1);

        var diving = Make();
        diving.Category = "diving";
        diving.MeetingPoint = "Bãi Xếp, Quy Nhơn";
        diving.Description = "09:00 tập trung · 10:00 xuống nước · 12:00 kết thúc";
        diving.ModerationStatus = ExperienceModeration.Approved;

        var missing = ExperienceRules.PublishBlockers(diving, today);
        Assert.Contains("Giấy phép hành nghề", missing);
        Assert.Contains("Bảo hiểm trách nhiệm", missing);
        Assert.Contains("Số điện thoại khẩn cấp", missing);
        Assert.False(ExperienceRules.CanPublish(diving, today));

        // Papers in, and it clears.
        diving.SafetyPlan = "Có hướng dẫn trước khi xuống nước, thợ lặn kèm 1:2.";
        diving.LicenceName = "Chứng chỉ dạy lặn PADI";
        diving.LicenceExpiresOn = today.AddYears(1);
        diving.InsurancePolicy = "Bảo hiểm trách nhiệm PVI-2026";
        diving.InsuranceExpiresOn = today.AddYears(1);
        diving.EmergencyPhone = "0900000000";
        Assert.Empty(ExperienceRules.PublishBlockers(diving, today));
        Assert.True(ExperienceRules.CanPublish(diving, today));

        // An expired licence is the same as no licence.
        diving.LicenceExpiresOn = today.AddDays(-1);
        Assert.Contains("Giấy phép hành nghề còn hạn", ExperienceRules.PublishBlockers(diving, today));
    }

    [Fact]
    public void Nothing_goes_on_sale_without_a_person_approving_it()
    {
        var today = new DateOnly(2026, 9, 1);

        var walk = Make();
        walk.Category = "walking";
        walk.MeetingPoint = "Chợ Bến Thành";
        walk.Description = "08:00 gặp nhau · 10:00 kết thúc";

        // Nothing is missing, but nobody has looked at it yet (§2.2).
        Assert.Empty(ExperienceRules.PublishBlockers(walk, today));
        Assert.False(ExperienceRules.CanPublish(walk, today));

        walk.ModerationStatus = ExperienceModeration.Approved;
        Assert.True(ExperienceRules.CanPublish(walk, today));
    }

    [Fact]
    public void An_experience_pays_its_host_a_day_after_the_session_ends()   // scenario 12
    {
        var start = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

        // docs/09 §4 — a 2-hour session ends at 11:00; the payout is due 24 hours
        // after that end, not 24 hours after the start the way a stay pays.
        Assert.Equal(start.AddHours(2).AddDays(1), Payouts.SessionPayoutDue(start, 120));

        Assert.False(Payouts.SessionPayoutReady(start, 120, start.AddHours(2)));                 // just ended
        Assert.False(Payouts.SessionPayoutReady(start, 120, start.AddHours(2).AddHours(23)));    // 23h after end
        Assert.True(Payouts.SessionPayoutReady(start, 120, start.AddHours(2).AddDays(1)));       // due
    }

    [Fact]
    public void Sessions_closer_together_than_their_duration_clash()
    {
        var nine = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

        // A two-hour experience: another session an hour later overlaps…
        Assert.True(ExperienceRules.Overlaps(nine, nine.AddHours(1), 120));
        // …one exactly two hours later just clears it…
        Assert.False(ExperienceRules.Overlaps(nine, nine.AddHours(2), 120));
        // …three hours later is plainly fine, and order does not matter.
        Assert.False(ExperienceRules.Overlaps(nine.AddHours(3), nine, 120));
    }

    [Fact]
    public void A_repeating_pattern_expands_to_the_days_it_names()
    {
        // Tuesday, Thursday, Saturday at 09:00 — Monday is bit 0.
        var mask = (1 << 1) | (1 << 3) | (1 << 5);
        var from = new DateOnly(2026, 9, 1);              // a Tuesday
        var now = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc);

        var starts = ExperienceRules.ExpandRecurrence(mask, new TimeOnly(9, 0), from, 2, now);

        Assert.Equal(6, starts.Count);                     // three a week, two weeks
        Assert.All(starts, s => Assert.Equal(9, s.Hour));
        Assert.All(starts, s => Assert.Contains(
            s.DayOfWeek, new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday }));

        // Sessions already in the past are never created.
        var late = ExperienceRules.ExpandRecurrence(
            mask, new TimeOnly(9, 0), from, 2, new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc));
        Assert.All(late, s => Assert.True(s > new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc)));

        // A mask that names no day produces nothing rather than every day.
        Assert.Empty(ExperienceRules.ExpandRecurrence(0, new TimeOnly(9, 0), from, 2, now));
    }

    [Fact]
    public void Nine_in_the_morning_means_nine_where_the_host_lives()
    {
        // The same seven hours that broke the services picker (docs/09 §3.4).
        // A host in Ho Chi Minh City typing 09:00 must not get sessions at
        // 09:00Z, which is four in the afternoon to everybody involved.
        var monday = 1 << 0;
        var from = new DateOnly(2026, 9, 7);              // a Monday
        var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var local = ExperienceRules.ExpandRecurrence(
            monday, new TimeOnly(9, 0), from, 1, now, "Asia/Ho_Chi_Minh");

        Assert.Single(local);
        Assert.Equal(2, local[0].Hour);                   // 09:00 +07:00 == 02:00Z
        Assert.Equal(DateTimeKind.Utc, local[0].Kind);

        // Left unsaid the pattern is still read as UTC, so a caller that has
        // already converted is not converted twice.
        var utc = ExperienceRules.ExpandRecurrence(monday, new TimeOnly(9, 0), from, 1, now);
        Assert.Equal(9, utc[0].Hour);

        // An unknown zone falls back to UTC rather than throwing: a mistyped
        // setting must not stop a host putting sessions on sale.
        var unknown = ExperienceRules.ExpandRecurrence(
            monday, new TimeOnly(9, 0), from, 1, now, "Mars/Olympus_Mons");
        Assert.Equal(9, unknown[0].Hour);
    }

    [Fact]
    public void A_called_off_session_points_at_ones_the_guest_could_actually_take()
    {
        var now = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        var slots = new[]
        {
            new ExperienceSlot { Id = 1, StartsAt = now.AddDays(1), Capacity = 10, SeatsTaken = 2 },
            new ExperienceSlot { Id = 2, StartsAt = now.AddDays(2), Capacity = 10, SeatsTaken = 9 },
            new ExperienceSlot { Id = 3, StartsAt = now.AddDays(3), Capacity = 10, SeatsTaken = 0, IsPrivate = true },
            new ExperienceSlot { Id = 4, StartsAt = now.AddDays(4), Capacity = 10, SeatsTaken = 0, Status = SlotStatus.Cancelled },
            new ExperienceSlot { Id = 5, StartsAt = now.AddDays(-1), Capacity = 10, SeatsTaken = 0 },
            new ExperienceSlot { Id = 6, StartsAt = now.AddDays(5), Capacity = 10, SeatsTaken = 4 }
        };

        // A party of three: only 1 and 6 have room, are open, public and ahead.
        var alternatives = ExperienceRules.AlternativesFor(slots, cancelledSlotId: 99, seats: 3, now);
        Assert.Equal([1, 6], alternatives.Select(s => s.Id).ToArray());

        // The session being called off is never suggested back.
        Assert.DoesNotContain(
            ExperienceRules.AlternativesFor(slots, cancelledSlotId: 1, seats: 3, now),
            s => s.Id == 1);
    }

    /* ------------------------------------------- §2.9 register, §2.10 reviews */

    [Fact]
    public void The_register_can_only_be_taken_once_the_session_is_under_way()
    {
        var start = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

        Assert.False(ExperienceAttendance.CanMark(start, start.AddMinutes(-1)));
        Assert.True(ExperienceAttendance.CanMark(start, start));

        // docs/09 §2.9 (TN-F) — a quarter of an hour, then the host may begin.
        Assert.False(ExperienceAttendance.MayStartWithout(start, start.AddMinutes(15)));
        Assert.True(ExperienceAttendance.MayStartWithout(start, start.AddMinutes(16)));

        // A no-show is not a cancellation, and gets nothing back.
        Assert.Equal(0m, ExperienceAttendance.NoShowRefund());
    }

    [Fact]
    public void Only_somebody_who_was_there_can_review_an_experience()
    {
        var start = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
        var ends = start.AddHours(2);

        var attended = new ExperienceBooking { Attended = true, Status = ExperienceBookingStatus.Completed };

        Assert.True(ExperienceReviews.CanReview(attended, ends, ends));
        // Not before the session is over, even for somebody marked present.
        Assert.False(ExperienceReviews.CanReview(attended, ends, ends.AddMinutes(-1)));

        // docs/09 §2.10 — "chỉ người có mặt": a no-show and an unmarked ticket
        // both fail, however much they paid.
        Assert.False(ExperienceReviews.CanReview(
            new ExperienceBooking { Attended = false }, ends, ends.AddDays(1)));
        Assert.False(ExperienceReviews.CanReview(
            new ExperienceBooking { Attended = null }, ends, ends.AddDays(1)));
    }

    [Fact]
    public void An_experience_is_judged_on_its_own_four_criteria()
    {
        // docs/09 §2.10 — four, and not the stay's six: no cleanliness, no
        // check-in, no location.
        Assert.Equal(4, ExperienceReviews.Criteria.Count);
        Assert.Equal(["host", "asDescribed", "safety", "value"],
            ExperienceReviews.Criteria.Select(c => c.Key).ToArray());
        Assert.DoesNotContain(ExperienceReviews.Criteria, c => c.Label.Contains("sạch"));

        Assert.Equal(4.5, ExperienceReviews.Average(5, 4, 5, 4));
        Assert.True(ExperienceReviews.ScoreInRange(1));
        Assert.True(ExperienceReviews.ScoreInRange(5));
        Assert.False(ExperienceReviews.ScoreInRange(0));
        Assert.False(ExperienceReviews.ScoreInRange(6));
    }

    /* --------------------------------------------------------- refunds */

    [Fact]
    public void The_experience_cancellation_ladder_is_its_own_not_the_stay_policy()
    {
        // Booked well in the past, so the 24h grace never colours these tiers.
        var booking = new ExperienceBooking { Total = 1_140_000m, CreatedAt = Now.AddDays(-30) };

        // docs/09 §2.8: ≥7 days 100%, 24h–7 days 50%, <24h nothing.
        Assert.Equal(1_140_000m, ExperienceRules.GuestRefund(booking, Now.AddDays(8), Now));
        // Scenario 6 — a guest pulling out 5 days ahead gets 50%, not 100% like a stay.
        Assert.Equal(570_000m, ExperienceRules.GuestRefund(booking, Now.AddDays(5), Now));
        Assert.Equal(570_000m, ExperienceRules.GuestRefund(booking, Now.AddHours(25), Now));
        Assert.Equal(0m, ExperienceRules.GuestRefund(booking, Now.AddHours(23), Now));
    }

    [Fact]
    public void The_first_day_after_booking_is_a_full_refund_grace_while_the_session_is_far_off()
    {
        var booking = new ExperienceBooking { Total = 1_140_000m, CreatedAt = Now };

        // Cancelled 3 hours after booking, session still 10 days away → full back
        // even though the plain tier would already be 100% here; the grace matters
        // most when the session is inside the week…
        Assert.Equal(1_140_000m, ExperienceRules.GuestRefund(booking, Now.AddDays(10), Now.AddHours(3)));

        // …booked, then cancelled next morning with the session 3 days off: the tier
        // alone would give 50%, but the grace lifts it to 100%.
        Assert.Equal(1_140_000m, ExperienceRules.GuestRefund(booking, Now.AddDays(3), Now.AddHours(20)));

        // Grace does not apply once the session is inside 48h: booked then cancelled
        // two hours later, but the session is only 20h away → the <24h tier, nothing.
        Assert.Equal(0m, ExperienceRules.GuestRefund(booking, Now.AddHours(20), Now.AddHours(2)));
    }

    [Fact]
    public void Refunding_a_ticket_leaves_the_books_flat()
    {
        var price = Pricing.QuoteExperience(new Pricing.ExperienceRequest
        {
            Experience = Make(), Seats = 3, StartsAt = Now.AddDays(3),
            TaxRules = [new TaxRule
            {
                Id = 1, Country = "Việt Nam", Name = "VAT",
                Method = TaxMethod.Percentage, Value = 0.08m, Base = TaxBase.Subtotal
            }]
        });

        var booking = new ExperienceBooking
        {
            Id = 1, Reference = "XP1", Seats = 3,
            Subtotal = price.Subtotal, ServiceFee = price.GuestServiceFee, Tax = price.Tax,
            Total = price.Total, HostServiceFee = price.HostServiceFee, HostPayout = price.HostPayout
        };

        var captured = Ledger.CaptureExperience(booking, Now);
        var refunded = Ledger.RefundExperience(booking, booking.Total, Now);

        Assert.Equal(0m, Ledger.Imbalance(captured));
        Assert.Equal(0m, Ledger.Imbalance(refunded));

        // A full refund leaves nothing behind in any account.
        foreach (var account in Enum.GetValues<LedgerAccount>())
            Assert.Equal(0m, Net(captured.Concat(refunded), account));
    }

    [Fact]
    public void A_partial_refund_still_balances()
    {
        var booking = new ExperienceBooking
        {
            Id = 1, Reference = "XP1",
            Subtotal = 1_000_000m, ServiceFee = 140_000m, Tax = 80_000m,
            Total = 1_220_000m, HostServiceFee = 30_000m, HostPayout = 970_000m
        };

        Assert.Equal(0m, Ledger.Imbalance(Ledger.RefundExperience(booking, 610_000m, Now)));
        Assert.Empty(Ledger.RefundExperience(booking, 0m, Now));
    }

    private static decimal Net(IEnumerable<LedgerEntry> entries, LedgerAccount account) =>
        entries.Where(e => e.Account == account).Sum(e => e.Signed);
}
