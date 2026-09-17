namespace StayHost.Domain;

/// <summary>docs/08 §6 — what happens to one live booking when an account is locked.</summary>
public enum BookingFallout
{
    /// <summary>Somebody is asleep in that bed tonight. Nothing happens to it.</summary>
    LeaveAlone = 0,
    /// <summary>Cancelled by the platform, everything back, no penalty on the host.</summary>
    CancelRefundFull = 1,
    /// <summary>Cancelled under the cancellation policy the guest agreed to.</summary>
    CancelPerPolicy = 2,
    /// <summary>A request nobody will ever answer now.</summary>
    CancelRequest = 3,
    /// <summary>Money already earned, kept back until the case is settled.</summary>
    HoldPayout = 4
}

/// <summary>
/// docs/08 §6 — "Đây là chỗ dễ gây thiệt hại nhất."
///
/// Locking an account is an administrative act; being mid-holiday is not. The
/// rule the whole section reduces to is its last line: "người đang trong chuyến
/// đi không bị bỏ rơi giữa đường vì quyết định hành chính." Everything here
/// exists so an admin sees the cost before they click, which is what QT-U-07
/// asks for.
/// </summary>
public static class SuspensionImpact
{
    /// <summary>One booking as this calculation needs to see it.</summary>
    public readonly record struct Booking(
        int Id,
        string Reference,
        BookingStatus Status,
        DateOnly CheckIn,
        DateOnly CheckOut,
        decimal GuestPaid,
        decimal HostPayout,
        string CounterpartyName,
        bool PayoutAlreadySent,
        bool HasOpenDispute);

    public readonly record struct Line(
        int BookingId, string Reference, BookingFallout Action, decimal Money, string CounterpartyName, string Note);

    public readonly record struct Preview(
        IReadOnlyList<Line> Lines,
        int GuestsStaying,
        int BookingsCancelled,
        decimal MoneyRefunded,
        decimal PayoutHeld,
        bool AnyOpenDispute)
    {
        public bool Nothing => Lines.Count == 0;

        /// <summary>The sentence on the confirmation screen, before anything is done.</summary>
        public string Warning =>
            Nothing
                ? "Tài khoản này không có đơn nào đang chạy."
                : $"Sẽ ảnh hưởng {Lines.Count} đơn: {BookingsCancelled} đơn bị huỷ, " +
                  $"hoàn {MoneyRefunded:#,##0}₫ cho khách" +
                  (PayoutHeld > 0 ? $", giữ lại {PayoutHeld:#,##0}₫ chưa chuyển" : "") +
                  (GuestsStaying > 0 ? $". {GuestsStaying} khách đang ở sẽ KHÔNG bị đụng tới" : "") +
                  ".";
    }

    /* --------------------------------------------------- locking a host */

    /// <summary>
    /// docs/08 §6, "Khoá một chủ nhà". The guest who is already there stays; the
    /// guest who has not arrived gets everything back; and the host is not fined
    /// for a cancellation the platform made — that is the trap this table exists
    /// to avoid.
    /// </summary>
    public static Preview ForHost(IEnumerable<Booking> bookings) => Build(bookings, isHost: true, refundInFull: true);

    /* -------------------------------------------------- locking a guest */

    /// <param name="refundInFull">
    /// docs/08 §6, "Khoá một khách" — the one place the table leaves a choice:
    /// refund under the policy, or in full, "tuỳ mức độ vi phạm — admin chọn và
    /// ghi lý do".
    /// </param>
    public static Preview ForGuest(IEnumerable<Booking> bookings, bool refundInFull) =>
        Build(bookings, isHost: false, refundInFull);

