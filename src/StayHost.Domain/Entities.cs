namespace StayHost.Domain;

/// <summary>Kind of place, mirrors the category strip on the browse page.</summary>
public enum PlaceType
{
    Villa = 1,
    Apartment = 2,
    Homestay = 3,
    House = 4,
    Cabin = 5,
    Boutique = 6,
    /// <summary>docs/01 MR-08 — one property with several kinds of room.</summary>
    Hotel = 7
}

/// <summary>Whether the guest books the whole place or shares it.</summary>
public enum RoomType
{
    EntirePlace = 1,
    PrivateRoom = 2,
    SharedRoom = 3
}

/// <summary>
/// The ten states of docs/03 §3. Only the arrows drawn in that diagram are
/// legal; <see cref="BookingLifecycle"/> owns the transition table.
/// </summary>
public enum BookingStatus
{
    /// <summary>Request-to-book waiting on the host. Does not hold the dates.</summary>
    PendingHostApproval = 0,
    /// <summary>Dates are held for 15 minutes while the guest pays.</summary>
    PendingPayment = 1,
    Confirmed = 2,
    /// <summary>Check-in has passed in the listing's own time zone.</summary>
    InProgress = 3,
    /// <summary>Check-out has passed; reviews and the host payout unlock here.</summary>
    Completed = 4,

    Declined = 5,
    /// <summary>The host did not answer the request within 24 hours.</summary>
    Expired = 6,
    /// <summary>Payment failed, or the 15-minute hold ran out.</summary>
    PaymentFailed = 7,
    CancelledByGuest = 8,
    CancelledByHost = 9
}

/// <summary>
/// The six policies of docs/03 §4. <see cref="Cancellation"/> owns the refund
/// maths; this enum only names the choice a host makes per listing.
/// </summary>
public enum CancellationTier
{
    /// <summary>Full refund up to 24h before check-in, then the first night is lost.</summary>
    Flexible = 0,
    /// <summary>Full refund up to 5 days before check-in, then 50% of the room rate.</summary>
    Moderate = 1,
    /// <summary>100% before 30 days, 50% before 7 days, nothing after.</summary>
    Strict = 2,
    /// <summary>50% up to 7 days before; nothing after.</summary>
    SuperStrict = 3,
    /// <summary>No room refund at any point, in exchange for a lower price.</summary>
    NonRefundable = 4,
    /// <summary>For stays of 28 nights or more: after 30 days out, the guest pays the first 30 nights.</summary>
    LongTermStrict = 5
}

/// <summary>docs/03 §5 — how often the platform sends a host their money.</summary>
public enum PayoutSchedule
{
    /// <summary>24 hours after each guest checks in.</summary>
    PerBooking = 0,
    /// <summary>Batched once a week.</summary>
    Weekly = 1,
    /// <summary>Batched once a month; required for stays of 28 nights or more.</summary>
    Monthly = 2
}

/// <summary>Public-facing host identity. The login account itself is <see cref="User"/>.</summary>
public class HostProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Initials { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public bool IsSuperhost { get; set; }

    /// <summary>
    /// docs/03 §8 — the first day of the quarter the title was last decided in.
    /// Null means never decided; older than this quarter means it is due again.
    /// </summary>
    public DateOnly? SuperhostReviewedOn { get; set; }

    public int YearsHosting { get; set; }
    public string? Bio { get; set; }
    public string ResponseRate { get; set; } = "100%";
    public string ResponseTime { get; set; } = "trong vòng 1 giờ";
    public DateTime JoinedAt { get; set; } = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Null for the seeded demo hosts; set for every host created from a real account.</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    // --- docs/01 QL-20: where the money goes and how often.
    public string? PayoutBankName { get; set; }
    public string? PayoutAccountName { get; set; }
    /// <summary>Only the last four digits are ever displayed (docs/07 §14.3).</summary>
    public string? PayoutAccountLast4 { get; set; }

