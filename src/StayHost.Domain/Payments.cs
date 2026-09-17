namespace StayHost.Domain;

/// <summary>
/// docs/07 §8 — why a charge was refused, in terms the guest can act on.
///
/// The bank's own code never reaches the screen (docs/07 §8, last line): it
/// tells the guest nothing and tells a card tester everything.
/// </summary>
public enum DeclineReason
{
    /// <summary>Anything the gateway did not explain. Retryable, because guessing "no" is worse.</summary>
    Unknown = 0,
    InsufficientFunds = 1,
    ExpiredCard = 2,
    IncorrectDetails = 3,
    BankRefused = 4,
    LimitExceeded = 5,
    /// <summary>Very common on Vietnamese domestic cards.</summary>
    OnlinePaymentsOff = 6,
    SuspectedFraud = 7,
    GatewayError = 8,
    /// <summary>
    /// Not the bank's answer but the platform's: nothing on this site can take
    /// money by that method right now, so nothing was attempted.
    /// </summary>
    MethodUnavailable = 9
}

/// <summary>Where one attempt at taking money got to.</summary>
public enum PaymentAttemptStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}

/// <summary>
/// docs/07 §7 — one attempt at taking money, keyed so the same attempt cannot
/// be made twice.
///
/// "Mỗi yêu cầu thu tiền phải có mã chống trùng. Nếu vì lỗi mạng mà cùng một
/// yêu cầu bị gửi hai lần, chỉ được trừ tiền một lần. Đây là lỗi nghiêm trọng
/// nhất trong module thanh toán."
/// </summary>
public class PaymentAttempt
{
    public long Id { get; set; }

    /// <summary>Unique. Two requests carrying the same key are one attempt.</summary>
    public string Key { get; set; } = "";

    public int BookingId { get; set; }
    public Booking? Booking { get; set; }

    public decimal Amount { get; set; }
    public string Method { get; set; } = "card";
    public string? CardLast4 { get; set; }

    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Pending;
    public DeclineReason Reason { get; set; }

