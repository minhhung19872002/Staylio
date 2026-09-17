namespace StayHost.Domain;

public enum UserRole
{
    Guest = 0,
    Host = 1,
    Admin = 2
}

/// <summary>
/// docs/00 §3.4 — what an admin is allowed to touch. Anything above the role
/// they hold is refused, and every action is written to the audit log.
/// </summary>
[Flags]
public enum AdminScope
{
    None = 0,
    /// <summary>Read everything, answer tickets.</summary>
    Support = 1,
    /// <summary>Approve or take down listings and reviews.</summary>
    Moderation = 2,
    /// <summary>Refunds, reconciliation, payouts, fee and tax configuration.</summary>
    Finance = 4,
    /// <summary>Rule on disputes.</summary>
    Arbitration = 8,
    /// <summary>Everything, including granting scopes.</summary>
    Super = Support | Moderation | Finance | Arbitration | 16
}

/// <summary>
/// docs/00 §3.4 and docs/01 QT-09 — "mọi hành động của admin phải để lại dấu
/// vết ai làm, lúc nào, trước/sau ra sao". Append-only, like the ledger.
/// </summary>
public class AdminAuditEntry
{
    public long Id { get; set; }

    public int ActorUserId { get; set; }
    public User? ActorUser { get; set; }

    /// <summary>What was done: "listing.publish", "case.decide", "fees.update".</summary>
    public string Action { get; set; } = "";
    /// <summary>What it was done to: "listing:12", "case:4".</summary>
    public string Target { get; set; } = "";

    public string? Before { get; set; }
    public string? After { get; set; }
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A real account. Guests become hosts by publishing their first listing.</summary>
public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Initials { get; set; } = "";
    public string? Phone { get; set; }

    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";

    public UserRole Role { get; set; } = UserRole.Guest;
    /// <summary>Meaningful only when <see cref="Role"/> is Admin (docs/00 §3.4).</summary>
    public AdminScope AdminScope { get; set; } = AdminScope.None;
    public bool IsIdentityVerified { get; set; }
    public bool EmailConfirmed { get; set; }

    /// <summary>docs/01 TK-01 — the phone was verified with a six-digit code.</summary>
    public bool PhoneConfirmed { get; set; }

    /// <summary>docs/01 TK-03 — nobody under 18 may hold an account.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>docs/01 TK-02 — Google, Apple or Facebook accounts attached to this one.</summary>
    public List<ExternalLogin> ExternalLogins { get; set; } = [];

    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// docs/01 TK-04 — what they chose to be called. The full name on the
    /// account is legal identity; this is the one strangers see.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>docs/01 TK-04 — comma-separated language codes, see <see cref="Profiles"/>.</summary>
    public string SpokenLanguages { get; set; } = "";

    /// <summary>docs/01 TK-04 — where they live, in their own words.</summary>
    public string? Location { get; set; }

    public string? Occupation { get; set; }

    /// <summary>docs/01 TK-04 — newline-separated, because an interest may hold a comma.</summary>
    public string? Interests { get; set; }

    /// <summary>
    /// docs/01 XH-02 — who may see this person's journey map. Friends by default:
    /// the social features are opt-in-ish but not broadcast to strangers.
    /// </summary>
    public JourneyVisibility JourneyVisibility { get; set; } = JourneyVisibility.Friends;

    /// <summary>
    /// docs/01 TK-07 — a company email, proved with a code, so business travellers
    /// carry a work-verified badge. Held separately from the sign-in email; a free
    /// consumer mailbox never earns it (<see cref="Domain.WorkEmail"/>).
    /// </summary>
    public string? WorkEmail { get; set; }
    public bool WorkEmailConfirmed { get; set; }