    /// <summary>
    /// docs/07 §14.3 — the whole account number, sealed with <see cref="SecretText"/>.
    ///
    /// It has to be kept, or the platform collects a guest's money and has no way
    /// to forward the host's share: §13's option A settles into the platform's own
    /// account and leaves the splitting to a bulk bank transfer. "Encrypted at
    /// rest, masked on screen" is the rule; for a while this build did the second
    /// half by discarding the number, which also discarded the ability to pay
    /// anyone.
    ///
    /// Null when no encryption key is configured — nothing is ever written here
    /// in the clear, and a payout with nothing to open says so rather than
    /// guessing.
    /// </summary>
    public string? PayoutAccountSealed { get; set; }
    public PayoutSchedule PayoutSchedule { get; set; } = PayoutSchedule.PerBooking;

    /// <summary>
    /// docs/07 §12.2 — when the account the money goes to was last changed.
    /// Payouts freeze for three days afterwards: somebody who has taken over an
    /// account must not be able to redirect a payout before the real host has
    /// had a chance to read the warning.
    /// </summary>
    public DateTime? PayoutAccountChangedAt { get; set; }

    /// <summary>Set once a small test transfer has proved the account is real (docs/07 §12.2).</summary>
    public bool PayoutAccountVerified { get; set; }

    /// <summary>
    /// docs/07 §17.4 — what the host still owes Staylio: a cancellation penalty,
    /// a settled claim, a chargeback arbitration found them at fault for. It comes
    /// out of the next transfer rather than being chased separately.
    /// </summary>
    public decimal OwedToPlatform { get; set; }

    public List<Listing> Listings { get; set; } = [];
}

public class Amenity
{
    public int Id { get; set; }
    /// <summary>Stable slug used by the filter query string, e.g. "pool".</summary>
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Group { get; set; } = "Tiện nghi";
    /// <summary>Shown in the filter panel when true; every amenity still renders on the detail page.</summary>
    public bool IsFilterable { get; set; }
    public int SortOrder { get; set; }

    public List<ListingAmenity> Listings { get; set; } = [];
}