    private static Preview Build(IEnumerable<Booking> bookings, bool isHost, bool refundInFull)
    {
        var lines = new List<Line>();
        var staying = 0;
        var cancelled = 0;
        var refunded = 0m;
        var held = 0m;
        var disputed = false;

        foreach (var b in bookings)
        {
            if (b.HasOpenDispute) disputed = true;

            // The whole point of §6: a stay under way is not touched by either
            // table. Somebody is in that house tonight.
            if (b.Status == BookingStatus.InProgress)
            {
                staying++;
                lines.Add(new Line(b.Id, b.Reference, BookingFallout.LeaveAlone, 0m, b.CounterpartyName,
                    isHost
                        ? "Khách đang ở — để khách ở hết kỳ."
                        : "Đang ở — để ở hết kỳ, trừ khi có nguy hiểm cho chủ nhà."));
                continue;
            }

            if (b.Status == BookingStatus.Confirmed)
            {
                var full = isHost || refundInFull;
                var money = full ? b.GuestPaid : 0m;   // the policy figure is Cancellation's to work out

                cancelled++;
                refunded += money;

                lines.Add(new Line(b.Id, b.Reference,
                    full ? BookingFallout.CancelRefundFull : BookingFallout.CancelPerPolicy,
                    money, b.CounterpartyName,
                    isHost
                        ? "Huỷ, hoàn 100% cho khách, không tính phạt huỷ cho chủ nhà, gửi khách chỗ thay thế."
                        : full
                            ? "Huỷ, hoàn 100% cho khách."
                            : "Huỷ, hoàn theo chính sách huỷ đã áp dụng cho đơn."));
                continue;
            }

            if (b.Status == BookingStatus.PendingHostApproval || b.Status == BookingStatus.PendingPayment)
            {
                cancelled++;
                lines.Add(new Line(b.Id, b.Reference, BookingFallout.CancelRequest, 0m, b.CounterpartyName,
                    "Yêu cầu chưa được duyệt — tự động huỷ và báo khách."));
                continue;
            }

            // Completed, but the money has not gone out yet. docs/08 §6 —
            // "Giữ lại cho tới khi xử lý xong vi phạm; không tịch thu tự động."
            if (isHost && b.Status == BookingStatus.Completed && !b.PayoutAlreadySent && b.HostPayout > 0)
            {
                held += b.HostPayout;
                lines.Add(new Line(b.Id, b.Reference, BookingFallout.HoldPayout, b.HostPayout, b.CounterpartyName,
                    "Giữ lại cho tới khi xử lý xong vi phạm. Không tịch thu."));
            }
        }

        return new Preview(lines, staying, cancelled, refunded, held, disputed);
    }

    /// <summary>
    /// docs/08 §6 — somebody who hosts and also travels is locked on both
    /// tables at once. Only the host table used to be read for them, so the
    /// trips they had booked as a guest carried on as if nothing happened.
    /// </summary>
    public static Preview Combine(Preview asHost, Preview asGuest) => new(
        [.. asHost.Lines, .. asGuest.Lines],
        asHost.GuestsStaying + asGuest.GuestsStaying,
        asHost.BookingsCancelled + asGuest.BookingsCancelled,
        asHost.MoneyRefunded + asGuest.MoneyRefunded,
        asHost.PayoutHeld + asGuest.PayoutHeld,
        asHost.AnyOpenDispute || asGuest.AnyOpenDispute);

    /* ------------------------------------------------------ the guardrails */

    /// <summary>
    /// docs/08 §6 — "Có tranh chấp đang mở: giữ tài khoản mở đủ để họ phản hồi,
    /// không được cắt quyền tự vệ." Suspending somebody out of a case they are a
    /// party to decides it against them without hearing them.
    /// </summary>
    public static bool MustKeepAbleToRespond(Preview preview) => preview.AnyOpenDispute;

    public static string OpenDisputeNotice() =>
        "Người này đang có tranh chấp mở. Tài khoản vẫn phải vào được phần hồ sơ tranh chấp để phản hồi — " +
        "không được cắt quyền tự vệ.";

    /// <summary>
    /// docs/08 §6 — the exception under the host table: a safety case moves the
    /// guest out now, and Staylio Shield pays for it (docs/06 §2.3).
    /// </summary>
    public static string SafetyRelocationNotice(int guestsStaying) =>
        guestsStaying == 0
            ? ""
            : $"Vi phạm liên quan an toàn: {guestsStaying} khách đang ở cần được chuyển sang chỗ khác ngay, " +
              "chi phí theo Staylio Shield (docs/06 §2.3).";

    /// <summary>docs/08 §6, "Số dư khuyến mãi: đóng băng, không xoá."</summary>
    public static string BalanceNotice() => "Số dư khuyến mãi bị đóng băng, không bị xoá.";
}