    /// <summary>
    /// docs/01 TK-09 (P0) — the display preferences, on the account where they
    /// survive a new device. Null means "never chosen": the client keeps doing
    /// what it does today (its own localStorage and the device clock), so an
    /// account from before these columns behaves exactly as before. Validated
    /// through <see cref="Locales"/>; Currency against exchange_rates.
    ///
    /// The server-side READER of Language — emails composed in the guest's own
    /// language — is the large remaining half of TK-09 and is deliberately not
    /// claimed by these columns existing. Counting a stored-but-unread
    /// preference as the feature would be the YT-08 lesson again.
    /// </summary>
    public string? Language { get; set; }
    public string? Currency { get; set; }
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// docs/01 TK-13 — who to reach if something goes wrong on a trip. Private to
    /// the account; surfaced to support only when an incident is open.
    /// </summary>
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; }

    /// <summary>
    /// docs/01 TK-10 — the whole notification matrix as one bitmask. Read it
    /// through <see cref="NotificationPrefs"/>, never directly.
    /// </summary>
    public int NotificationMask { get; set; } = NotificationPrefs.Defaults();

    /// <summary>
    /// docs/01 TK-08 — a six-digit code is asked for after the password. Which
    /// identifier it goes to is <see cref="TwoFactorKind"/>.
    /// </summary>
    public bool TwoFactorEnabled { get; set; }

    public IdentifierKind TwoFactorKind { get; set; } = IdentifierKind.Email;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set when the account was created from an anonymous browsing session.</summary>
    public string? AdoptedSessionId { get; set; }

    public int? HostProfileId { get; set; }
    public HostProfile? HostProfile { get; set; }

    public List<AuthSession> Sessions { get; set; } = [];

    /* ------------------------------------------------------- docs/08 §5 */

    /// <summary>
    /// docs/08 §5.3 and §5.4 — set while the account is locked out. The reason
    /// and the paperwork live on the <see cref="Sanction"/> row; this is the flag
    /// the sign-in path reads, so a lock is one field and not a search.
    /// </summary>
    public bool IsSuspended { get; set; }

    /// <summary>Null on a permanent ban: it does not end.</summary>
    public DateTime? SuspendedUntil { get; set; }

    public bool IsBanned { get; set; }

    /// <summary>
    /// docs/01 TK-12 — the person stepped away from their own account. Kept
    /// apart from <see cref="IsSuspended"/> on purpose: one is a decision the
    /// platform made about somebody and carries a policy and an appeal, the
    /// other is somebody's own choice and ends whenever they sign back in
    /// (<see cref="AccountPause"/>). Storing both in one flag would make the two
    /// unreadable in the admin console.
    /// </summary>
    public DateTime? PausedAt { get; set; }

    /// <summary>
    /// docs/08 §6 — "giữ tài khoản mở đủ để họ phản hồi, không được cắt quyền tự
    /// vệ". A suspended account with this set can still reach its own disputes.
    /// </summary>
    public bool MayStillRespondToDisputes { get; set; }

    /// <summary>docs/08 §5.2 — the active restrictions, as one bitmask.</summary>
    public int RestrictionMask { get; set; }

    /// <summary>docs/08 §9 — set once the account has been anonymised.</summary>
    public DateTime? ErasedAt { get; set; }

    /// <summary>docs/08 §3 — last time this admin did anything, for the quarterly review.</summary>
    public DateTime? AdminLastActiveAt { get; set; }

    /// <summary>docs/08 §3 — when their permissions were last looked over.</summary>
    public DateOnly? AdminAccessReviewedOn { get; set; }
}

public enum TokenPurpose
{
    PasswordReset = 0,
    EmailVerification = 1,
    /// <summary>
    /// docs/01 TK-08 — hands out no access on its own. It says only "this
    /// browser got the password right" while the second factor is being typed.
    /// </summary>
    TwoFactorChallenge = 2,
    /// <summary>
    /// docs/08 §8 — sent with a suspension notice so somebody who can no longer
    /// sign in can still file their one appeal. Opens nothing else.
    /// </summary>
    AppealAccess = 3
}

