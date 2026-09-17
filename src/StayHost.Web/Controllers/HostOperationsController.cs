using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayHost.Domain;
using StayHost.Infrastructure;
using StayHost.Web.Contracts;
using StayHost.Web.Infrastructure;
using StayHost.Web.Services;

namespace StayHost.Web.Controllers;

/// <summary>
/// The running-a-listing half of the host console: the daily board, calendar
/// rules, bulk day edits, income export and payout settings.
/// </summary>
[ApiController]
[Route("api/host")]
public class HostOperationsController(
    StayHostDbContext db, AuthService auth, HostAccess access, ShieldService shield,
    BadgeService badges, NotificationService notifications, PaymentGateway gateway,
    CatalogService catalog, PayoutAccounts payoutAccounts, RefundGateway refunds,
    ILogger<HostOperationsController> log) : ControllerBase
{
    /// <summary>
    /// docs/01 CĐ-06, docs/04 QT-4 — the host answers a guest's request to change
    /// dates or guests. Accepting moves the booking, frees the old dates and
    /// settles the difference in the ledger; rejecting leaves the booking as it was.
    /// </summary>
    [HttpPost("bookings/{id:int}/change-request/{reqId:int}/respond")]
    public async Task<IActionResult> RespondChange(
        int id, int reqId, [FromBody] RespondChangeRequest req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var booking = await db.Bookings.Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();

        if (await access.ListingAsync(user, booking.ListingId, CoHostScope.Bookings, ct) is null)
            return this.Denied("Bạn không có quyền với đơn này.");

        var change = await db.BookingChangeRequests
            .FirstOrDefaultAsync(r => r.Id == reqId && r.BookingId == booking.Id, ct);
        if (change is null) return NotFound();
        if (!ChangeRequests.IsLive(change, DateTime.UtcNow))
            return BadRequest(new { message = "Yêu cầu đổi lịch này không còn hiệu lực." });

        if (!req.Accept)
        {
            change.Status = ChangeRequestStatus.Rejected;
            change.RespondedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await notifications.QueueWithEmailAsync(
                await GuestOf(booking, ct), NotificationKind.System, "Yêu cầu đổi lịch bị từ chối",
                $"Chủ nhà không đổi được lịch đơn {booking.Reference}. Đơn giữ nguyên như cũ.",
                $"/trips/{booking.Id}", ct);
            return NoContent();
        }

        // The dates could have been taken in the meantime; the same exclusion
        // check runs again before anything moves.
        var clash = await db.Bookings.AnyAsync(b =>
            b.ListingId == booking.ListingId && b.Id != booking.Id
            && BookingLifecycle.BlocksDates.Contains(b.Status)
            && b.CheckIn < change.NewCheckOut && change.NewCheckIn < b.CheckOut, ct);
        if (clash) return Conflict(new { message = "Ngày mới vừa có người khác đặt. Không đổi được." });

        var party = new PartySize(change.NewAdults, change.NewChildren, change.NewInfants, change.NewPets);
        var fresh = await catalog.BuildQuoteRequestAsync(
            booking.ListingId, change.NewCheckIn, change.NewCheckOut, party, ct, booking.Id,
            booking.RoomTypeId, nightlyOverride: booking.NightlyOverride, plan: booking.Plan,
            loyaltyPercent: booking.LoyaltyPercent);
        if (fresh is null) return NotFound();
        if (booking.CouponDiscount > 0) fresh = fresh with { CouponAmount = booking.CouponDiscount, CouponLabel = "Mã giảm giá" };
        if (booking.CreditUsed > 0) fresh = fresh with { PromotionAmount = booking.CreditUsed, PromotionLabel = "Số dư Staylio" };
        var price = Pricing.Quote(fresh);
        var now = DateTime.UtcNow;
        var diff = price.Total - booking.Total;

        // docs/01 CĐ-06 — the money already recognised shifts by the difference.
        // Only where money was recognised: a request still waiting on the host
        // has nothing captured, and a stay paid at the door never passed through
        // the platform (docs/07 §2.5).
        var captured = booking.Status is BookingStatus.Confirmed or BookingStatus.InProgress
                       && !booking.PaidAtProperty;

        if (captured)
            db.LedgerEntries.AddRange(Ledger.AdjustBooking(booking, price, now));

        // Move the booking onto the new stay.
        booking.CheckIn = change.NewCheckIn;
        booking.CheckOut = change.NewCheckOut;
        booking.Nights = price.Nights;
        booking.Guests = party.Counted;
        booking.Adults = change.NewAdults;
        booking.Children = change.NewChildren;
        booking.Infants = change.NewInfants;
        booking.Pets = change.NewPets;
        booking.RoomBeforeDiscount = price.RoomBeforeDiscount;
        booking.RoomDiscount = price.RoomDiscount;
        booking.DiscountPercent = price.DiscountPercent;
        booking.ExtraGuestFee = price.ExtraGuestFee;
        booking.PetFee = price.PetFee;
        booking.BreakfastFee = price.BreakfastFee;
        booking.CleaningFee = price.CleaningFee;
        booking.Subtotal = price.Subtotal;
        booking.ServiceFee = price.GuestServiceFee;
        booking.Tax = price.Tax;
        booking.Total = price.Total;
        booking.HostServiceFee = price.HostServiceFee;
        booking.HostPayout = price.HostPayout;

        if (booking.Payment is not null)
        {
            booking.Payment.Amount = price.Total;
            booking.Payment.HostPayout = price.HostPayout;
            booking.Payment.PlatformFee = price.GuestServiceFee + price.HostServiceFee;
        }

        // The difference has to actually move. Accepting used to adjust the books
        // and the host's payout and stop there: ten nights paid out for two paid
        // for, and a smaller stay's refund recorded as owed and never sent.
        if (captured && diff > 0)
        {
            // Collected like the rest of a part-paid stay: due today, reminded,
            // and on the same 72-hour clock (BalanceCollector).
            booking.BalanceDue += diff;
            if (booking.BalanceStatus is BalanceStatus.None or BalanceStatus.Paid)
            {
                booking.BalanceStatus = BalanceStatus.Scheduled;
                booking.BalanceFirstFailedAt = null;
            }
            booking.BalanceDueOn = DateOnly.FromDateTime(now);
        }
        else if (captured && diff < 0)
        {
            var owed = -diff;

            // Set against what the guest still owes first; only the rest is cash.
            var netted = Math.Min(owed, booking.BalanceDue);
            if (netted > 0)
            {
                db.LedgerEntries.AddRange(Ledger.NetRefundAgainstReceivable(booking, netted, now));
                booking.BalanceDue -= netted;
                if (booking.BalanceDue == 0 && booking.BalanceStatus != BalanceStatus.None)
                    booking.BalanceStatus = BalanceStatus.Paid;
            }

            var cash = owed - netted;
            if (cash > 0)
            {
                var sent = await refunds.SendForAsync(
                    s => s.BookingId == booking.Id, cash, booking.Reference,
                    booking.Payment?.Method ?? "card", booking.Payment?.CardLast4, $"host:{user.Id}", ct);
                var toCard = Math.Min(cash, sent.ToCard);
                var asBalance = cash - toCard;

                if (toCard > 0)
                    db.LedgerEntries.AddRange(Ledger.SettleRefund(booking, toCard, now));
                if (asBalance > 0)
                {
                    db.LedgerEntries.AddRange(Ledger.SettleRefundAsCredit(booking, asBalance, now));
                    if (booking.GuestUserId is { } owner)
                        db.CreditEntries.Add(CreditLedger.Grant(
                            owner, asBalance, CreditReason.Returned,
                            $"Hoàn chênh lệch đổi lịch đơn {booking.Reference}", now, booking.Id));
                }

                booking.DepositPaid = Math.Max(0m, booking.DepositPaid - cash);
            }
        }

        change.Status = ChangeRequestStatus.Accepted;
        change.RespondedAt = DateTime.UtcNow;

        db.BookingEvents.Add(BookingLifecycle.Note(booking, $"host:{user.Id}",
            $"Đổi lịch sang {change.NewCheckIn:dd/MM}–{change.NewCheckOut:dd/MM}, "
            + ChangeRequests.DiffLabel(change.Difference)));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (BookingsController.IsOverlapViolation(ex))
        {
            return Conflict(new { message = "Ngày mới vừa có người khác đặt. Không đổi được." });
        }

        await notifications.QueueWithEmailAsync(
            await GuestOf(booking, ct), NotificationKind.System, "Đổi lịch được chấp nhận",
            $"Đơn {booking.Reference} đã chuyển sang {change.NewCheckIn:dd/MM}–{change.NewCheckOut:dd/MM}. "
            + ChangeRequests.DiffLabel(change.Difference), $"/trips/{booking.Id}", ct);

        return Ok(new { newTotal = booking.Total, difference = change.Difference });
    }

    private async Task<User?> GuestOf(Booking b, CancellationToken ct) =>
        b.GuestUserId is { } gid ? await db.Users.FirstOrDefaultAsync(u => u.Id == gid, ct) : null;

    /// <summary>
    /// A host walking away from a confirmed booking. docs/03 §4 gives the guest
    /// everything back plus a credit, and docs/06 §2.1 K1 opens a Staylio Shield
    /// case on their behalf when it happens inside 30 days of check-in — the
    /// guest should not have to notice and file it themselves.
    /// </summary>
    /// <summary>
    /// docs/01 QL-13 — "được cảnh báo rõ hậu quả trước khi xác nhận".
    ///
    /// The same refund maths the cancellation itself will run, plus the two
    /// consequences that are not money: a Staylio Shield case opens on the guest's
    /// behalf inside 30 days (docs/06 K1), and the self-cancellation rate is one
    /// of the four Superhost criteria (docs/03 §8). A host who only learns that
    /// afterwards was not warned.
    /// </summary>
    [HttpGet("bookings/{id:int}/cancel-preview")]
    public async Task<ActionResult<HostCancelPreviewDto>> CancelPreview(int id, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var booking = await db.Bookings
            .Include(b => b.Payment)
            .Include(b => b.Listing)
            .Include(b => b.GuestUser)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();

        if (await access.ListingAsync(user, booking.ListingId, CoHostScope.Bookings, ct) is null)
            return this.Denied("Bạn không có quyền với đơn này.");

        if (!BookingLifecycle.CanTransition(booking.Status, BookingStatus.CancelledByHost))
            return BadRequest(new
            {
                message = $"Đơn đang ở trạng thái \"{BookingLifecycle.Label(booking.Status)}\" nên không huỷ được."
            });

        var outcome = Cancellation.Refund(new Cancellation.Context
        {
            Booking = booking,
            Now = DateTime.UtcNow,
            By = CancelledBy.Host,
            ServiceFeeRefundsUsed = 0
        });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysOut = booking.CheckIn.DayNumber - today.DayNumber;
        var opensShield = daysOut is >= 0 and <= 30;

        // docs/03 §8 — where this leaves the criterion, counted the way the
        // badge job counts it, so the warning and the decision agree.
        var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == booking.Listing!.HostId, ct);
        var yearAgo = today.AddYears(-1);

        var orders = await db.Bookings
            .Where(b => b.Listing!.HostId == booking.Listing!.HostId && b.CheckIn >= yearAgo)
            .Select(b => new { b.Status, b.CancelledBy })
            .ToListAsync(ct);

        var live = orders.Count(o => BookingLifecycle.BlocksDates.Contains(o.Status));
        var cancels = orders.Count(o => o.Status == BookingStatus.CancelledByHost
                                        && o.CancelledBy == CancelledBy.Host);
        var after = Math.Round((cancels + 1) * 100.0 / Math.Max(1, live + cancels + 1), 2);

        var rateNote = after >= Badges.SuperhostCancelRate
            ? $"Tỉ lệ tự huỷ sẽ thành {after:0.##}% — vượt mức {Badges.SuperhostCancelRate:0}% "
              + $"của danh hiệu Siêu chủ nhà{(host?.IsSuperhost == true ? ", bạn có thể mất danh hiệu ở kỳ xét tới." : ".")}"
            : $"Tỉ lệ tự huỷ sẽ thành {after:0.##}%, vẫn dưới mức {Badges.SuperhostCancelRate:0}%.";

        var consequences = new List<string>
        {
            $"Khách được hoàn {outcome.Amount:N0}đ — toàn bộ số tiền đã trả.",
            "Những ngày của đơn này bị chặn trên lịch, không nhận đặt lại.",
            "Tin đăng hiện ghi chú công khai rằng bạn đã huỷ một đơn trước ngày nhận phòng.",
            host?.IsSuperhost == true
                ? "Bạn mất danh hiệu Siêu chủ nhà ngay và không được xét lại trong 1 năm."
                : "Bạn không được xét danh hiệu Siêu chủ nhà trong 1 năm.",
            rateNote
        };

        if (cancels + 1 >= HostCancelHideAt)
            consequences.Add($"Đây là lần huỷ thứ {cancels + 1} trong 1 năm: tin đăng bị tạm ẩn để Staylio xem xét.");

        if (outcome.GoodwillCredit > 0)
            consequences.Insert(1, $"Khách nhận thêm {outcome.GoodwillCredit:N0}đ số dư đền bù.");

        var penalty = booking.PaidAtProperty
                      || booking.Status is not (BookingStatus.Confirmed or BookingStatus.InProgress)
            ? 0m
            : HostPenalties.For(booking.Subtotal,
                BookingService.CheckInUtc(booking.Listing!, booking.CheckIn), DateTime.UtcNow);
        if (penalty > 0)
            consequences.Insert(1,
                $"Bạn chịu phí phạt huỷ đơn {penalty:N0}đ ({HostPenalties.RateFor(BookingService.CheckInUtc(booking.Listing!, booking.CheckIn), DateTime.UtcNow):P0} giá trị đơn), trừ vào lần chuyển tiền kế tiếp.");

        if (opensShield)
            consequences.Insert(1,
                $"Còn {daysOut} ngày tới ngày nhận phòng nên hệ thống tự mở hồ sơ Staylio Shield "
                + "để tìm chỗ ở thay thế cho khách; chi phí chênh lệch có thể được thu lại từ bạn.");

        return Ok(new HostCancelPreviewDto(
            booking.Reference,
            booking.GuestUser?.FullName ?? booking.GuestName,
            booking.CheckIn,
            booking.Nights,
            outcome.Amount,
            outcome.GoodwillCredit,
            booking.Payment?.HostPayout ?? booking.HostPayout,
            opensShield,
            rateNote,
            consequences));
    }

    [HttpPost("bookings/{id:int}/cancel")]
    public async Task<IActionResult> CancelBooking(
        int id, [FromBody] HostCancelRequest? req, CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var booking = await db.Bookings
            .Include(b => b.Events)
            .Include(b => b.Payment)
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return NotFound();

        if (await access.ListingAsync(user, booking.ListingId, CoHostScope.Bookings, ct) is null)
            return this.Denied("Bạn không có quyền với đơn này.");

        if (!BookingLifecycle.CanTransition(booking.Status, BookingStatus.CancelledByHost))
            return BadRequest(new
            {
                message = $"Đơn đang ở trạng thái \"{BookingLifecycle.Label(booking.Status)}\" nên không huỷ được."
            });

        var outcome = Cancellation.Refund(new Cancellation.Context
        {
            Booking = booking,
            Now = DateTime.UtcNow,
            By = CancelledBy.Host,
            ServiceFeeRefundsUsed = 0
        });

        // docs/07 §10 — ask the gateway before deciding where the money lands.
        // This used to default to "the card took it" without asking anything,
        // which was harmless until a real gateway held the money.
        var wasPaid = booking.Status is BookingStatus.Confirmed or BookingStatus.InProgress
                      && !booking.PaidAtProperty;

        var sentBack = await refunds.SendAsync(
            booking, outcome.Amount, "host", "Chu nha huy don", ct);

        BookingsController.PostCancellation(
            db, booking, outcome, CancelledBy.Host,
            (req?.Reason ?? "Chủ nhà huỷ đơn").Trim(), sentBack);

        await ApplyHostCancelPenaltyAsync(booking, wasPaid, ct);

        // docs/01 ĐG-12 — a public note on the listing, so the next guest sees the
        // host has pulled out of a confirmed stay before. Not a review: no rating,
        // no effect on the score.
        var daysBefore = CancellationNotes.DaysBefore(booking.CheckIn, DateTime.UtcNow);
        db.ListingCancellationNotes.Add(new ListingCancellationNote
        {
            ListingId = booking.ListingId,
            Note = CancellationNotes.Compose(daysBefore),
            DaysBeforeCheckIn = daysBefore
        });

        await db.SaveChangesAsync(ct);

        await shield.OpenHostCancellationAsync(booking, ct);

        return Ok(new
        {
            refunded = outcome.Amount,
            credit = outcome.GoodwillCredit,
            message = "Đã huỷ đơn và hoàn tiền cho khách."
        });
    }

    /// <summary>docs/03 §4 — the third cancellation inside a year hides the listing.</summary>
    private const int HostCancelHideAt = 3;

    /// <summary>
    /// docs/03 §4, "Hậu quả khi chủ nhà tự huỷ đơn đã xác nhận". Three of the
    /// five used to be missing: the dates went straight back on sale, the
    /// Superhost title waited for the quarter, and nothing ever hid a listing.
    /// The fifth — a penalty rising towards check-in — has no amounts in the
    /// spec and is left for the customer to set.
    /// </summary>
    private async Task ApplyHostCancelPenaltyAsync(Booking booking, bool wasPaid, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (booking.CheckOut > booking.CheckIn)
        {
            db.CalendarBlocks.Add(new CalendarBlock
            {
                ListingId = booking.ListingId,
                From = booking.CheckIn,
                To = booking.CheckOut.AddDays(-1),
                Note = $"Đã huỷ đơn {booking.Reference} — ngày bị chặn",
                ExternalUid = CalendarBlock.HostCancelPrefix + booking.Id
            });
        }

        var hostId = booking.Listing!.HostId;
        var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == hostId, ct);

        // The fine rises as the stay comes closer. Only a stay that was actually
        // paid through the platform: a request nobody paid for costs the host
        // nothing to decline, and a stay paid at the door has no platform money.
        var fine = !wasPaid
            ? 0m
            : HostPenalties.For(booking.Subtotal, BookingService.CheckInUtc(booking.Listing, booking.CheckIn), now);
        if (host is not null && fine > 0)
        {
            host.OwedToPlatform += fine;
            db.BookingEvents.Add(BookingLifecycle.Note(booking, "system",
                $"Chủ nhà chịu phí phạt huỷ {fine:#,##0}₫."));
            var hostUser = await db.Users.FirstOrDefaultAsync(u => u.Id == host.UserId, ct);
            await notifications.QueueWithEmailAsync(hostUser, NotificationKind.System,
                "Phí phạt huỷ đơn", HostPenalties.Notice(fine, booking.Reference), "/hosting?tab=earnings", ct);
        }

        if (host is { IsSuperhost: true })
        {
            host.IsSuperhost = false;
            db.BookingEvents.Add(BookingLifecycle.Note(
                booking, "system", "Chủ nhà mất danh hiệu Siêu chủ nhà do tự huỷ đơn."));
        }

        // "Trong 1 năm" is when the host cancelled, not when the stay was due:
        // counting by check-in kept a cancellation of a stay a year out on the
        // record for two years, and ignored one whose stay had already passed.
        var yearAgo = now.AddYears(-1);
        var cancels = await db.Bookings.CountAsync(b =>
            b.Listing!.HostId == hostId
            && b.Status == BookingStatus.CancelledByHost && b.CancelledBy == CancelledBy.Host
            && b.Id != booking.Id
            && b.Events.Any(e => e.ToStatus == BookingStatus.CancelledByHost && e.CreatedAt >= yearAgo), ct) + 1;

        if (cancels >= HostCancelHideAt && booking.Listing is { } listing
            && listing.ReviewStatus == ListingReviewStatus.Approved)
        {
            // Pending rather than unpublished: it drops out of search at once and
            // lands in the admin queue, which reads published pending listings.
            listing.ReviewStatus = ListingReviewStatus.Pending;
            listing.SubmittedForReviewAt = now;
            listing.ReviewNote = $"Tạm ẩn: chủ nhà đã huỷ {cancels} đơn trong 1 năm.";
        }
    }

    private async Task<(User? User, HostProfile? Profile)> ResolveAsync(CancellationToken ct)
    {
        var user = await auth.CurrentUserAsync(ct);
        if (user is null) return (null, null);
        return (user, await db.Hosts.FirstOrDefaultAsync(h => h.UserId == user.Id, ct));
    }

    /* ------------------------------------------------------------- QL-01 */


    /* ------------------------------------- docs/07 §2.5: cash at the door */

    /// <summary>
    /// docs/07 §2.5 — the host confirms the guest handed over the money.
    ///
    /// This is the only moment a pay-at-property booking touches the platform's
    /// books, and even then no ledger entry is written: nothing moved through
    /// Staylio. What is recorded is the platform's own claim — both service fees,
    /// which the guest paid as part of the total and the host is now holding —
    /// against <see cref="HostProfile.OwedToPlatform"/>, netted off the host's
    /// next transfer the same way a lost chargeback already is.
    ///
    /// Only the host may mark it, and only once. A second press is answered with
    /// the booking unchanged rather than billed twice.
    /// </summary>
    [HttpPost("bookings/{id:int}/cash-collected")]
    public async Task<ActionResult<CashCollectedDto>> CashCollected(int id, CancellationToken ct)
    {
        var (user, profile) = await ResolveAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

        var listingIds = await access.ListingIdsAsync(user, CoHostScope.Bookings, ct);

        var booking = await db.Bookings
            .Include(b => b.Payment)
            .Include(b => b.Listing!).ThenInclude(l => l.Host)
            .FirstOrDefaultAsync(b => b.Id == id && listingIds.Contains(b.ListingId), ct);

        if (booking is null) return NotFound();

        if (!PaymentMethods.SettlesAtProperty(booking.Payment?.Method))
            return BadRequest(new { message = "Đơn này không phải đơn trả tiền tại nơi ở." });

        if (booking.CashCollectedAt is not null)
            return Ok(new CashCollectedDto(booking.Reference, booking.Total, 0m, booking.CashCollectedAt.Value, true));

        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.InProgress or BookingStatus.Completed))
            return BadRequest(new
            {
                message = $"Đơn đang ở trạng thái \"{BookingLifecycle.Label(booking.Status)}\" nên chưa ghi nhận tiền được."
            });

        var now = DateTime.UtcNow;
        var fees = PayAtProperty.FeesOwed(booking.ServiceFee, booking.HostServiceFee);

        // Only the booking records this. The payment row deliberately stays
        // Pending with no CapturedAt: nothing was captured, and the payout sweep
        // reads Captured to decide what Staylio owes a host. Marking it would put
        // this booking in a transfer of money the platform never received.
        booking.CashCollectedAt = now;

        // The host of the listing, not whoever pressed the button: a co-host with
        // the bookings scope may confirm the cash, and the fee is still the
        // listing owner's to settle.
        if (booking.Listing?.Host is { } host) host.OwedToPlatform += fees;

        db.BookingEvents.Add(BookingLifecycle.Note(booking, $"host:{user.Id}",
            $"Chủ nhà xác nhận đã nhận {booking.Total:#,##0}₫ tiền mặt tại nơi ở."));

        await notifications.QueueWithEmailAsync(user, NotificationKind.System,
            "Đã ghi nhận tiền tại nơi ở",
            $"Đơn {booking.Reference}: {booking.Total:#,##0}₫. Phí dịch vụ {fees:#,##0}₫ sẽ trừ vào lần chuyển tiền kế tiếp.",
            "/hosting", ct);

        await db.SaveChangesAsync(ct);
        return Ok(new CashCollectedDto(booking.Reference, booking.Total, fees, now, false));
    }

    /// <summary>
    /// docs/01 QL-01 — what needs doing today: guests arriving, guests in the
    /// house, guests leaving, and requests still waiting on an answer.
    /// </summary>
    [HttpGet("today")]
    public async Task<ActionResult<TodayBoardDto>> Today(CancellationToken ct)
    {
        var (user, profile) = await ResolveAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        // A co-host with the bookings scope sees the same board for the places
        // they help run (docs/01 QL-19).
        var listingIds = await access.ListingIdsAsync(user, CoHostScope.Bookings, ct);
        if (listingIds.Count == 0) return Ok(new TodayBoardDto([], [], [], [], 0));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizon = today.AddDays(7);

        var bookings = await db.Bookings
            .Where(b => listingIds.Contains(b.ListingId) && b.CheckOut >= today && b.CheckIn <= horizon)
            .Include(b => b.Listing)
            .Include(b => b.GuestUser)
            .OrderBy(b => b.CheckIn)
            .ToListAsync(ct);

        TodayItemDto Row(Booking b, string what) => new(
            b.Id, b.Reference, b.Listing?.Title ?? "",
            b.GuestUser?.FullName ?? b.GuestName ?? "Khách",
            b.CheckIn, b.CheckOut, b.Nights, b.Guests, what,
            BookingLifecycle.Label(b.Status), BookingLifecycle.BadgeClass(b.Status));

        var live = bookings.Where(b => BookingLifecycle.BlocksDates.Contains(b.Status)).ToList();

        var arriving = live
            .Where(b => b.CheckIn >= today && b.CheckIn <= horizon)
            .Select(b => Row(b, b.CheckIn == today ? "Nhận phòng hôm nay" : $"Nhận phòng {b.CheckIn:dd/MM}"))
            .ToList();

        var inHouse = live
            .Where(b => b.CheckIn <= today && today < b.CheckOut)
            .Select(b => Row(b, "Đang lưu trú"))
            .ToList();

        var leaving = live
            .Where(b => b.CheckOut >= today && b.CheckOut <= horizon)
            .Select(b => Row(b, b.CheckOut == today ? "Trả phòng hôm nay" : $"Trả phòng {b.CheckOut:dd/MM}"))
            .ToList();

        var waiting = await db.Bookings
            .Where(b => listingIds.Contains(b.ListingId) && b.Status == BookingStatus.PendingHostApproval)
            .Include(b => b.Listing).Include(b => b.GuestUser)
            .OrderBy(b => b.RequestExpiresAt)
            .ToListAsync(ct);

        return Ok(new TodayBoardDto(
            arriving, inHouse, leaving,
            waiting.Select(b => Row(b, "Cần trả lời trong 24 giờ")).ToList(),
            waiting.Count));
    }

    /* ------------------------------------------------------------- QL-04 */

    /// <summary>
    /// docs/01 QL-04 — every listing's availability side by side for one date
    /// range, so a host with several places can see the whole month at once.
    /// </summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<MultiCalendarDto>> MultiCalendar(
        [FromQuery] DateOnly? from, [FromQuery] int days = 30, CancellationToken ct = default)
    {
        var (user, profile) = await ResolveAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var span = Math.Clamp(days, 7, 90);
        var end = start.AddDays(span);

        var mine = await access.ListingIdsAsync(user, CoHostScope.Calendar, ct);
        var listings = await db.Listings
            .Where(l => mine.Contains(l.Id))
            .OrderBy(l => l.Title)
            .ToListAsync(ct);
        var ids = listings.Select(l => l.Id).ToList();

        var stays = await db.Bookings
            .Where(b => ids.Contains(b.ListingId)
                        && BookingLifecycle.BlocksDates.Contains(b.Status)
                        && b.CheckIn < end && start < b.CheckOut)
            .Select(b => new { b.ListingId, b.CheckIn, b.CheckOut, b.Reference, b.Guests })
            .ToListAsync(ct);

        var blocks = await db.CalendarBlocks
            .Where(b => ids.Contains(b.ListingId) && b.From <= end && start <= b.To)
            .Select(b => new { b.ListingId, b.From, b.To })
            .ToListAsync(ct);

        var rules = await db.PriceRules
            .Where(r => ids.Contains(r.ListingId) && r.From <= end && start <= r.To)
            .ToListAsync(ct);

        var rows = new List<MultiCalendarRowDto>();
        foreach (var listing in listings)
        {
            var booked = new Dictionary<DateOnly, string>();
            foreach (var s in stays.Where(x => x.ListingId == listing.Id))
                for (var d = s.CheckIn; d < s.CheckOut; d = d.AddDays(1)) booked[d] = s.Reference;

            var blocked = new HashSet<DateOnly>();
            foreach (var b in blocks.Where(x => x.ListingId == listing.Id))
                for (var d = b.From; d <= b.To; d = d.AddDays(1)) blocked.Add(d);

            var listingRules = rules.Where(r => r.ListingId == listing.Id).ToList();
            var cells = new List<MultiCalendarCellDto>(span);

            for (var d = start; d < end; d = d.AddDays(1))
            {
                var rate = Pricing.RateFor(listing, d, listingRules);
                var state = booked.ContainsKey(d) ? "booked" : blocked.Contains(d) ? "blocked" : "open";
                cells.Add(new MultiCalendarCellDto(d, rate.Rate, rate.Source, state, booked.GetValueOrDefault(d)));
            }

            rows.Add(new MultiCalendarRowDto(listing.Id, listing.Title, listing.IsPublished, cells));
        }

        return Ok(new MultiCalendarDto(start, span, rows));
    }

    /* --------------------------------------------------------- QL-06/07 */

    /// <summary>
    /// docs/01 QL-06 and QL-07 — the calendar rules that decide who can book:
    /// night limits, notice, turnover, how far ahead the calendar opens, and
    /// which weekdays are closed to arrivals or departures.
    /// </summary>
    [HttpPut("listings/{id:int}/rules")]
    public async Task<ActionResult<CalendarRulesDto>> SaveRules(
        int id, [FromBody] CalendarRulesDto req, CancellationToken ct)
    {
        var listing = await OwnedListingAsync(id, ct, CoHostScope.Pricing);
        if (listing is null) return this.Denied();

        listing.MinNights = Math.Clamp(req.MinNights, 1, 365);
        listing.MaxNights = Math.Clamp(req.MaxNights, 0, 365);
        listing.AdvanceNoticeHours = Math.Clamp(req.AdvanceNoticeHours, 0, 24 * 30);
        listing.SameDayCutoffHour = req.SameDayCutoffHour is int h ? Math.Clamp(h, 0, 23) : null;
        listing.CalendarVisibilityMonths = Math.Clamp(req.CalendarVisibilityMonths, 0, 24);
        listing.TurnoverDays = Math.Clamp(req.TurnoverDays, 0, 14);
        listing.BlockedCheckInDays = req.BlockedCheckInDays & 0b1111111;
        listing.BlockedCheckOutDays = req.BlockedCheckOutDays & 0b1111111;
        if (!string.IsNullOrWhiteSpace(req.TimeZoneId)) listing.TimeZoneId = req.TimeZoneId.Trim();

        // A maximum below the minimum would make the listing unbookable outright.
        if (listing.MaxNights > 0 && listing.MaxNights < listing.MinNights)
            return BadRequest(new { message = "Số đêm tối đa phải lớn hơn hoặc bằng số đêm tối thiểu." });

        await db.SaveChangesAsync(ct);
        return Ok(RulesOf(listing));
    }

    [HttpGet("listings/{id:int}/rules")]
    public async Task<ActionResult<CalendarRulesDto>> GetRules(int id, CancellationToken ct)
    {
        var listing = await OwnedListingAsync(id, ct);
        return listing is null ? this.Denied() : Ok(RulesOf(listing));
    }

    private static CalendarRulesDto RulesOf(Listing l) => new(
        l.MinNights, l.MaxNights, l.AdvanceNoticeHours, l.SameDayCutoffHour,
        l.CalendarVisibilityMonths, l.TurnoverDays,
        l.BlockedCheckInDays, l.BlockedCheckOutDays, l.TimeZoneId);

    /* ------------------------------------------------------------- QL-05 */

    /// <summary>
    /// docs/01 QL-05 — one action over a set of days: set a price, block or
    /// unblock them, or change the minimum stay that starts on them.
    /// </summary>
    [HttpPost("listings/{id:int}/days")]
    public async Task<IActionResult> EditDays(int id, [FromBody] BulkDayEditRequest req, CancellationToken ct)
    {
        // A price is a Pricing matter. This endpoint used to need only Calendar,
        // so a co-host lent the calendar could set any nightly rate.
        var listing = await OwnedListingAsync(
            id, ct, req.NightlyRate is not null ? CoHostScope.Pricing : CoHostScope.Calendar);
        if (listing is null) return this.Denied();

        if (req.To < req.From) return BadRequest(new { message = "Ngày kết thúc phải sau ngày bắt đầu." });
        if (req.To.DayNumber - req.From.DayNumber > 365)
            return BadRequest(new { message = "Chỉ sửa được tối đa 365 ngày một lần." });
        if (req.NightlyRate is < 50_000)
            return BadRequest(new { message = "Giá mỗi đêm tối thiểu 50.000₫." });
        if (req.MinNights is < 1 or > 365)
            return BadRequest(new { message = "Số đêm tối thiểu phải từ 1 đến 365." });

        if (req.NightlyRate is { } rate)
        {
            // Day overrides beat seasons, so the edited days are replaced rather
            // than stacked on. Only those days: an override reaching past the
            // edited range keeps its outer parts, where it used to be dropped
            // whole and the neighbouring days fell back to the base price.
            await CarveAsync(id, PriceRuleKind.DayOverride, req.From, req.To, keepInside: false, ct);
            db.PriceRules.Add(new PriceRule
            {
                ListingId = id,
                Kind = PriceRuleKind.DayOverride,
                Name = req.Label ?? "Giá theo ngày",
                From = req.From,
                To = req.To,
                NightlyRate = rate,
                MinNights = req.MinNights
            });
        }

        if (req.MinNights is { } min && req.NightlyRate is null)
        {
            // docs/03 §1 step 1 — a day's own price outranks season and weekend.
            // Changing only the minimum used to write a day override at the base
            // price, so a Tết rate of 2 million quietly became the base the moment
            // the host set "tối thiểu 3 đêm". The minimum now lives on a rule
            // that carries no price at all, and the days' prices are untouched.
            await CarveAsync(id, PriceRuleKind.MinStay, req.From, req.To, keepInside: false, ct);
            await CarveAsync(id, PriceRuleKind.DayOverride, req.From, req.To, keepInside: true, ct);
            db.PriceRules.Add(new PriceRule
            {
                ListingId = id,
                Kind = PriceRuleKind.MinStay,
                Name = req.Label ?? "Số đêm tối thiểu",
                From = req.From,
                To = req.To,
                NightlyRate = 0,
                MinNights = min
            });
        }

        if (req.Blocked == true)
        {
            db.CalendarBlocks.Add(new CalendarBlock
            {
                ListingId = id, From = req.From, To = req.To, Note = req.Label ?? "Chủ nhà khoá"
            });
        }
        else if (req.Blocked == false)
        {
            // Imported blocks belong to their feed and a cancelled stay's nights
            // stay shut (docs/03 §4); only the host's own blocks are the host's
            // to clear.
            var overlapping = await db.CalendarBlocks
                .Where(b => b.ListingId == id && b.From <= req.To && req.From <= b.To
                            && b.FeedId == null
                            && (b.ExternalUid == null || !b.ExternalUid.StartsWith(CalendarBlock.HostCancelPrefix)))
                .ToListAsync(ct);
            db.CalendarBlocks.RemoveRange(overlapping);
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Cuts [<paramref name="from"/>, <paramref name="to"/>] out of every rule of
    /// one kind on a listing. The parts outside stay as they were; the part
    /// inside is dropped, or with <paramref name="keepInside"/> kept with its
    /// minimum cleared, so a new minimum is the only one on those days.
    /// </summary>
    private async Task CarveAsync(
        int listingId, PriceRuleKind kind, DateOnly from, DateOnly to, bool keepInside, CancellationToken ct)
    {
        var hit = await db.PriceRules
            .Where(r => r.ListingId == listingId && r.Kind == kind && r.From <= to && from <= r.To)
            .ToListAsync(ct);

        foreach (var r in hit)
        {
            db.PriceRules.Remove(r);

            PriceRule Piece(DateOnly a, DateOnly b, int? min) => new()
            {
                ListingId = r.ListingId, Kind = r.Kind, Name = r.Name,
                From = a, To = b, NightlyRate = r.NightlyRate, MinNights = min
            };

            if (r.From < from) db.PriceRules.Add(Piece(r.From, from.AddDays(-1), r.MinNights));
            if (r.To > to) db.PriceRules.Add(Piece(to.AddDays(1), r.To, r.MinNights));
            if (keepInside)
                db.PriceRules.Add(Piece(r.From > from ? r.From : from, r.To < to ? r.To : to, null));
        }
    }

    /* ------------------------------------------------------------- QL-15 */

    /// <summary>docs/01 QL-15 — the income report, as a file the host can keep.</summary>
    [HttpGet("earnings.csv")]
    public async Task<IActionResult> EarningsCsv(CancellationToken ct)
    {
        var (user, profile) = await ResolveAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        if (profile is null) return NotFound();

        var listingIds = await db.Listings.Where(l => l.HostId == profile.Id).Select(l => l.Id).ToListAsync(ct);

        var rows = await db.Bookings
            .Where(b => listingIds.Contains(b.ListingId) && BookingLifecycle.BlocksDates.Contains(b.Status))
            .Include(b => b.Listing)
            .Include(b => b.Payment)
            .OrderBy(b => b.CheckIn)
            .ToListAsync(ct);

        var vn = CultureInfo.GetCultureInfo("vi-VN");
        var csv = new StringBuilder();
        csv.AppendLine("Mã đơn;Chỗ nghỉ;Nhận phòng;Trả phòng;Số đêm;Khách;Khách trả;Phí dịch vụ chủ nhà;Bạn nhận;Trạng thái;Ngày trả tiền");

        foreach (var b in rows)
        {
            csv.Append(b.Reference).Append(';')
               .Append(Escape(b.Listing?.Title ?? "")).Append(';')
               .Append(b.CheckIn.ToString("dd/MM/yyyy")).Append(';')
               .Append(b.CheckOut.ToString("dd/MM/yyyy")).Append(';')
               .Append(b.Nights).Append(';')
               .Append(b.Guests).Append(';')
               .Append(b.Total.ToString("0", vn)).Append(';')
               .Append(b.HostServiceFee.ToString("0", vn)).Append(';')
               .Append((b.Payment?.HostPayout ?? b.HostPayout).ToString("0", vn)).Append(';')
               .Append(BookingLifecycle.Label(b.Status)).Append(';')
               .Append(b.Payment?.PayoutDueOn?.ToString("dd/MM/yyyy") ?? "")
               .AppendLine();
        }

        // The BOM is what makes Excel open a semicolon-separated UTF-8 file correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv", $"stayhost-doanh-thu-{DateTime.UtcNow:yyyy-MM-dd}.csv");

        static string Escape(string v) => v.Replace(';', ',').Replace('\n', ' ');
    }

    /* ------------------------------------------------------------- QL-16 */

    /// <summary>
    /// docs/01 QL-16, docs/02 G7 — how each of a host's listings is doing over a
    /// window: views, saves, bookings, the conversion between them, and occupancy.
    /// The view counts have been collected all along (listing_views); this is the
    /// first thing that reads them back.
    /// </summary>
    [HttpGet("report")]
    public async Task<ActionResult<HostReportDto>> Report(
        [FromQuery] int days, CancellationToken ct)
    {
        var (user, profile) = await ResolveAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        if (profile is null) return NotFound();

        var window = days is >= 7 and <= 365 ? days : 30;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-window);
        // Npgsql compares against a timestamptz, which must be UTC-kinded.
        var fromUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var listings = await db.Listings
            .Where(l => l.HostId == profile.Id)
            .Select(l => new { l.Id, l.Title, l.IsPublished, l.City, l.RoomType, l.Bedrooms })
            .ToListAsync(ct);
        if (listings.Count == 0)
            return Ok(new HostReportDto(window, [], [], [], 0));

        var ids = listings.Select(l => l.Id).ToList();

        var views = await db.ListingViews
            .Where(v => ids.Contains(v.ListingId) && v.Day >= from)
            .GroupBy(v => v.ListingId)
            .Select(g => new { ListingId = g.Key, Views = g.Sum(x => x.Views) })
            .ToDictionaryAsync(x => x.ListingId, x => x.Views, ct);

        var saves = await db.Favorites
            .Where(f => ids.Contains(f.ListingId))
            .GroupBy(f => f.ListingId)
            .Select(g => new { ListingId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ListingId, x => x.Count, ct);

        // Bookings made in the window that actually took the dates — a hold that
        // lapsed is not a booking the listing earned.
        var madeInWindow = await db.Bookings
            .Where(b => ids.Contains(b.ListingId)
                        && BookingLifecycle.BlocksDates.Contains(b.Status)
                        && b.CreatedAt >= fromUtc)
            .Select(b => new { b.ListingId })
            .ToListAsync(ct);
        var bookingCounts = madeInWindow
            .GroupBy(b => b.ListingId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Nights actually occupied inside the window, for occupancy.
        var staying = await db.Bookings
            .Where(b => ids.Contains(b.ListingId)
                        && BookingLifecycle.BlocksDates.Contains(b.Status)
                        && b.CheckOut > from && b.CheckIn < today)
            .Select(b => new { b.ListingId, b.CheckIn, b.CheckOut })
            .ToListAsync(ct);
        var nightsBooked = staying
            .GroupBy(b => b.ListingId)
            .ToDictionary(g => g.Key,
                g => g.Sum(b => Domain.Performance.NightsInWindow(b.CheckIn, b.CheckOut, from, today)));

        // docs/02 G7 "giá trung bình" — what the rooms actually sold for over the
        // window, which is not the asking price: a host who discounts to fill the
        // calendar is charging less than their listing says, and this is the
        // number that says so.
        var sold = await db.Bookings
            .Where(b => ids.Contains(b.ListingId)
                        && BookingLifecycle.BlocksDates.Contains(b.Status)
                        && b.CheckOut > from && b.CheckIn < today)
            .Select(b => new { b.ListingId, b.RoomBeforeDiscount, b.RoomDiscount, b.Nights })
            .ToListAsync(ct);

        var achieved = sold
            .GroupBy(b => b.ListingId)
            .ToDictionary(
                g => g.Key,
                g => Domain.Performance.AchievedNightlyRate(
                    g.Sum(b => b.RoomBeforeDiscount - b.RoomDiscount), g.Sum(b => b.Nights)));

        // docs/02 G7 "so sánh với chỗ tương tự trong khu vực". One query for every
        // city this host is in, then compared listing by listing on exactly the
        // terms CN-10 uses — same city, same room type, within a bedroom either
        // way — because a studio and a five-bedroom villa are not each other's
        // market. Pulled once rather than per listing: a host with twelve places
        // in one city would otherwise ask the same question twelve times.
        var cities = listings.Select(l => l.City).Distinct().ToList();
        var market = await db.Listings
            .Where(l => l.IsPublished && l.ReviewStatus == ListingReviewStatus.Approved
                        && cities.Contains(l.City))
            .Select(l => new { l.City, l.RoomType, l.Bedrooms, l.PricePerNight })
            .ToListAsync(ct);

        var rows = listings.Select(l =>
        {
            var v = views.GetValueOrDefault(l.Id);
            var bk = bookingCounts.GetValueOrDefault(l.Id);
            var nights = nightsBooked.GetValueOrDefault(l.Id);

            var peers = market
                .Where(m => m.City == l.City && m.RoomType == l.RoomType
                            && m.Bedrooms >= l.Bedrooms - 1 && m.Bedrooms <= l.Bedrooms + 1)
                .Select(m => m.PricePerNight)
                .OrderBy(p => p)
                .ToList();

            return new ListingPerformanceDto(
                l.Id, l.Title, l.IsPublished,
                v, saves.GetValueOrDefault(l.Id), bk,
                Math.Round(Domain.Performance.ConversionRate(bk, v) * 100, 1),
                Math.Round(Domain.Performance.OccupancyRate(nights, window) * 100, 1),
                achieved.GetValueOrDefault(l.Id),
                Domain.Performance.Percentile(peers, 0.5),
                peers.Count);
        }).OrderByDescending(r => r.Views).ToList();

        var months = await EarningsByMonthAsync(ids, today, ct);
        var (reviews, reviewCount) = await ReviewTrendAsync(ids, today, ct);

        return Ok(new HostReportDto(window, months, rows, reviews, reviewCount));
    }

    /// <summary>How far back the two time series in docs/02 G7 look.</summary>
    private const int ReportMonths = 12;

    /// <summary>
    /// docs/02 G7 "Thu nhập: biểu đồ theo tháng, đã trả và sắp trả".
    ///
    /// Bucketed by the month the stay ended, because that is the month the host
    /// earned it — not the month the bank happened to move the money, which
    /// would put one stay's income in whichever month the payout run fell in.
    ///
    /// The split is the point. <see cref="PayoutStatus.Paid"/> is the only state
    /// that means the bank executed the transfer; <c>Sent</c> is a line on a file
    /// somebody still has to put through internet banking, and calling that "đã
    /// trả" would have the screen promising what the ledger has not posted.
    /// </summary>
    private async Task<IReadOnlyList<ReportMonthDto>> EarningsByMonthAsync(
        List<int> listingIds, DateOnly today, CancellationToken ct)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-(ReportMonths - 1));

        var stays = await db.Bookings
            .Where(b => listingIds.Contains(b.ListingId)
                        && BookingLifecycle.BlocksDates.Contains(b.Status)
                        && b.CheckOut >= first)
            .Select(b => new
            {
                b.CheckOut,
                b.Nights,
                Payout = b.Payment != null ? b.Payment.HostPayout : b.HostPayout,
                Status = b.Payment != null ? b.Payment.PayoutStatus : PayoutStatus.Scheduled
            })
            .ToListAsync(ct);

        var byMonth = stays
            .GroupBy(s => new DateOnly(s.CheckOut.Year, s.CheckOut.Month, 1))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Every month in the run, including the ones that earned nothing: a chart
        // drawn from a GROUP BY alone joins March straight to June and says
        // business was steady when in fact it stopped.
        return Domain.Performance.MonthsBetween(first, today)
            .Select(m =>
            {
                var rows = byMonth.GetValueOrDefault(m) ?? [];
                return new ReportMonthDto(
                    $"{m.Month:00}/{m.Year}", m.Year, m.Month,
                    rows.Where(r => r.Status == PayoutStatus.Paid).Sum(r => r.Payout),
                    rows.Where(r => r.Status != PayoutStatus.Paid).Sum(r => r.Payout),
                    rows.Sum(r => r.Nights));
            })
            .ToList();
    }

    /// <summary>
    /// docs/02 G7 "Đánh giá: điểm theo hạng mục qua thời gian".
    ///
    /// The overall star average is already on every listing; what it cannot show
    /// is which of the six is dragging it. A host whose score slipped from 4.9 to
    /// 4.6 can act on "cleanliness fell in July" and can do nothing at all with
    /// "the average fell".
    /// </summary>
    private async Task<(IReadOnlyList<ReportReviewMonthDto> Months, int Total)> ReviewTrendAsync(
        List<int> listingIds, DateOnly today, CancellationToken ct)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-(ReportMonths - 1));
        var firstUtc = DateTime.SpecifyKind(first.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var written = await db.Reviews
            .Where(r => listingIds.Contains(r.ListingId) && r.PublishedAt != null && r.CreatedAt >= firstUtc)
            .Select(r => new
            {
                r.CreatedAt, r.Rating,
                r.Cleanliness, r.Accuracy, r.CheckIn, r.Communication, r.Location, r.Value
            })
            .ToListAsync(ct);

        var byMonth = written
            .GroupBy(r => new DateOnly(r.CreatedAt.Year, r.CreatedAt.Month, 1))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Only the months that have a review are returned. An empty month in a
        // score series is not a zero — nobody rated this place a nought — and
        // drawing it as one would invent a collapse that never happened. This is
        // the opposite call from the earnings series above, where an empty month
        // really does mean nothing was earned.
        var months = Domain.Performance.MonthsBetween(first, today)
            .Where(byMonth.ContainsKey)
            .Select(m =>
            {
                var rows = byMonth[m];
                return new ReportReviewMonthDto(
                    $"{m.Month:00}/{m.Year}", m.Year, m.Month, rows.Count,
                    Math.Round(rows.Average(r => r.Rating), 2),
                    Math.Round(rows.Average(r => r.Cleanliness), 2),
                    Math.Round(rows.Average(r => r.Accuracy), 2),
                    Math.Round(rows.Average(r => r.CheckIn), 2),
                    Math.Round(rows.Average(r => r.Communication), 2),
                    Math.Round(rows.Average(r => r.Location), 2),
                    Math.Round(rows.Average(r => r.Value), 2));
            })
            .ToList();

        return (months, written.Count);
    }

    /* ------------------------------------------------------------- TC-04 */

    /// <summary>
    /// docs/01 TC-04, docs/02 G7 — "báo cáo thuế theo năm". Only completed stays
    /// count: a tax year is a record of what was delivered, and a booking still
    /// running has not finished happening.
    /// </summary>
    [HttpGet("tax-report")]
    public async Task<ActionResult<TaxReportDto>> TaxReport([FromQuery] int? year, CancellationToken ct)
    {
        var (user, profile) = await ResolveAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        if (profile is null) return NotFound();

        var stays = await CompletedStaysAsync(profile.Id, ct);
        var years = TaxReports.YearsCovered(stays);
        var chosen = year ?? years.FirstOrDefault(DateTime.UtcNow.Year);

        var report = TaxReports.Build(stays, chosen);

        return Ok(new TaxReportDto(
            report.Year,
            years,
            report.Months
                .Select(m => new TaxReportMonthDto(
                    m.Month, TaxReports.MonthLabel(m.Month), m.Stays,
                    m.GuestPaid, m.Tax, m.HostServiceFee, m.HostPayout))
                .ToList(),
            report.Taxes.Select(t => new TaxReportLineDto(t.Name, t.Amount, t.Stays)).ToList(),
            report.Stays, report.GuestPaid, report.RoomSubtotal, report.GuestServiceFee,
            report.Tax, report.HostServiceFee, report.HostPayout,
            TaxReports.RemittanceNote));
    }

    /// <summary>The same year, as a file to hand an accountant.</summary>
    [HttpGet("tax-report.csv")]
    public async Task<IActionResult> TaxReportCsv([FromQuery] int? year, CancellationToken ct)
    {
        var (user, profile) = await ResolveAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        if (profile is null) return NotFound();

        var stays = await CompletedStaysAsync(profile.Id, ct);
        var chosen = year ?? TaxReports.YearsCovered(stays).FirstOrDefault(DateTime.UtcNow.Year);
        var report = TaxReports.Build(stays, chosen);

        var vn = CultureInfo.GetCultureInfo("vi-VN");
        var csv = new StringBuilder();

        csv.AppendLine($"Báo cáo thuế năm {chosen}");
        csv.AppendLine(TaxReports.RemittanceNote.Replace(';', ','));
        csv.AppendLine();

        csv.AppendLine("Tháng;Số đơn;Khách trả;Thuế;Phí dịch vụ chủ nhà;Bạn nhận");
        foreach (var m in report.Months)
            csv.Append(TaxReports.MonthLabel(m.Month)).Append(';')
               .Append(m.Stays).Append(';')
               .Append(m.GuestPaid.ToString("0", vn)).Append(';')
               .Append(m.Tax.ToString("0", vn)).Append(';')
               .Append(m.HostServiceFee.ToString("0", vn)).Append(';')
               .Append(m.HostPayout.ToString("0", vn))
               .AppendLine();

        csv.Append("Cả năm;").Append(report.Stays).Append(';')
           .Append(report.GuestPaid.ToString("0", vn)).Append(';')
           .Append(report.Tax.ToString("0", vn)).Append(';')
           .Append(report.HostServiceFee.ToString("0", vn)).Append(';')
           .Append(report.HostPayout.ToString("0", vn))
           .AppendLine();

        if (report.Taxes.Count > 0)
        {
            csv.AppendLine();
            csv.AppendLine("Loại thuế;Số đơn;Số tiền");
            foreach (var t in report.Taxes)
                csv.Append(t.Name.Replace(';', ',')).Append(';')
                   .Append(t.Stays).Append(';')
                   .Append(t.Amount.ToString("0", vn))
                   .AppendLine();
        }

        // Every stay behind the totals, so the file can be checked rather than
        // trusted — and so a cash-basis reader has the payout dates to re-cut by.
        csv.AppendLine();
        csv.AppendLine("Mã đơn;Chỗ nghỉ;Nhận phòng;Trả phòng;Khách trả;Thuế;Phí dịch vụ chủ nhà;Bạn nhận;Ngày nhận tiền");
        foreach (var s in stays.Where(s => s.CheckOut.Year == chosen).OrderBy(s => s.CheckOut))
            csv.Append(s.Reference).Append(';')
               .Append(s.ListingTitle.Replace(';', ',').Replace('\n', ' ')).Append(';')
               .Append(s.CheckIn.ToString("dd/MM/yyyy")).Append(';')
               .Append(s.CheckOut.ToString("dd/MM/yyyy")).Append(';')
               .Append(s.GuestPaid.ToString("0", vn)).Append(';')
               .Append(s.Tax.ToString("0", vn)).Append(';')
               .Append(s.HostServiceFee.ToString("0", vn)).Append(';')
               .Append(s.HostPayout.ToString("0", vn)).Append(';')
               .Append(s.PaidOutOn?.ToString("dd/MM/yyyy") ?? "")
               .AppendLine();

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv", $"stayhost-bao-cao-thue-{chosen}.csv");
    }

    /// <summary>
    /// docs/03 §1 step 8 — the tax a guest paid is recorded on the booking as the
    /// rows they were shown, so the breakdown comes back out of those rather than
    /// from today's tax rules. A rule renamed or retired since must not rewrite
    /// what somebody was already charged.
    /// </summary>
    private async Task<List<TaxReports.Stay>> CompletedStaysAsync(int hostId, CancellationToken ct)
    {
        var bookings = await db.Bookings
            .Where(b => b.Listing!.HostId == hostId && b.Status == BookingStatus.Completed)
            .Include(b => b.Listing)
            .Include(b => b.Payment)
            .ToListAsync(ct);

        return bookings.Select(b => new TaxReports.Stay(
            b.Reference,
            b.Listing?.Title ?? "",
            b.CheckIn,
            b.CheckOut,
            // The day the money actually left, when it has; otherwise the day it
            // is due, which is the best answer available for a stay not paid yet.
            b.Payment?.PaidOutAt is { } paid ? DateOnly.FromDateTime(paid) : b.Payment?.PayoutDueOn,
            b.Total,
            b.Subtotal,
            b.ServiceFee,
            b.HostServiceFee,
            b.Payment?.HostPayout ?? b.HostPayout,
            TaxLinesOf(b))).ToList();
    }

    private static readonly System.Text.Json.JsonSerializerOptions TaxLineJson =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private static IReadOnlyList<PriceLine> TaxLinesOf(Booking booking)
    {
        try
        {
            var lines = System.Text.Json.JsonSerializer
                .Deserialize<List<PriceLineDto>>(booking.PriceLinesJson, TaxLineJson) ?? [];

            return lines
                .Where(l => l.Key.StartsWith("tax-", StringComparison.Ordinal))
                .Select(l => new PriceLine(l.Key, l.Label, l.Amount))
                .ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            // A booking whose stored rows cannot be read still belongs in the
            // totals; it just cannot say which tax it was. Losing the whole stay
            // would understate the year.
            return booking.Tax > 0 ? [new PriceLine("tax-0", "Thuế", booking.Tax)] : [];
        }
    }

    /* ------------------------------------------------------------- QL-20 */

    [HttpGet("payout")]
    public async Task<ActionResult<PayoutSettingsDto>> GetPayout(CancellationToken ct)
    {
        var (user, profile) = await ResolveAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        if (profile is null) return Ok(new PayoutSettingsDto(null, null, null, nameof(PayoutSchedule.PerBooking), []));

        return Ok(await PayoutOf(profile, ct));
    }

    [HttpPut("payout")]
    public async Task<ActionResult<PayoutSettingsDto>> SavePayout(
        [FromBody] SavePayoutRequest req, CancellationToken ct)
    {
        var (user, profile) = await ResolveAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        if (profile is null) return this.Denied();

        var digits = new string((req.AccountNumber ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length is > 0 and < 6)
            return BadRequest(new { message = "Số tài khoản không hợp lệ." });

        // docs/08 §5.4 — a ban also blocks the payout account from coming back
        // under a new name. Bank + tail is the strongest signal this build keeps.
        if (digits.Length >= 6)
        {
            var tail = digits[^4..];
            var bank = req.BankName?.Trim();

            var bannedAccount = await db.Hosts.AnyAsync(
                h => h.Id != profile.Id
                     && h.PayoutAccountLast4 == tail
                     && h.PayoutBankName == bank
                     && h.User != null && h.User.IsBanned, ct);

            if (bannedAccount)
            {
                return BadRequest(new
                {
                    message = "Không dùng được tài khoản nhận tiền này. " +
                              "Nếu bạn cho rằng có nhầm lẫn, hãy liên hệ hỗ trợ Staylio."
                });
            }
        }

        // docs/07 §12.2 — changing where the money goes freezes payouts for
        // three days and warns the address on file. Only a real change counts;
        // re-saving the same account is not an event.
        var newTail = digits.Length >= 6 ? digits[^4..] : profile.PayoutAccountLast4;
        var changed = newTail != profile.PayoutAccountLast4
                      || req.BankName?.Trim() != profile.PayoutBankName
                      || req.AccountName?.Trim() != profile.PayoutAccountName;

        profile.PayoutBankName = req.BankName?.Trim();
        profile.PayoutAccountName = req.AccountName?.Trim();
        profile.PayoutAccountLast4 = newTail;

        // docs/07 §14.3 — sealed here, masked everywhere it is shown. This build
        // used to keep only the tail, on the reading that the number "is not ours
        // to keep"; the rule actually says encrypted at rest, and without the
        // number the platform collects a guest's payment and has no way to
        // forward the host's share (§13 option A splits by bank transfer).
        if (digits.Length >= 6)
        {
            profile.PayoutAccountSealed = payoutAccounts.Seal(digits);

            if (!payoutAccounts.CanStore)
                log.LogWarning("Chủ nhà {HostId} khai tài khoản nhận tiền nhưng chưa có khoá mã hoá.",
                    profile.Id);
        }

        if (changed && profile.PayoutAccountLast4 is not null)
        {
            profile.PayoutAccountChangedAt = DateTime.UtcNow;
            profile.PayoutAccountVerified = false;

            await notifications.QueueWithEmailAsync(user, NotificationKind.System,
                "Tài khoản nhận tiền vừa được thay đổi",
                Payouts.FreezeNotice(profile.PayoutAccountChangedAt.Value),
                "/hosting", ct);

            // docs/07 §12.2 — the account only becomes payable once the name on it
            // matches the verified identity and a small transfer has actually
            // landed there. A mismatch is not a refusal; it is a queue for a person.
            if (!Payouts.NameMatchesIdentity(profile.PayoutAccountName, user.FullName))
            {
                await notifications.QueueWithEmailAsync(user, NotificationKind.System,
                    "Cần xem xét tài khoản nhận tiền", Payouts.NameMismatchNotice(), "/hosting", ct);
            }
            else
            {
                var test = gateway.TestTransfer(Payouts.TestTransferAmount, profile.PayoutAccountLast4);

                if (test.Ok)
                {
                    profile.PayoutAccountVerified = true;
                    await notifications.QueueWithEmailAsync(user, NotificationKind.System,
                        "Tài khoản nhận tiền đã xác minh",
                        Payouts.VerifiedNotice(profile.PayoutAccountLast4), "/hosting", ct);
                }
                else
                {
                    await notifications.QueueWithEmailAsync(user, NotificationKind.System,
                        "Không chuyển thử được tới tài khoản này",
                        $"{test.Reason} Vui lòng kiểm tra lại số tài khoản.", "/hosting", ct);
                }
            }
        }
        profile.PayoutSchedule = Enum.TryParse<PayoutSchedule>(req.Schedule, true, out var s)
            ? s
            : PayoutSchedule.PerBooking;

        await db.SaveChangesAsync(ct);
        return Ok(await PayoutOf(profile, ct));
    }

    /// <summary>
    /// docs/03 §5 — money reaches the host 24 hours after the guest checks in,
    /// so the schedule is derived from live bookings rather than a stored plan.
    /// </summary>
    private async Task<PayoutSettingsDto> PayoutOf(HostProfile profile, CancellationToken ct)
    {
        var listingIds = await db.Listings.Where(l => l.HostId == profile.Id).Select(l => l.Id).ToListAsync(ct);

        var bookings = await db.Bookings
            .Where(b => listingIds.Contains(b.ListingId)
                        && (BookingLifecycle.BlocksDates.Contains(b.Status) || b.Status == BookingStatus.Completed))
            .Include(b => b.Payment)
            .Include(b => b.Listing)
            .OrderByDescending(b => b.CheckIn)
            .Take(120)
            .ToListAsync(ct);

        // The status is read off the payout itself, not guessed from the calendar:
        // a host chasing money needs to see the hold reason, not a date that has
        // passed while the transfer sat still (docs/07 §12.4).
        PayoutRowDto RowOf(Booking b)
        {
            var p = b.Payment;
            var due = p?.PayoutDueOn ?? b.CheckIn.AddDays(1);
            var status = p?.PayoutStatus switch
            {
                PayoutStatus.Paid => "Đã chuyển",
                PayoutStatus.OnHold => p.PayoutHoldReason == PayoutHoldReason.None
                    ? "Chuyển không thành công, sẽ thử lại"
                    : "Tạm giữ",
                _ => "Chờ chuyển"
            };

            return new PayoutRowDto(
                b.Reference, b.Listing?.Title ?? "", due, p?.HostPayout ?? b.HostPayout, status,
                HoldReason: p is { PayoutHoldReason: not PayoutHoldReason.None }
                    ? Payouts.HoldLabel(p.PayoutHoldReason)
                    : null,
                TransferReference: p?.PayoutReference,
                PaidAt: p?.PaidOutAt,
                Attempts: p?.PayoutAttempts ?? 0,
                Deducted: p?.PayoutDeducted ?? 0m);
        }

        var paidOut = bookings.Where(b => b.Payment?.PayoutStatus == PayoutStatus.Paid).ToList();

        var upcoming = bookings
            .Where(b => b.Payment?.PayoutStatus != PayoutStatus.Paid)
            .OrderBy(b => b.Payment?.PayoutDueOn ?? b.CheckIn.AddDays(1))
            .Take(30)
            .Select(RowOf)
            .ToList();

        var history = paidOut
            .OrderByDescending(b => b.Payment!.PaidOutAt)
            .Take(30)
            .Select(RowOf)
            .ToList();

        return new PayoutSettingsDto(
            profile.PayoutBankName, profile.PayoutAccountName, profile.PayoutAccountLast4,
            profile.PayoutSchedule.ToString(), upcoming,
            Verified: profile.PayoutAccountVerified,
            FrozenUntil: profile.PayoutAccountChangedAt is { } at && DateTime.UtcNow < Payouts.FrozenUntil(at)
                ? Payouts.FrozenUntil(at)
                : null,
            OwedToPlatform: profile.OwedToPlatform,
            History: history);
    }

    /* ------------------------------------------------------------- QL-17 */

    /// <summary>
    /// docs/03 §8 — the four criteria for Chủ nhà Ưu tú, each with where the
    /// host currently stands, so progress is visible before the quarterly review.
    /// </summary>
    [HttpGet("superhost")]
    public async Task<ActionResult<SuperhostProgressDto>> Superhost(CancellationToken ct)
    {
        var (user, profile) = await ResolveAsync(ct);
        if (user is null) return Unauthorized(new { message = "Bạn cần đăng nhập." });
        if (profile is null) return NotFound();

        // docs/03 §8 — the same numbers and the same thresholds the quarterly
        // sweep decides on, so this screen cannot promise a title the job then
        // refuses. BadgeService owns the counting; Badges owns the rule.
        var stats = await badges.ProgressStatsAsync(profile, ct);
        var criteria = Badges.SuperhostCriteria(stats);

        return Ok(new SuperhostProgressDto(
            profile.IsSuperhost,
            criteria.All(c => c.Met),
            Badges.NextSuperhostReview(DateOnly.FromDateTime(DateTime.UtcNow)),
            criteria.Select(c => new SuperhostCriterionDto(c.Key, c.Label, c.Current, c.Target, c.Met)).ToList()));
    }

    /* ------------------------------------------------ docs/01 TĐ-22 guidebook */

    /// <summary>
    /// docs/01 TĐ-22 — the host's own guidebook for one listing, in the order
    /// they arranged it. This is the editing view: unlike the guest's, it keeps
    /// the flat list and every empty category, because the host is about to add
    /// to them.
    /// </summary>
    [HttpGet("listings/{id:int}/guidebook")]
    public async Task<ActionResult<IReadOnlyList<GuidebookPlaceDto>>> Guidebook(int id, CancellationToken ct)
    {
        var listing = await OwnedListingAsync(id, ct, CoHostScope.Listing);
        if (listing is null) return this.Denied("Bạn không có quyền với chỗ nghỉ này.");

        return Ok(await GuidebookOf(listing, ct));
    }

    /// <summary>docs/01 TĐ-22 — add one recommendation to the end of the list.</summary>
    [HttpPost("listings/{id:int}/guidebook")]
    public async Task<ActionResult<IReadOnlyList<GuidebookPlaceDto>>> AddGuidebookPlace(
        int id, [FromBody] GuidebookPlaceRequest req, CancellationToken ct)
    {
        var listing = await OwnedListingAsync(id, ct, CoHostScope.Listing);
        if (listing is null) return this.Denied("Bạn không có quyền với chỗ nghỉ này.");

        if (Guidebooks.Validate(req.Name, req.Note, req.Address) is { } invalid)
            return BadRequest(new { message = invalid });
        if (!Enum.TryParse<GuidebookCategory>(req.Category, out var category))
            return BadRequest(new { message = "Nhóm địa điểm không hợp lệ." });

        var existing = await db.GuidebookPlaces.CountAsync(p => p.ListingId == listing.Id, ct);
        if (Guidebooks.ValidateCount(existing) is { } full)
            return BadRequest(new { message = full });

        db.GuidebookPlaces.Add(new GuidebookPlace
        {
            ListingId = listing.Id,
            Category = category,
            Name = req.Name.Trim(),
            Note = Blank(req.Note),
            Address = Blank(req.Address),
            // Half a coordinate is no coordinate: store both or neither, so no
            // reader has to guess which half to trust.
            Latitude = Guidebooks.HasPin(req.Latitude, req.Longitude) ? req.Latitude : null,
            Longitude = Guidebooks.HasPin(req.Latitude, req.Longitude) ? req.Longitude : null,
            SortOrder = existing
        });
        await db.SaveChangesAsync(ct);

        return Ok(await GuidebookOf(listing, ct));
    }

    /// <summary>docs/01 TĐ-22 — rewrite one entry in place.</summary>
    [HttpPut("listings/{id:int}/guidebook/{placeId:int}")]
    public async Task<ActionResult<IReadOnlyList<GuidebookPlaceDto>>> UpdateGuidebookPlace(
        int id, int placeId, [FromBody] GuidebookPlaceRequest req, CancellationToken ct)
    {
        var listing = await OwnedListingAsync(id, ct, CoHostScope.Listing);
        if (listing is null) return this.Denied("Bạn không có quyền với chỗ nghỉ này.");

        if (Guidebooks.Validate(req.Name, req.Note, req.Address) is { } invalid)
            return BadRequest(new { message = invalid });
        if (!Enum.TryParse<GuidebookCategory>(req.Category, out var category))
            return BadRequest(new { message = "Nhóm địa điểm không hợp lệ." });

        var place = await db.GuidebookPlaces
            .FirstOrDefaultAsync(p => p.Id == placeId && p.ListingId == listing.Id, ct);
        if (place is null) return NotFound();

        place.Category = category;
        place.Name = req.Name.Trim();
        place.Note = Blank(req.Note);
        place.Address = Blank(req.Address);
        place.Latitude = Guidebooks.HasPin(req.Latitude, req.Longitude) ? req.Latitude : null;
        place.Longitude = Guidebooks.HasPin(req.Latitude, req.Longitude) ? req.Longitude : null;
        await db.SaveChangesAsync(ct);

        return Ok(await GuidebookOf(listing, ct));
    }

    /// <summary>docs/01 TĐ-22 — drop one entry and close the gap it leaves in the order.</summary>
    [HttpDelete("listings/{id:int}/guidebook/{placeId:int}")]
    public async Task<ActionResult<IReadOnlyList<GuidebookPlaceDto>>> DeleteGuidebookPlace(
        int id, int placeId, CancellationToken ct)
    {
        var listing = await OwnedListingAsync(id, ct, CoHostScope.Listing);
        if (listing is null) return this.Denied("Bạn không có quyền với chỗ nghỉ này.");

        var place = await db.GuidebookPlaces
            .FirstOrDefaultAsync(p => p.Id == placeId && p.ListingId == listing.Id, ct);
        if (place is null) return NotFound();

        db.GuidebookPlaces.Remove(place);
        await db.SaveChangesAsync(ct);

        // Renumber what is left, or the next add lands on a SortOrder already taken.
        var rest = await db.GuidebookPlaces
            .Where(p => p.ListingId == listing.Id)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync(ct);
        for (var i = 0; i < rest.Count; i++) rest[i].SortOrder = i;
        await db.SaveChangesAsync(ct);

        return Ok(await GuidebookOf(listing, ct));
    }

    private async Task<List<GuidebookPlaceDto>> GuidebookOf(Listing listing, CancellationToken ct) =>
        (await db.GuidebookPlaces
            .Where(p => p.ListingId == listing.Id)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync(ct))
        .Select(p => CatalogService.ToGuidebookDto(p, listing))
        .ToList();

    private static string? Blank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// The owner, or a co-host the owner gave this much rope (docs/01 QL-19).
    /// </summary>
    private async Task<Listing?> OwnedListingAsync(int id, CancellationToken ct, CoHostScope scope = CoHostScope.Calendar)
    {
        var user = await auth.CurrentUserAsync(ct);
        return user is null ? null : await access.ListingAsync(user, id, scope, ct);
    }
}
