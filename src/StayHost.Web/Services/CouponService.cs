using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;

namespace StayHost.Web.Services;

/// <summary>
/// docs/01 ĐP-09, TC-09 — applying a promo code to a booking. The arithmetic and
/// the campaign rules are in <see cref="Coupons"/>; this counts the redemptions
/// they need against the database and writes the append-only redemption rows.
/// </summary>
public class CouponService(StayHostDbContext db)
{
    public sealed record Applied(Coupon Coupon, decimal Discount, string Label);

    /// <summary>
    /// Evaluates a code for a guest against a stay's pre-reduction total.
    ///
    /// <paramref name="excludeBookingId"/> is the booking being re-priced at
    /// payment, whose own earlier redemption must not count against the limits —
    /// the same trap as a hold counting itself out of the new-listing discount.
    /// Null while first quoting, before any redemption exists.
    /// </summary>
    public async Task<Coupons.Check> EvaluateAsync(
        string? code, int userId, decimal bookingTotal, DateTime now,
        int? excludeBookingId = null, CancellationToken ct = default)
    {
        var normalized = Coupons.Normalize(code);
        if (normalized.Length == 0) return new(false, Error: "Nhập mã giảm giá.");

        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Code == normalized, ct);
        if (coupon is null) return new(false, Error: "Mã giảm giá không tồn tại.");

        var live = db.CouponRedemptions
            .Where(r => r.CouponId == coupon.Id && !r.Voided
                        && (excludeBookingId == null || r.BookingId != excludeBookingId));

        var timesUsedTotal = await live.CountAsync(ct);
        var timesUsedByGuest = await live.CountAsync(r => r.UserId == userId, ct);

        return Coupons.Evaluate(coupon, bookingTotal, timesUsedTotal, timesUsedByGuest, now);
    }

    /// <summary>Records a use without saving; the caller commits it with the booking.</summary>
    public void Redeem(int couponId, int userId, int bookingId, decimal amount)
    {
        db.CouponRedemptions.Add(new CouponRedemption
        {
            CouponId = couponId,
            UserId = userId,
            BookingId = bookingId,
            Amount = amount
        });
    }

    /// <summary>
    /// docs/01 TC-09 — whether this booking's redemption, once written, is still
    /// inside the campaign's limits when every other request is counted too.
    ///
    /// Evaluating and then writing let two requests sent together both count the
    /// same "0 used" and both take a once-per-guest code. Ranking by row id makes
    /// the earliest write the winner, so exactly the late ones are turned away.
    /// </summary>
    public async Task<bool> WithinLimitsAsync(int couponId, int bookingId, CancellationToken ct)
    {
        var coupon = await db.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == couponId, ct);
        var mine = await db.CouponRedemptions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CouponId == couponId && r.BookingId == bookingId && !r.Voided, ct);
        if (coupon is null || mine is null) return true;

        var ahead = db.CouponRedemptions.Where(r => r.CouponId == couponId && !r.Voided && r.Id <= mine.Id);

        if (coupon.MaxRedemptions is { } max && await ahead.CountAsync(ct) > max) return false;
        if (coupon.MaxPerUser is { } perUser
            && await ahead.CountAsync(r => r.UserId == mine.UserId, ct) > perUser) return false;

        return true;
    }

    /// <summary>
    /// docs/01 TC-09 — a booking that fell through gives its redemption back, so a
    /// limited campaign is not spent on stays that never happened. The row stays
    /// for the record and is marked rather than deleted.
    /// </summary>
    public async Task<int> ReleaseAsync(int bookingId, CancellationToken ct = default)
    {
        var rows = await db.CouponRedemptions
            .Where(r => r.BookingId == bookingId && !r.Voided)
            .ToListAsync(ct);

        foreach (var r in rows) r.Voided = true;
        return rows.Count;
    }
}
