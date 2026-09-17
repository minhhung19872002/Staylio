namespace StayHost.Domain.Tests;

/// <summary>docs/01 CĐ-06 — change-request window, validation, and the ledger delta.</summary>
public class ChangeRequestsTests
{
    private static readonly DateTime Now = new(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc);

    /* ---- window ---- */

    [Fact]
    public void A_change_offer_lives_24_hours()
    {
        var r = new BookingChangeRequest { Status = ChangeRequestStatus.Pending, ExpiresAt = ChangeRequests.ExpiryFrom(Now) };
        Assert.Equal(Now.AddHours(24), r.ExpiresAt);
        Assert.True(ChangeRequests.IsLive(r, Now.AddHours(23)));
        Assert.False(ChangeRequests.IsLive(r, Now.AddHours(25)));
    }

    [Fact]
    public void Once_answered_it_is_not_live()
    {
        var r = new BookingChangeRequest { Status = ChangeRequestStatus.Rejected, ExpiresAt = ChangeRequests.ExpiryFrom(Now) };
        Assert.False(ChangeRequests.IsLive(r, Now.AddHours(1)));
    }

    [Fact]
    public void Validation_rejects_a_backwards_range_or_no_guests()
    {
        Assert.NotNull(ChangeRequests.Validate(new(2026, 9, 5), new(2026, 9, 3), 2));
        Assert.NotNull(ChangeRequests.Validate(new(2026, 9, 3), new(2026, 9, 5), 0));
        Assert.Null(ChangeRequests.Validate(new(2026, 9, 3), new(2026, 9, 5), 2));
    }

    [Fact]
    public void The_difference_reads_as_pay_more_or_get_back()
    {
        Assert.Contains("trả thêm", ChangeRequests.DiffLabel(200_000m));
        Assert.Contains("hoàn lại", ChangeRequests.DiffLabel(-150_000m));
        Assert.Contains("Không thay đổi", ChangeRequests.DiffLabel(0));
    }

    /* ---- the ledger delta balances both ways (docs/01 CĐ-06) ---- */

    private static Booking OldBooking() => new()
    {
        Id = 1, Reference = "SH1",
        Subtotal = 3_000_000m, ServiceFee = 420_000m, Tax = 240_000m,
        HostServiceFee = 90_000m, HostPayout = 2_910_000m, Total = 3_660_000m
    };

    private static Pricing.Breakdown NewPrice(decimal factor)
    {
        // Scale a stay up or down; the individual fields keep the capture identity
        // HostPayout + HSF + GSF + Tax == Total (no coupon/credit here).
        decimal sub = 3_000_000m * factor, gsf = 420_000m * factor, tax = 240_000m * factor,
                hsf = 90_000m * factor, payout = sub - hsf, total = payout + hsf + gsf + tax;
        return new Pricing.Breakdown
        {
            Nights = 3, NightlyRate = 1_000_000m, RoomBeforeDiscount = sub, RoomDiscount = 0,
            DiscountPercent = 0, DiscountParts = [], ExtraGuestFee = 0, PetFee = 0, CleaningFee = 0,
            Subtotal = sub, GuestServiceFee = gsf, Tax = tax, TaxLines = [], Coupon = 0, Promotion = 0,
            Total = total, HostServiceFee = hsf, HostPayout = payout, Lines = [], Nightly = []
        };
    }

    [Fact]
    public void Charging_more_balances_to_zero()
    {
        var entries = Ledger.AdjustBooking(OldBooking(), NewPrice(1.5m), DateTime.UtcNow);
        Assert.Equal(0m, Ledger.Imbalance(entries));
        // The extra is owed, not yet paid: it is collected later like a balance.
        Assert.Contains(entries, e => e.Account == LedgerAccount.GuestReceivable && e.Direction == LedgerDirection.Debit);
        Assert.DoesNotContain(entries, e => e.Account == LedgerAccount.GuestFunds);
    }

    [Fact]
    public void Refunding_the_difference_balances_to_zero()
    {
        var entries = Ledger.AdjustBooking(OldBooking(), NewPrice(0.6m), DateTime.UtcNow);
        Assert.Equal(0m, Ledger.Imbalance(entries));
        // A smaller stay owes the guest money back.
        Assert.Contains(entries, e => e.Account == LedgerAccount.GuestRefundPayable && e.Direction == LedgerDirection.Credit);
    }

    [Fact]
    public void No_change_posts_nothing()
    {
        var entries = Ledger.AdjustBooking(OldBooking(), NewPrice(1.0m), DateTime.UtcNow);
        Assert.Empty(entries);
    }
}