/// <summary>Single-use, short-lived token for password resets and email verification.</summary>
public class UserToken
{
    public int Id { get; set; }
    public string Token { get; set; } = "";
    public int UserId { get; set; }
    public User? User { get; set; }
    public TokenPurpose Purpose { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(2);
    public DateTime? UsedAt { get; set; }
}

/// <summary>Opaque bearer token stored in an HttpOnly cookie.</summary>
public class AuthSession
{
    public int Id { get; set; }
    public string Token { get; set; } = "";
    public int UserId { get; set; }
    public User? User { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(30);
    public string? UserAgent { get; set; }
    /// <summary>docs/08 §4 — shown on the admin profile page next to the device.</summary>
    public string? IpAddress { get; set; }
    /// <summary>
    /// docs/08 §3 QT-A — written only for admin sessions, which idle out after 30
    /// minutes. Regular users keep the flat 30-day expiry and never touch this.
    /// </summary>
    public DateTime? LastSeenAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}

public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Captured = 2,
    Refunded = 3,
    Failed = 4
}

public enum PayoutStatus
{
    Scheduled = 0,
    /// <summary>The bank executed the transfer. Only this means the host has the money.</summary>
    Paid = 1,
    OnHold = 2,
    /// <summary>
    /// docs/07 §13 — lined up in a <see cref="PayoutBatch"/> and waiting on a
    /// person to put the file through internet banking.
    ///
    /// It is a state of its own rather than an early <see cref="Paid"/> because
    /// the difference is whether the money is still the platform's. A booking
    /// here has been decided and not yet sent, so nothing is posted to the ledger
    /// for it and the host is told "đã lên lệnh", not "đã chuyển".
    /// </summary>
    Sent = 3
}

/// <summary>
/// Money movement for one booking. The demo has no real gateway, but the record
/// carries everything a gateway integration would need to reconcile against.
/// </summary>
public class Payment
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";

    public int BookingId { get; set; }
    public Booking? Booking { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Method { get; set; } = "card";
    public string? CardLast4 { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CapturedAt { get; set; }

    /// <summary>What the platform keeps; the rest is paid out to the host.</summary>
    public decimal PlatformFee { get; set; }
    public decimal HostPayout { get; set; }
    public PayoutStatus PayoutStatus { get; set; } = PayoutStatus.Scheduled;
    public DateOnly? PayoutDueOn { get; set; }

    /// <summary>docs/07 §12.4 — why this one is not going out, so the host can be told.</summary>
    public PayoutHoldReason PayoutHoldReason { get; set; }

    /// <summary>docs/07 §12.5 — how many times the transfer has been tried.</summary>
    public int PayoutAttempts { get; set; }

    public DateOnly? PayoutLastAttemptOn { get; set; }
    public DateTime? PaidOutAt { get; set; }

    /// <summary>
    /// docs/07 §12.3 — several bookings go out as one transfer to save the fee,
    /// so each one records which transfer carried it and the report can still be
    /// read per booking.
    /// </summary>
    public string? PayoutReference { get; set; }

    /// <summary>
    /// docs/07 §17.4 — this booking's share of what was kept back against the
    /// host's debt. What actually reached the bank is <see cref="HostPayout"/>
    /// minus this, and a host reconciling a statement needs the difference named.
    /// </summary>
    public decimal PayoutDeducted { get; set; }

    /// <summary>
    /// docs/02 G8, docs/07 §19 — the part of <see cref="HostPayout"/> that went to
    /// co-hosts rather than to the owner, decided when the transfer was decided.
    ///
    /// The owner's transfer and their ledger posting are both this much smaller.
    /// It is recorded on the payment rather than recomputed at settle time
    /// because the two transfers can settle days apart, and terms that changed in
    /// between must not retroactively move money that has already left.
    /// </summary>
    public decimal CoHostShare { get; set; }
}

/// <summary>One conversation, always anchored to a listing and optionally a booking.</summary>
public class MessageThread
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public int GuestUserId { get; set; }
    public User? GuestUser { get; set; }

    public int HostUserId { get; set; }
    public User? HostUser { get; set; }

    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    /// <summary>docs/01 TN-05 — archived independently by each side of the thread.</summary>
    public bool ArchivedByGuest { get; set; }
    public bool ArchivedByHost { get; set; }

    public List<Message> Messages { get; set; } = [];
}

/// <summary>
/// docs/01 AT-10 — one person has blocked another. The pair is directional (who
/// blocked whom) but its effect on messaging is mutual: once a block exists in
/// either direction, neither side can send the other a new message.
/// </summary>
public class UserBlock
{
    public int Id { get; set; }
    public int BlockerUserId { get; set; }
    public User? Blocker { get; set; }
    public int BlockedUserId { get; set; }
    public User? Blocked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// docs/01 TM-23 — a search a guest asked to be told about. The filters are kept
/// as columns rather than a blob so the sweep can query them; dates are left out
/// on purpose, because a brand-new listing has an open calendar and "new place
/// that matches" is about the place, not a particular week.
/// <see cref="LastNotifiedListingId"/> is a high-water mark: only listings created
/// after it can trigger the next alert, so nobody is told twice about the same one.
/// </summary>
public class SavedSearch
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public string Label { get; set; } = "";
    public string? Q { get; set; }
    public string? Category { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int Guests { get; set; }
    public string? AmenitiesCsv { get; set; }
    public string? RoomType { get; set; }
    public int Bedrooms { get; set; }
    public bool SuperhostOnly { get; set; }
    public bool InstantBookOnly { get; set; }
    public string? HostLanguagesCsv { get; set; }

    public int LastNotifiedListingId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckedAt { get; set; }
}

public class Message
{
    public int Id { get; set; }
    public int ThreadId { get; set; }
    public MessageThread? Thread { get; set; }