public class Listing
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "Việt Nam";
    public PlaceType Type { get; set; }
    public RoomType RoomType { get; set; } = RoomType.EntirePlace;

    public int Bedrooms { get; set; }
    public int Beds { get; set; }
    public int Bathrooms { get; set; }
    public int MaxGuests { get; set; }

    public decimal PricePerNight { get; set; }
    /// <summary>0–60. When set, the card shows the pre-discount price struck through.</summary>
    public int DiscountPercent { get; set; }
    public decimal CleaningFee { get; set; } = 350_000m;
    /// <summary>Uplift applied to Friday and Saturday nights (docs/03 §1 step 1, weekend tier).</summary>
    public decimal WeekendSurchargeRate { get; set; } = 0.15m;

    // --- docs/03 §1 step 2: length-of-stay discounts. One tier applies, longer wins.
    public int WeeklyDiscountPercent { get; set; }
    public int MonthlyDiscountPercent { get; set; }

    // --- step 3: booking-time discounts. One applies, whichever is larger.
    /// <summary>Book at least this many days ahead to earn <see cref="EarlyBirdPercent"/>.</summary>
    public int EarlyBirdDays { get; set; }
    public int EarlyBirdPercent { get; set; }
    /// <summary>Book within this many days of check-in to earn <see cref="LastMinutePercent"/>.</summary>
    public int LastMinuteDays { get; set; }
    public int LastMinutePercent { get; set; }

    // --- step 5: surcharges.
    /// <summary>Guests included in the nightly rate; infants never count (docs/03 §1 step 5).</summary>
    public int FreeGuestThreshold { get; set; } = 2;
    /// <summary>Charged per extra guest, per night.</summary>
    public decimal ExtraGuestFee { get; set; }

    public bool PetsAllowed { get; set; }
    public int MaxPets { get; set; } = 2;
    public decimal PetFee { get; set; }
    /// <summary>False charges the pet fee once for the stay, true charges it nightly.</summary>
    public bool PetFeePerNight { get; set; }

    public double Rating { get; set; }
    public int ReviewCount { get; set; }
    public bool IsSuperhost { get; set; }
    public bool IsGuestFavorite { get; set; }

    /// <summary>
    /// docs/03 §8 — the Monday the "Khách chọn" title was last decided for this
    /// listing. Null means never; anything older than this week's Monday is due.
    /// </summary>
    public DateOnly? FavoriteReviewedOn { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public string Description { get; set; } = "";
    public string? SpaceHighlight { get; set; }

    /// <summary>
    /// Title, city, country and the city's common abbreviations, all lowercase
    /// and stripped of diacritics, so "da lat" and "hcm" find the right rows
    /// (docs/03 §6). Kept in sync by <see cref="RefreshSearchText"/>.
    /// </summary>
    public string SearchText { get; set; } = "";

    /// <summary>
    /// Beds per room, as JSON: <c>[{"name":"Phòng ngủ 1","beds":["Giường đôi"]}]</c>.
    /// Empty means the detail page falls back to spreading <see cref="Beds"/>
    /// evenly across <see cref="Bedrooms"/>.
    /// </summary>
    public string BedLayoutJson { get; set; } = "[]";

    public void RefreshSearchText() =>
        SearchText = Domain.SearchText.ForListing(Title, City, Country);
    public CancellationTier CancellationTier { get; set; } = CancellationTier.Moderate;
    /// <summary>
    /// Free text the host writes. The arrival hours are not in here any more —
    /// they are <see cref="CheckInFrom"/> and friends, so the listing page and
    /// the trip page cannot disagree about them (docs/01 CĐ-03).
    /// </summary>
    public string HouseRules { get; set; } = "Không hút thuốc trong nhà|Không tổ chức tiệc|Giữ yên lặng sau 22:00";

    /* ------------------------------------------- docs/01 CĐ-03 and CĐ-04 */

    public TimeOnly CheckInFrom { get; set; } = new(14, 0);
    public TimeOnly CheckInTo { get; set; } = new(22, 0);
    public TimeOnly CheckOutBefore { get; set; } = new(12, 0);

    public CheckInMethod CheckInMethod { get; set; } = CheckInMethod.Host;

    /// <summary>
    /// docs/03 §10 — the street address, released only to a guest holding a
    /// confirmed stay. <see cref="City"/> is the public half.
    /// </summary>
    public string? AddressLine { get; set; }

    /// <summary>How to find the door once they are on the street.</summary>
    public string? Directions { get; set; }

    public string? WifiName { get; set; }
    public string? WifiPassword { get; set; }

    /// <summary>Newline-separated: air conditioner, water heater, induction hob…</summary>
    public string? ApplianceNotes { get; set; }

    /// <summary>
    /// docs/01 CĐ-04 — never leaves the server until the stay is confirmed
    /// <em>and</em> check-in is inside 48 hours. See <see cref="Domain.CheckInGuide"/>.
    /// </summary>
    public string? DoorCode { get; set; }

    /// <summary>Released with the address; a guest who cannot get in needs a number to ring.</summary>
    public string? HostPhone { get; set; }
    public string SafetyInfo { get; set; } = "Có thiết bị báo khói|Có bình chữa cháy|Có bộ sơ cứu";

    /// <summary>Drafts are visible only to their host until published.</summary>
    public bool IsPublished { get; set; } = true;

    /// <summary>
    /// docs/01 AT-01 — the platform's review stance on this listing. Defaults to
    /// <see cref="ListingReviewStatus.Approved"/> so existing and seeded places
    /// are live without a back-fill; only new listings published under an active
    /// gate (<see cref="ModerationSettings.NewListingsRequireApproval"/>) start
    /// <see cref="ListingReviewStatus.Pending"/>.
    /// </summary>
    public ListingReviewStatus ReviewStatus { get; set; } = ListingReviewStatus.Approved;
    /// <summary>Reason an admin gave when rejecting, shown back to the host.</summary>
    public string? ReviewNote { get; set; }
    public DateTime? SubmittedForReviewAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }

    /// <summary>
    /// docs/08 §5.2 and §5.5 — set when a sanction unpublished this listing, so
    /// restoring the account republishes exactly these and not the ones the host
    /// had chosen to keep offline. While set, the host cannot republish.
    /// </summary>
    public DateTime? HiddenBySanctionAt { get; set; }

    /// <summary>
    /// docs/01 TK-12 — set when the host paused their own account, so coming
    /// back republishes exactly the listings the pause took down and not the
    /// drafts they had deliberately left offline.
    ///
    /// A separate column from <see cref="HiddenBySanctionAt"/> rather than a
    /// shared one: a listing can be hidden by both at once, and resuming a
    /// pause must not put a sanctioned listing back on sale.
    /// </summary>
    public DateTime? HiddenByPauseAt { get; set; }

    public bool InstantBook { get; set; } = true;

    /// <summary>
    /// docs/07 §2.5 — this host takes the money at the door for this place.
    /// Off by default and never turned on by the platform: the protection being
    /// given up is the host's own (<see cref="PayAtProperty.HostWarning"/>).
    /// </summary>
    public bool AcceptsPayAtProperty { get; set; }

    /// <summary>docs/01 ĐP-03 — instant book only for identity-verified guests.</summary>
    public bool InstantBookRequiresVerified { get; set; }
    /// <summary>docs/01 ĐP-03 — instant book only for guests with good reviews.</summary>
    public bool InstantBookRequiresGoodReviews { get; set; }

    /// <summary>docs/01 ĐP-10 — no booking at all without a profile photo.</summary>
    public bool RequireGuestPhoto { get; set; }
    /// <summary>docs/01 ĐP-10 — no booking at all without identity verification.</summary>
    public bool RequireVerifiedToBook { get; set; }

    // --- docs/03 §2: the nine checks that decide whether a stay can be booked.
    public int MinNights { get; set; } = 1;
    /// <summary>0 means no upper limit.</summary>
    public int MaxNights { get; set; }
    /// <summary>How much warning the host needs before check-in. 0 allows same-day.</summary>
    public int AdvanceNoticeHours { get; set; }
    /// <summary>Latest local hour a same-day booking may be made; null means any time.</summary>
    public int? SameDayCutoffHour { get; set; }
    /// <summary>How far ahead the calendar is open, in months. 0 means unlimited.</summary>
    public int CalendarVisibilityMonths { get; set; }
    /// <summary>Clear days the host needs between two stays.</summary>
    public int TurnoverDays { get; set; }
    /// <summary>Bitmask over <see cref="DayOfWeek"/>; see <c>Availability.MaskOf</c>.</summary>
    public int BlockedCheckInDays { get; set; }
    public int BlockedCheckOutDays { get; set; }

    /// <summary>
    /// docs/03 §3: check-in and check-out roll over in the listing's own time
    /// zone, not the guest's and not the server's.
    /// </summary>
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>
    /// Secret in the export URL of docs/01 QL-10. Other platforms poll that URL
    /// with no login, so the token is the only thing standing between this
    /// listing's calendar and anyone who guesses an id.
    /// </summary>
    public string IcalToken { get; set; } = Guid.NewGuid().ToString("N");

    public List<CalendarFeed> CalendarFeeds { get; set; } = [];

    /// <summary>
    /// docs/01 MR-08 — empty for an ordinary place. When it has rows the listing
    /// is a hotel: a guest picks a room type and the count of that type decides
    /// whether the dates are free.
    /// </summary>
    public List<RoomTypeOption> RoomTypes { get; set; } = [];

    public bool IsHotel => Type == PlaceType.Hotel;

    // --- docs/01 CN-12: what the host has to declare before publishing.
    /// <summary>Business or rental licence number, where the area requires one.</summary>
    public string? LicenseNumber { get; set; }
    /// <summary>Recording devices anywhere on the property must be disclosed.</summary>
    public bool HasSecurityCameras { get; set; }
    public string? SecurityCameraNote { get; set; }
    public bool HasWeaponsOnProperty { get; set; }
    public bool HasDangerousAnimals { get; set; }

    /// <summary>
    /// docs/01 CN-01 — a listing being built step by step. It is not a draft in
    /// the publish sense: <see cref="IsPublished"/> covers that. This marks one
    /// the host has not finished, so the wizard can pick up where it left off.
    /// </summary>
    public int WizardStep { get; set; }
    public bool IsComplete { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int HostId { get; set; }
    public HostProfile? Host { get; set; }

    public List<ListingImage> Images { get; set; } = [];
    public List<CalendarBlock> Blocks { get; set; } = [];
    public List<PriceRule> PriceRules { get; set; } = [];
    public List<ListingAmenity> Amenities { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];

    /// <summary>docs/01 TĐ-22 — the host's local recommendations for this place.</summary>
    public List<GuidebookPlace> Guidebook { get; set; } = [];

    /// <summary>
    /// docs/01 YT-08 — when the "sắp hết phòng" notice last went out, so the
    /// crossing is announced once rather than on every sweep. Cleared the moment
    /// the calendar opens up again, which arms the next crossing.
    /// </summary>
    public DateTime? LowAvailabilityNotifiedAt { get; set; }
}