    /// <summary>What the guest was told. Kept so a retry of the same key says the same thing.</summary>
    public string? Message { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

/// <summary>docs/07 §7 and §8 — the rules around taking money, minus the gateway.</summary>
public static class Payments
{
    /* ------------------------------------------------ docs/07 §8, the limit */

    /// <summary>"tối đa 5 lần thử thất bại trên một đơn trong 1 giờ".</summary>
    public const int MaxFailuresPerHour = 5;

    public static readonly TimeSpan FailureWindow = TimeSpan.FromHours(1);

    /// <summary>Bank refusals get fewer goes than a mistyped card number.</summary>
    public const int MaxBankRefusals = 2;

    public static bool LockedOut(int failuresInWindow) => failuresInWindow >= MaxFailuresPerHour;

    public static string LockedOutMessage() =>
        $"Đã thử thanh toán không thành công {MaxFailuresPerHour} lần. " +
        "Vui lòng liên hệ hỗ trợ để tiếp tục.";

    /// <summary>
    /// docs/02 F1 — what a payment's state is called on the guest's own history.
    /// Named here, once, because a screen and an email must not each invent a
    /// wording for the same state.
    /// </summary>
    public static string StatusLabel(PaymentStatus status) => status switch
    {
        PaymentStatus.Captured => "Đã thanh toán",
        PaymentStatus.Refunded => "Đã hoàn tiền",
        PaymentStatus.Authorized => "Đã giữ tiền",
        PaymentStatus.Failed => "Không thành công",
        _ => "Đang chờ thanh toán"
    };

    /* ------------------------------------------- docs/07 §8, the whole table */

    /// <summary>What the guest is told. Never a bank code, always a next step.</summary>
    public static string Message(DeclineReason reason) => reason switch
    {
        DeclineReason.InsufficientFunds => "Thẻ không đủ số dư cho khoản này. Bạn thử thẻ khác nhé.",
        DeclineReason.ExpiredCard => "Thẻ đã hết hạn. Vui lòng dùng thẻ khác.",
        DeclineReason.IncorrectDetails => "Thông tin thẻ chưa đúng. Kiểm tra lại số thẻ và ngày hết hạn.",
        DeclineReason.BankRefused => "Ngân hàng phát hành đã từ chối. Liên hệ ngân hàng hoặc dùng thẻ khác.",
        DeclineReason.LimitExceeded => "Giao dịch vượt hạn mức của thẻ. Thử thẻ khác hoặc liên hệ ngân hàng.",
        DeclineReason.OnlinePaymentsOff => "Thẻ chưa mở thanh toán trực tuyến. Bạn mở trong ứng dụng ngân hàng rồi thử lại.",
        DeclineReason.SuspectedFraud => "Giao dịch cần xác minh thêm. Bộ phận hỗ trợ sẽ liên hệ với bạn.",
        DeclineReason.GatewayError => "Hệ thống thanh toán đang bận. Vui lòng thử lại sau ít phút.",
        DeclineReason.MethodUnavailable => "Cách thanh toán này hiện chưa dùng được. Bạn chọn thẻ hoặc ví điện tử khác nhé.",
        _ => "Chưa thanh toán được. Bạn thử lại hoặc dùng phương thức khác nhé."
    };

    /// <summary>
    /// docs/07 §8 — whether the same card is worth another go. A card that has
    /// expired or is being investigated will not start working on a retry, and
    /// saying "thử lại" there only wastes the guest's time.
    /// </summary>
    public static bool Retryable(DeclineReason reason) =>
        reason is not (DeclineReason.ExpiredCard or DeclineReason.SuspectedFraud or DeclineReason.MethodUnavailable);

    /// <summary>True when the guest should be pushed to a different method rather than the same one.</summary>
    public static bool NeedsDifferentMethod(DeclineReason reason) =>
        reason is DeclineReason.ExpiredCard or DeclineReason.InsufficientFunds or DeclineReason.LimitExceeded
            or DeclineReason.MethodUnavailable;

    /* --------------------------------------- docs/07 §7, the anti-double key */

    /// <summary>
    /// The key a client should send when it has none of its own: one attempt per
    /// booking per amount. A retry of the same payment reuses it and is refused
    /// a second charge; a genuinely different amount — a deposit after a full
    /// payment failed — is a different attempt.
    /// </summary>
    public static string KeyFor(int bookingId, decimal amount, string method) =>
        $"booking:{bookingId}:{amount:0.##}:{method.ToLowerInvariant()}";

    /// <summary>
    /// A client-supplied key is trusted only in shape, never in content: it is
    /// namespaced by the booking so one guest cannot replay another's key.
    /// </summary>
    public static string NamespaceKey(int bookingId, string? clientKey, decimal amount, string method)
    {
        var trimmed = (clientKey ?? "").Trim();
        if (trimmed.Length is 0 or > 100) return KeyFor(bookingId, amount, method);

        var safe = new string(trimmed.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return safe.Length == 0 ? KeyFor(bookingId, amount, method) : $"booking:{bookingId}:{safe}";
    }
}

/// <summary>
/// docs/07 §7 — the gateway's own record of a charge, as the platform sees it.
///
/// It is written by <c>PaymentGateway</c> and by nothing else, the way a mirror
/// of a PSP's webhook feed would be. Keeping it apart from <see cref="Payment"/>
/// is the whole point: the daily reconciliation compares two independent
/// records, and two records that are the same row cannot disagree.
/// </summary>
public class GatewayCharge
{
    public long Id { get; set; }

    /// <summary>The attempt key, which is what both sides know the charge by.</summary>
    public string Reference { get; set; } = "";

    public decimal Amount { get; set; }
    public string Method { get; set; } = "card";
    public DateTime ChargedAt { get; set; } = DateTime.UtcNow;
}