    public int SenderUserId { get; set; }
    public User? SenderUser { get; set; }

    public string Body { get; set; } = "";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }

    /// <summary>
    /// docs/01 TN-04 — a line the platform wrote itself (order confirmed,
    /// cancelled, check-in tomorrow). Rendered differently and never masked.
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>docs/01 TN-02 — photo URLs sent with the message, newline-separated.</summary>
    public string Attachments { get; set; } = "";
}

/// <summary>
/// docs/01 TN-08 — a phrase a host reuses often enough to be worth saving.
/// Personal to the host; never shown to guests as a list.
/// </summary>
public class QuickReply
{
    public int Id { get; set; }
    public int HostUserId { get; set; }
    public User? HostUser { get; set; }

    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Which tier of docs/03 §1 step 1 a rule belongs to. A day override beats a
/// season, and both beat the weekend and base rates.
/// </summary>
public enum PriceRuleKind
{
    /// <summary>A season the host named, e.g. "Tết Nguyên đán".</summary>
    Season = 0,
    /// <summary>A price the host set on specific days from the calendar grid.</summary>
    DayOverride = 1,
    /// <summary>
    /// A minimum stay the host set on specific days without touching the price.
    /// Its <see cref="PriceRule.NightlyRate"/> means nothing and is never read
    /// for pricing (<c>Pricing.RateFor</c> only looks at the other two kinds).
    /// </summary>
    MinStay = 2
}

public class PriceRule
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public string Name { get; set; } = "";
    public PriceRuleKind Kind { get; set; } = PriceRuleKind.Season;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    /// <summary>Flat nightly rate that replaces the base price inside the window.</summary>
    public decimal NightlyRate { get; set; }
    /// <summary>
    /// Minimum nights for stays starting inside this window, overriding the
    /// listing's own minimum (docs/03 §2 step 6). Null leaves it unchanged.
    /// </summary>
    public int? MinNights { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Review a host leaves about a guest after checkout.</summary>
public class GuestReview
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking? Booking { get; set; }

    public int HostUserId { get; set; }
    public User? HostUser { get; set; }

    public int GuestUserId { get; set; }
    public User? GuestUser { get; set; }

    public double Rating { get; set; } = 5;

    /// <summary>
    /// docs/03 §7 — "Chủ nhà chấm khách: sạch sẽ, giao tiếp, tuân thủ nội quy".
    /// Null on reviews written before the three headings existed; the overall
    /// rating is their average whenever they are given.
    /// </summary>
    public int? Cleanliness { get; set; }
    public int? Communication { get; set; }
    public int? HouseRules { get; set; }

    public string Text { get; set; } = "";
    public bool WouldHostAgain { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// docs/03 §7 — blind until the other side also submits, or the 14-day
    /// window runs out. Null means written but not yet visible to anyone.
    /// </summary>
    public DateTime? PublishedAt { get; set; }
}

/// <summary>Host-side calendar block that is not backed by a booking (maintenance, own stay).</summary>
public class CalendarBlock
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public Listing? Listing { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// Set when the block came from an imported calendar (docs/01 QL-10). A sync
    /// only ever replaces its own feed's blocks, never the ones the host made.
    /// </summary>
    public int? FeedId { get; set; }
    public CalendarFeed? Feed { get; set; }

    /// <summary>The UID of the event upstream, so a re-sync updates instead of duplicating.</summary>
    public string? ExternalUid { get; set; }

    /// <summary>
    /// docs/03 §4 — the nights of a stay the host cancelled carry this prefix on
    /// <see cref="ExternalUid"/>: "những ngày đó bị chặn, không cho đặt lại". The
    /// host cannot lift them from the calendar.
    /// </summary>
    public const string HostCancelPrefix = "host-cancel:";

    public bool IsHostCancelPenalty => ExternalUid?.StartsWith(HostCancelPrefix) == true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
