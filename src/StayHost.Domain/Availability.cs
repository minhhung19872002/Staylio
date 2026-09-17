namespace StayHost.Domain;

/// <summary>
/// The nine checks of docs/03 §2, run in order, stopping at the first failure
/// so the guest is told the one thing that actually blocked them.
/// </summary>
public static class Availability
{
    /// <summary>Which of the nine steps rejected the stay, for logging and tests.</summary>
    public enum Reason
    {
        Ok = 0,
        NotBookable = 1,
        OverCapacity = 2,
        Pets = 3,
        AdvanceNotice = 4,
        CalendarHorizon = 5,
        NightCount = 6,
        BlockedWeekday = 7,
        DatesTaken = 8,
        TurnoverTime = 9,
        /// <summary>Not one of the nine, but nothing downstream makes sense without it.</summary>
        InvalidRange = 10
    }

    public readonly record struct Result(bool Ok, Reason Reason, string Message)
    {
        public static Result Pass => new(true, Reason.Ok, "");
        public static Result Fail(Reason reason, string message) => new(false, reason, message);
    }

    /// <summary>A stay that already owns some nights, or a host block.</summary>
    public readonly record struct Occupied(DateOnly From, DateOnly To, bool IsHostBlock);

    public sealed record Request
    {
        public required Listing Listing { get; init; }
        public required DateOnly CheckIn { get; init; }
        public required DateOnly CheckOut { get; init; }
        public PartySize Party { get; init; } = new(1);

        /// <summary>Now, in the listing's own time zone (docs/03 §2 step 4).</summary>
        public required DateTime LocalNow { get; init; }

        /// <summary>Live bookings and host blocks that already cover nights.</summary>
        public IReadOnlyCollection<Occupied> Occupied { get; init; } = [];

        /// <summary>Per-day minimum-night overrides, keyed by night.</summary>
        public IReadOnlyDictionary<DateOnly, int> MinNightsByDay { get; init; } =
            new Dictionary<DateOnly, int>();
    }