public class ListingImage
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public Listing? Listing { get; set; }
    public string Url { get; set; } = "";
    public string Caption { get; set; } = "";
    public int SortOrder { get; set; }
}

public class ListingAmenity
{
    public int ListingId { get; set; }
    public Listing? Listing { get; set; }
    public int AmenityId { get; set; }
    public Amenity? Amenity { get; set; }
}

public class Review
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    /// <summary>Set when a real guest reviewed a completed stay; null for seeded reviews.</summary>
    public int? AuthorUserId { get; set; }
    public User? AuthorUser { get; set; }
    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }

    public string AuthorName { get; set; } = "";
    public string AuthorInitials { get; set; } = "";
    public string? AuthorLocation { get; set; }
    public string When { get; set; } = "";
    public string Text { get; set; } = "";
    public double Rating { get; set; } = 5;

    /// <summary>
    /// docs/01 TĐ-12 — the host's public answer, shown under the review. One
    /// reply only, within 30 days (docs/03 §7).
    /// </summary>
    public string? HostReply { get; set; }
    public DateTime? HostRepliedAt { get; set; }

    /// <summary>
    /// docs/03 §7 — reviews are blind both ways: this stays null until the host
    /// has also written one, or the 14-day window closes. Seeded reviews are
    /// published immediately.
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>docs/01 ĐG-08 — the writer may correct it inside 48 hours.</summary>
    public DateTime? EditableUntil { get; set; }

    /// <summary>docs/01 ĐG-05 — feedback for the host alone, never public.</summary>
    public string? PrivateNote { get; set; }

    // Airbnb-style per-category scores, averaged into the ratings breakdown.
    public double Cleanliness { get; set; } = 5;
    public double Accuracy { get; set; } = 5;
    public double CheckIn { get; set; } = 5;
    public double Communication { get; set; } = 5;
    public double Location { get; set; } = 5;
    public double Value { get; set; } = 5;

    /// <summary>
    /// docs/01 TĐ-11 — the language this was written in, taken from the writer's
    /// own interface at the moment they wrote it. Null on everything written
    /// before the column existed and on seeded rows;
    /// <see cref="ReviewInsights.LanguageOf"/> answers for those from the text,
    /// so the filter works over the whole set rather than only over new reviews.
    /// </summary>
    public string? Language { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A named collection of saved listings, e.g. "Chuyến đi Đà Nẵng".</summary>
public class Wishlist
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int? UserId { get; set; }
    public User? User { get; set; }
    /// <summary>The list new saves land in when the guest does not pick one.</summary>
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// docs/01 YT-05 — a token in the share link. Null until the owner turns
    /// sharing on; anyone holding it can view the list read-only, and turning
    /// sharing off drops the token so the old link stops working.
    /// </summary>
    public string? ShareToken { get; set; }

    public List<Favorite> Items { get; set; } = [];
}