    public static Result Check(Request req)
    {
        var l = req.Listing;
        var nights = req.CheckOut.DayNumber - req.CheckIn.DayNumber;
        var today = DateOnly.FromDateTime(req.LocalNow);

        if (nights < 1)
            return Result.Fail(Reason.InvalidRange, "Ngày trả phòng phải sau ngày nhận phòng.");

        // 1 — the listing is on sale at all. docs/01 AT-01: a place still waiting
        // on review (or rejected) is published but not yet public, so not bookable.
        if (!ListingModeration.IsPubliclyVisible(l.IsPublished, l.ReviewStatus))
            return Result.Fail(Reason.NotBookable, "Chỗ nghỉ này hiện không nhận đặt.");

        // 2 — capacity. Infants do not count.
        if (req.Party.Counted > l.MaxGuests)
            return Result.Fail(Reason.OverCapacity, $"Chỗ nghỉ này nhận tối đa {l.MaxGuests} khách.");

        // 3 — pets.
        if (req.Party.Pets > 0)
        {
            if (!l.PetsAllowed)
                return Result.Fail(Reason.Pets, "Chỗ nghỉ này không nhận thú cưng.");
            if (req.Party.Pets > l.MaxPets)
                return Result.Fail(Reason.Pets, $"Chỗ nghỉ này nhận tối đa {l.MaxPets} thú cưng.");
        }

        // 4 — advance notice, measured in the listing's time zone.
        if (req.CheckIn < today)
            return Result.Fail(Reason.AdvanceNotice, "Không thể đặt ngày trong quá khứ.");

        if (req.CheckIn == today)
        {
            if (l.AdvanceNoticeHours > 0)
            {
                return Result.Fail(Reason.AdvanceNotice,
                    $"Chủ nhà cần được báo trước ít nhất {Describe(l.AdvanceNoticeHours)}.");
            }
            if (l.SameDayCutoffHour is int cutoff && req.LocalNow.Hour >= cutoff)
            {
                return Result.Fail(Reason.AdvanceNotice,
                    $"Đặt trong ngày phải hoàn tất trước {cutoff:00}:00 giờ địa phương.");
            }
        }
        else if (l.AdvanceNoticeHours > 0)
        {
            var earliest = req.LocalNow.AddHours(l.AdvanceNoticeHours);
            if (req.CheckIn.ToDateTime(TimeOnly.MinValue) < earliest.Date)
            {
                return Result.Fail(Reason.AdvanceNotice,
                    $"Chủ nhà cần được báo trước ít nhất {Describe(l.AdvanceNoticeHours)}.");
            }
        }

        // 5 — how far ahead the calendar is open.
        if (l.CalendarVisibilityMonths > 0)
        {
            var horizon = today.AddMonths(l.CalendarVisibilityMonths);
            if (req.CheckOut > horizon)
            {
                return Result.Fail(Reason.CalendarHorizon,
                    $"Chủ nhà mới mở lịch tới {horizon:dd/MM/yyyy}.");
            }
        }

        // 6 — night count. docs/03 §2 step 6: a minimum the host set for that
        // day "ưu tiên giá trị riêng đó" — it replaces the listing's, lower as
        // well as higher. Taking the larger of the two meant a host could never
        // open a single night on a place that normally needs three.
        var minNights = req.MinNightsByDay.TryGetValue(req.CheckIn, out var own) && own > 0
            ? own
            : l.MinNights;
        if (nights < minNights)
            return Result.Fail(Reason.NightCount, $"Chỗ nghỉ này yêu cầu tối thiểu {minNights} đêm.");

        if (l.MaxNights > 0 && nights > l.MaxNights)
            return Result.Fail(Reason.NightCount, $"Chỗ nghỉ này nhận tối đa {l.MaxNights} đêm.");

        // 7 — weekdays the host refuses to hand over or take back keys.
        if (IsBlocked(l.BlockedCheckInDays, req.CheckIn.DayOfWeek))
        {
            return Result.Fail(Reason.BlockedWeekday,
                $"Chủ nhà không nhận khách vào {DayName(req.CheckIn.DayOfWeek)}.");
        }
        if (IsBlocked(l.BlockedCheckOutDays, req.CheckOut.DayOfWeek))
        {
            return Result.Fail(Reason.BlockedWeekday,
                $"Chủ nhà không cho trả phòng vào {DayName(req.CheckOut.DayOfWeek)}.");
        }

        // 8 — every night must be free. The last night is the day before
        // check-out, so the check-out day itself is never examined.
        foreach (var o in req.Occupied)
        {
            var to = o.IsHostBlock ? o.To.AddDays(1) : o.To;   // host blocks include their end date
            if (o.From < req.CheckOut && req.CheckIn < to)
            {
                return Result.Fail(Reason.DatesTaken, o.IsHostBlock
                    ? "Chủ nhà đã khoá lịch trong khoảng ngày này."
                    : "Khoảng ngày này đã có người đặt. Vui lòng chọn ngày khác.");
            }
        }

        // 9 — cleaning gap between two stays.
        if (l.TurnoverDays > 0)
        {
            foreach (var o in req.Occupied.Where(x => !x.IsHostBlock))
            {
                var gapBefore = req.CheckIn.DayNumber - o.To.DayNumber;
                var gapAfter = o.From.DayNumber - req.CheckOut.DayNumber;

                // Zero counts: a guest arriving the day another leaves is the
                // tightest turnover there is, and ">" let exactly that through.
                if ((gapBefore >= 0 && gapBefore < l.TurnoverDays) || (gapAfter >= 0 && gapAfter < l.TurnoverDays))
                {
                    return Result.Fail(Reason.TurnoverTime,
                        $"Chủ nhà cần {l.TurnoverDays} ngày dọn dẹp giữa hai lượt khách.");
                }
            }
        }

        return Result.Pass;
    }

    /// <summary>Weekdays are stored as a bitmask so a listing needs one column, not seven.</summary>
    public static bool IsBlocked(int mask, DayOfWeek day) => (mask & (1 << (int)day)) != 0;

    public static int MaskOf(params DayOfWeek[] days) =>
        days.Aggregate(0, (mask, d) => mask | (1 << (int)d));

    private static string Describe(int hours) => hours switch
    {
        < 24 => $"{hours} giờ",
        24 => "1 ngày",
        _ => $"{hours / 24} ngày"
    };

    private static string DayName(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "thứ Hai",
        DayOfWeek.Tuesday => "thứ Ba",
        DayOfWeek.Wednesday => "thứ Tư",
        DayOfWeek.Thursday => "thứ Năm",
        DayOfWeek.Friday => "thứ Sáu",
        DayOfWeek.Saturday => "thứ Bảy",
        _ => "Chủ nhật"
    };
}