/// <summary>
/// docs/01 YT-06 — a thumbs up/down from one member of a shared wishlist on one
/// place in it. The voter is a session (or an account), so people sharing a link
/// each get one vote per place; changing your mind updates the same row.
/// </summary>
public class WishlistVote
{
    public int Id { get; set; }
    public int WishlistId { get; set; }
    public Wishlist? Wishlist { get; set; }
    public int ListingId { get; set; }
    /// <summary>Session id, or "u{userId}" once signed in. One vote per voter per place.</summary>
    public string VoterKey { get; set; } = "";
    public bool Up { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Wishlist entry keyed by an anonymous browser session id.</summary>
public class Favorite
{
    public int Id { get; set; }
    public string SessionId { get; set; } = "";
    /// <summary>Set once the visitor signs in; the wishlist then follows the account.</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }
    public int ListingId { get; set; }
    public Listing? Listing { get; set; }
    public int? WishlistId { get; set; }
    public Wishlist? Wishlist { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Booking
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int? GuestUserId { get; set; }
    public User? GuestUser { get; set; }

    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    /// <summary>Adults plus children — what capacity and surcharges were priced on.</summary>
    public int Guests { get; set; }
    public int Adults { get; set; } = 1;
    public int Children { get; set; }
    public int Infants { get; set; }
    public int Pets { get; set; }
    public int Nights { get; set; }

    // The priced stay, frozen at booking time. docs/00 §6.2: a receipt must still
    // add up years later, even after the host has changed their rates.
    public decimal RoomBeforeDiscount { get; set; }
    public decimal RoomDiscount { get; set; }
    public int DiscountPercent { get; set; }
    public decimal ExtraGuestFee { get; set; }
    public decimal PetFee { get; set; }
    public decimal CleaningFee { get; set; }
    /// <summary>Room after discount plus every surcharge — the base of both service fees.</summary>
    public decimal Subtotal { get; set; }
    /// <summary>The guest service fee (docs/03 §1 step 7).</summary>
    public decimal ServiceFee { get; set; }
    public decimal Tax { get; set; }
    public decimal Promotion { get; set; }
    public decimal Total { get; set; }

    public decimal HostServiceFee { get; set; }
    public decimal HostPayout { get; set; }

    /// <summary>The displayed rows, as JSON, so a receipt renders exactly as quoted.</summary>
    public string PriceLinesJson { get; set; } = "[]";

    public decimal RefundedAmount { get; set; }

    /// <summary>
    /// docs/07 §6 — the currency the guest was reading prices in, and the rate
    /// the screen used, frozen at the moment they booked. The money itself is
    /// always taken in the listing's own currency; this pair exists so a
    /// complaint about "the price I saw" can be settled from the record rather
    /// than from today's rate.
    /// </summary>
    public string? DisplayCurrency { get; set; }
    public decimal? DisplayRate { get; set; }
    /// <summary>Promotional balance granted on cancellation, e.g. when the host walked away.</summary>
    public decimal GoodwillCredit { get; set; }
    public CancellationTier CancellationTier { get; set; } = CancellationTier.Moderate;

    public string? GuestName { get; set; }
    public string? GuestEmail { get; set; }

    /// <summary>
    /// docs/07 §2.5 — how the host reaches somebody who booked without an
    /// account. An account carries its own phone; a stranger has to leave one,
    /// and a host expecting cash at the door needs it more than usually.
    /// </summary>
    public string? GuestPhone { get; set; }

    public string? GuestNote { get; set; }

    /* ------------------------------------------- StayDetails — told to the host */
    /// <summary>Local hour the guest expects to arrive, 0–23; null when not said.</summary>
    public int? EstimatedArrivalHour { get; set; }
    /// <summary>Who is actually staying, when the booker books for somebody else.</summary>
    public string? StayingGuestName { get; set; }
    public bool IsBusinessTrip { get; set; }
    /// <summary>Comma-joined keys of <see cref="StayDetails.Requests"/>.</summary>
    public string? SpecialRequests { get; set; }

    /// <summary>
    /// docs/07 §2.5 — the guest settles with the host on arrival, so Staylio
    /// never holds this money.
    ///
    /// Kept on the booking rather than read from <c>Payment.Method</c>, which is
    /// a separate row that half the call sites do not Include: a cancellation
    /// rule that silently fails to fire because a navigation was not loaded is
    /// the exact shape of bug this codebase keeps paying for. Set in one place,
    /// when the method is chosen.
    /// </summary>
    public bool PaidAtProperty { get; set; }

    /// <summary>
    /// docs/07 §2.5 — when the host confirmed they had the cash in hand. Until
    /// then a pay-at-property booking is a promise, not a payment: nothing is in
    /// the ledger and the platform's fee is not yet billed.
    /// </summary>
    public DateTime? CashCollectedAt { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.PendingHostApproval;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
    public string? CancellationReason { get; set; }
    public CancelledBy? CancelledBy { get; set; }

    // --- docs/01 ĐP-06: paying a deposit now and the rest nearer the stay.
    /// <summary>What the guest actually paid at booking. Equal to Total when they paid in full.</summary>
    public decimal DepositPaid { get; set; }
    /// <summary>What is still owed. Zero unless the guest chose to part-pay.</summary>
    public decimal BalanceDue { get; set; }
    /// <summary>When the rest is taken: 14 days before check-in, or at once for a late booking.</summary>
    public DateOnly? BalanceDueOn { get; set; }
    public BalanceStatus BalanceStatus { get; set; } = BalanceStatus.None;
    public int BalanceAttempts { get; set; }
    /// <summary>Starts the 72-hour retry window of docs/03 §1.</summary>
    public DateTime? BalanceFirstFailedAt { get; set; }
    public DateTime? BalanceLastAttemptAt { get; set; }

    /// <summary>How much of the guest's balance went into this booking.</summary>
    public decimal CreditUsed { get; set; }

    /// <summary>
    /// docs/01 ĐP-09, TC-09 — the promo code applied, and how much it took off.
    /// Both null when no code was used. Kept so the discount can be re-priced at
    /// payment exactly as it was quoted, the same way credit is.
    /// </summary>
    public int? CouponId { get; set; }
    public Coupon? Coupon { get; set; }
    public decimal CouponDiscount { get; set; }

    /// <summary>
    /// docs/01 ĐP-17 — the nightly rate a private offer set for this booking, kept
    /// so the re-price at payment reproduces the total instead of falling back to
    /// the listing's normal rate and failing its own "did the price move" check.
    /// Null for an ordinary booking, whose rate the re-price derives on its own.
    /// </summary>
    public decimal? NightlyOverride { get; set; }

    /// <summary>docs/01 MR-09 — which kind of room, for a hotel booking.</summary>
    public int? RoomTypeId { get; set; }
    public RoomTypeOption? RoomType { get; set; }

    /// <summary>When the 15-minute payment hold lapses (docs/03 §2).</summary>
    public DateTime? HoldExpiresAt { get; set; }
    /// <summary>When an unanswered request to book expires (docs/03 §3).</summary>
    public DateTime? RequestExpiresAt { get; set; }

    public Payment? Payment { get; set; }
    public List<LedgerEntry> LedgerEntries { get; set; } = [];
    /// <summary>Append-only history; see <see cref="BookingLifecycle"/>.</summary>
    public List<BookingEvent> Events { get; set; } = [];
    /// <summary>Guarded so a stay can only be reviewed once.</summary>
    public bool HasReview { get; set; }
}
