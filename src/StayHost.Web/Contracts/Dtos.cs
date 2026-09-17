namespace StayHost.Web.Contracts;

/* ------------------------------------------------------------------ account */

public record RegisterRequest(
    /// <summary>docs/01 TK-01 — either this or <see cref="Phone"/> is enough.</summary>
    string? Email, string Password, string FullName, string? Phone,
    /// <summary>Optional: the code a friend sent. Falls back to matching on email.</summary>
    string? ReferralCode = null,
    /// <summary>docs/01 TK-03 — required, and it has to put them over 18.</summary>
    DateOnly? DateOfBirth = null);

/* ---- docs/01 TK-01 and TK-02 -------------------------------------------- */

public record SendCodeRequest(string? Kind);
public record ConfirmCodeRequest(string? Kind, string? Code);

public record ExternalSignInRequest(
    string? Provider,
    /// <summary>
    /// What the provider handed the browser: an identity token from Google or
    /// Apple, an access token from Facebook. The server takes who somebody is
    /// from this and from nothing else.
    /// </summary>
    string? Credential);

/// <summary>The public ids a browser needs to raise each provider's own sign-in window.</summary>
public record ExternalLoginConfigDto(
    string? GoogleClientId, string? AppleServicesId, string? AppleRedirectUri, string? FacebookAppId);

public record LinkedLoginDto(string Provider, string Label, string? Email, DateTime? LastUsedAt);

public record VerificationStateDto(
    string? Email,
    bool EmailConfirmed,
    string? Phone,
    bool PhoneConfirmed,
    int CodeLength,
    int CodeMinutes,
    IReadOnlyList<LinkedLoginDto> Linked);

public record LoginRequest(string Email, string Password);

/// <summary>docs/01 TK-04 — everything somebody may say about themselves.</summary>
public record UpdateProfileRequest(
    string? FullName,
    string? Phone,
    string? Bio,
    string? DisplayName = null,
    string? AvatarUrl = null,
    IReadOnlyList<string>? Languages = null,
    string? Location = null,
    string? Occupation = null,
    IReadOnlyList<string>? Interests = null,
    // docs/01 TK-13 — emergency contact for trip incidents.
    string? EmergencyContactName = null,
    string? EmergencyContactPhone = null,
    string? EmergencyContactRelation = null);

/// <summary>docs/01 TK-07 — request to verify a company email.</summary>
public record WorkEmailRequest(string? Email);

/// <summary>docs/01 AT-10 — the block list.</summary>
public record BlockedUserDto(int UserId, string Name, string Initials, string? AvatarUrl, DateTime CreatedAt);
public record BlockRequest(int UserId);

/// <summary>docs/01 TM-23 — a saved search, as the account screen lists it.</summary>
/* ----------------------------------------------- docs/02 F1 — lịch sử trả */

/// <summary>
/// One payment on the guest's own history, exactly as stored. Kind names the
/// line of business ("stay" / "experience" / "service" / "gift-card"); only a
/// stay carries a BookingId, because only stays have an invoice endpoint.
/// StatusLabel is server-generated Vietnamese and is translated at the render
/// site through t(), like every other server-composed string.
/// </summary>
public record PaymentHistoryRowDto(
    string Kind,
    string Reference,
    string Title,
    decimal Amount,
    string Method,
    string? CardLast4,
    string StatusLabel,
    DateTime At,
    int? BookingId);

public record SavedSearchDto(int Id, string Label, string Summary, DateTime CreatedAt);

/// <summary>docs/01 TM-23 — save the current search to be alerted about.</summary>
public record SaveSearchRequest(
    string? Label, string? Q, string? Category, decimal? MinPrice, decimal? MaxPrice,
    int Guests, IReadOnlyList<string>? Amenities, string? RoomType, int Bedrooms,
    bool SuperhostOnly, bool InstantBookOnly, IReadOnlyList<string>? HostLanguages);

/* ------------------------------------------- docs/01 CĐ-10, CĐ-11: trip plans */
public record TripPlanSummaryDto(int Id, string Name, bool IsOwner, int BookingCount, int MemberCount, DateTime CreatedAt);
public record TripPlanBookingDto(int BookingId, string Reference, string ListingTitle, string City, DateOnly CheckIn, DateOnly CheckOut);
public record TripItineraryItemDto(int Id, DateOnly Day, string Title, string? Note, string AddedBy, int SortOrder);
public record TripMemberDto(int UserId, string Name, string Initials, string? AvatarUrl, bool IsOwner);
public record TripPlanDetailDto(
    int Id, string Name, bool CanEdit, bool IsOwner,
    IReadOnlyList<TripPlanBookingDto> Bookings,
    IReadOnlyList<TripMemberDto> Members,
    IReadOnlyList<TripItineraryItemDto> Items);
public record CreateTripPlanRequest(string? Name);
public record AddTripBookingRequest(int BookingId);
public record AddTripMemberRequest(int UserId);
public record AddItineraryItemRequest(DateOnly Day, string? Title, string? Note);

/// <summary>docs/01 XH-01 — a friend, or a pending request, as the friends screen sees it.</summary>
public record FriendDto(int UserId, string Name, string Initials, string? AvatarUrl);
public record FriendRequestDto(int Id, int UserId, string Name, string Initials, string? AvatarUrl, DateTime CreatedAt);

/// <summary>docs/01 XH-01/XH-02 — where a friend has been and is going.</summary>
public record FriendJourneyDto(
    string Name, string Visibility,
    IReadOnlyList<JourneyStopDto> Been, IReadOnlyList<JourneyStopDto> Upcoming);
public record JourneyStopDto(int ListingId, string City, double Latitude, double Longitude, int Nights, DateOnly When);
public record JourneyVisibilityRequest(string? Visibility);

/// <summary>docs/01 XH-03 — a peer message between friends, about a place.</summary>
public record FriendMessageDto(int Id, bool Mine, string Body, int? ListingId, string? ListingTitle, DateTime SentAt);
public record SendFriendMessageRequest(int? ListingId, string? Body);

/// <summary>docs/01 ĐG-11 — a review flagged as possible secondary-account fraud.</summary>
public record ReviewFraudDto(
    int ReviewId, int ListingId, string ListingTitle, string HostName, string ReviewerName,
    double Rating, string Risk, IReadOnlyList<string> Reasons, DateTime CreatedAt);

/// <summary>docs/01 AT-03 — the neighbour report form and its admin view.</summary>
public record NeighborConcernDto(string Value, string Label);
public record NeighborReportRequest(string? Location, string? Category, string? Detail, string? Contact);
public record NeighborReportDto(
    int Id, string Location, string Category, string Detail, string? Contact,
    string Status, string? Resolution, DateTime CreatedAt);

/// <summary>docs/01 AT-12 — one host's decline record, for the discrimination monitor.</summary>
public record DeclineMonitorDto(
    int HostId, string HostName, int Responded, int Declined, int DeclineRatePercent,
    int Flagged, IReadOnlyList<FlaggedDeclineDto> FlaggedReasons);

public record FlaggedDeclineDto(string Reference, string Reason, string Category);

/// <summary>docs/01 QT-07 — a help article as the admin editor sees it.</summary>
public record HelpAdminDto(
    int Id, string Slug, string Title, string Category, string Audience,
    string Summary, string Body, int SortOrder, DateTime UpdatedAt);

/// <summary>docs/01 QT-07 — create or update a help article.</summary>
public record HelpArticleSaveRequest(
    int? Id, string? Slug, string? Title, string? Category, string? Audience,
    string? Summary, string? Body, int SortOrder);

/// <summary>docs/01 QT-08 — a feature flag as the admin console sees it.</summary>
public record FeatureFlagDto(
    string Key, string Description, bool Enabled, int RolloutPercent, DateTime UpdatedAt);

/// <summary>docs/01 QT-08 — create or update a feature flag.</summary>
public record FeatureFlagRequest(string Key, string? Description, bool Enabled, int RolloutPercent);

/// <summary>One of the languages the profile editor offers (docs/01 TK-04).</summary>
public record SpokenLanguageDto(string Code, string Label);

/* ---- docs/01 TK-10: notifications, type × channel ------------------------ */

/// <summary>One cell of the matrix: on or off, and whether it may be changed at all.</summary>
public record NotificationCellDto(string Channel, string ChannelLabel, bool On, bool Locked);

public record NotificationRowDto(
    string Topic,
    string Label,
    string Note,
    IReadOnlyList<NotificationCellDto> Cells);

public record NotificationPrefsDto(
    IReadOnlyList<string> Channels,
    IReadOnlyList<string> ChannelLabels,
    IReadOnlyList<NotificationRowDto> Rows);

public record UpdateNotificationPrefRequest(string Topic, string Channel, bool On);

/* ---- docs/01 TK-08: two-factor ------------------------------------------ */

/// <summary>
/// What the browser gets when the password was right but a code is still owed.
/// Carries no session and no account details beyond where the code went.
/// </summary>
public record TwoFactorChallengeDto(
    string Challenge,
    /// <summary>"email" or "phone" — which one the code was sent to.</summary>
    string Kind,
    /// <summary>Masked: "b***@gmail.com", "09****678".</summary>
    string SentTo,
    int CodeLength,
    /// <summary>Development only, like the sign-up code (docs/01 TK-01).</summary>
    string? DevCode);

public record TwoFactorVerifyRequest(string? Challenge, string? Code);

public record TwoFactorSetupRequest(string? Kind, string? Code);

public record TwoFactorStateDto(bool Enabled, string Kind, string? SentTo);

/* ---- docs/01 TK-06: identity verification -------------------------------- */

public record IdentityCheckRequest(
    string? Document,
    string? DocumentNumber,
    string? FrontImageUrl,
    string? BackImageUrl,
    string? SelfieImageUrl);

public record IdentityCheckDto(
    int Id,
    string Document,
    string DocumentLabel,
    string? DocumentLast4,
    string Status,
    string StatusLabel,
    string BadgeClass,
    string? Note,
    DateTime SubmittedAt,
    DateTime? DecidedAt);

/// <summary>The admin queue view — carries the images, which the guest's own view does not need.</summary>
public record IdentityReviewDto(
    int Id,
    int UserId,
    string UserName,
    string? UserEmail,
    string DocumentLabel,
    string? DocumentLast4,
    string FrontImageUrl,
    string? BackImageUrl,
    string SelfieImageUrl,
    string Status,
    DateTime SubmittedAt);

public record DecideIdentityRequest(bool Approve, string? Note);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword);

public record VerifyEmailRequest(string Token);

public record SessionDto(int Id, string Device, DateTime CreatedAt, DateTime ExpiresAt, bool IsCurrent);

public record CurrentUserDto(
    int Id,
    string Email,
    string FullName,
    string Initials,
    string? Phone,
    string? Bio,
    string Role,
    bool IsHost,
    int? HostId,
    int ListingCount,
    int UnreadMessages,
    bool EmailConfirmed,
    string JoinedLabel,
    /// <summary>docs/01 TK-01 — the phone was proved with a six-digit code.</summary>
    bool PhoneConfirmed = false,
    /* ------------------------------------------------------ docs/01 TK-04 */
    string? DisplayName = null,
    string? AvatarUrl = null,
    IReadOnlyList<string>? Languages = null,
    string? Location = null,
    string? Occupation = null,
    IReadOnlyList<string>? Interests = null,
    /* ------------------------------------------------------ docs/01 TK-07 */
    string? WorkEmail = null,
    bool WorkEmailConfirmed = false,
    /* ------------------------------------------------------ docs/01 TK-13 */
    string? EmergencyContactName = null,
    string? EmergencyContactPhone = null,
    string? EmergencyContactRelation = null,
    /* ------------------------------------------------------ docs/01 XH-02 */
    string JourneyVisibility = "Friends",
    /* ------------------------------------------------------ docs/01 TK-09 */
    /// <summary>Null means never chosen — the client keeps its own local choice.</summary>
    string? Language = null,
    string? Currency = null,
    string? TimeZoneId = null);

/// <summary>
/// docs/01 TK-09 — all three sent together (the picker knows all three). Null
/// or empty clears one back to "never chosen"; an INVALID value is refused by
/// name, never silently treated as a clear — a typo that quietly erased a
/// preference would be a failure with no witness.
/// </summary>
public record SavePreferencesRequest(string? Language, string? Currency, string? TimeZoneId);

/* ------------------------------------------------------------------ hosting */

public record HostListingDto(
    int Id,
    string Slug,
    string Title,
    string City,
    string TypeKey,
    string RoomTypeKey,
    int Bedrooms,
    int Beds,
    int Bathrooms,
    int MaxGuests,
    decimal PricePerNight,
    decimal CleaningFee,
    int MinNights,
    bool InstantBook,
    bool IsPublished,
    string CancellationTier,
    double Rating,
    int ReviewCount,
    string Description,
    string? Highlight,
    double Latitude,
    double Longitude,
    IReadOnlyList<string> Images,
    IReadOnlyList<string> AmenityKeys,
    int UpcomingBookings,
    decimal EarningsToDate,
    PricingRulesDto Pricing,
    IReadOnlyList<BedroomDto> BedLayout,
    IReadOnlyList<string> ImageCaptions,
    LegalDeclarationDto Legal,
    int WizardStep,
    bool IsComplete,
    /// <summary>docs/01 CĐ-03 — the arrival guide, as the host last saved it.</summary>
    CheckInSetupDto? CheckIn = null,
    /// <summary>docs/01 ĐP-03 — instant-book conditions, so the editor round-trips them.</summary>
    bool InstantBookRequiresVerified = false,
    bool InstantBookRequiresGoodReviews = false,
    /// <summary>docs/01 ĐP-10 — hard preconditions, so the editor round-trips them.</summary>
    bool RequireGuestPhoto = false,
    bool RequireVerifiedToBook = false,
    /// <summary>docs/07 §2.5 — this host takes the money at the door for this place.</summary>
    bool AcceptsPayAtProperty = false,
    /// <summary>docs/01 AT-01 — review stance, so the host sees "Đang chờ duyệt" / "Bị từ chối".</summary>
    string ReviewStatus = "Approved",
    string? ReviewNote = null);

/// <summary>The host-settable half of docs/03 §1 — discounts and surcharges.</summary>
public record PricingRulesDto(
    int WeeklyDiscountPercent,
    int MonthlyDiscountPercent,
    int EarlyBirdDays,
    int EarlyBirdPercent,
    int LastMinuteDays,
    int LastMinutePercent,
    decimal WeekendSurchargeRate,
    int FreeGuestThreshold,
    decimal ExtraGuestFee,
    bool PetsAllowed,
    int MaxPets,
    decimal PetFee,
    bool PetFeePerNight);

public record SaveListingRequest(
    string Title,
    string City,
    string TypeKey,
    string RoomTypeKey,
    int Bedrooms,
    int Beds,
    int Bathrooms,
    int MaxGuests,
    decimal PricePerNight,
    decimal CleaningFee,
    int MinNights,
    bool InstantBook,
    bool IsPublished,
    string CancellationTier,
    string Description,
    string? Highlight,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<string> Images,
    IReadOnlyList<string> AmenityKeys,
    /// <summary>docs/01 ĐP-03 — instant-book conditions; default off keeps old clients unchanged.</summary>
    bool InstantBookRequiresVerified = false,
    bool InstantBookRequiresGoodReviews = false,
    /// <summary>docs/01 ĐP-10 — hard preconditions to book at all.</summary>
    bool RequireGuestPhoto = false,
    bool RequireVerifiedToBook = false,
    /// <summary>docs/07 §2.5 — this host takes the money at the door for this place.</summary>
    bool AcceptsPayAtProperty = false,
    /// <summary>Omitted by older clients; the listing keeps its current rules then.</summary>
    PricingRulesDto? Pricing = null,
    /// <summary>docs/01 CN-05 — beds per room, as the host laid them out.</summary>
    IReadOnlyList<BedroomDto>? BedLayout = null,
    /// <summary>docs/01 CN-07 — one label per photo, in display order.</summary>
    IReadOnlyList<string>? ImageCaptions = null,
    /// <summary>docs/01 CN-12 — licence, cameras, weapons, animals.</summary>
    LegalDeclarationDto? Legal = null,
    /// <summary>docs/01 CN-01 — where the host got to; 0 means finished.</summary>
    int WizardStep = 0,
    bool IsComplete = true,
    /// <summary>docs/01 CĐ-03 — omitted by older clients; the guide is then left alone.</summary>
    CheckInSetupDto? CheckIn = null);

/// <summary>
/// docs/01 CĐ-03 and CĐ-04 — the arrival guide as the host fills it in. Times
/// travel as "14:00" rather than as a duration, because that is what an
/// &lt;input type="time"&gt; hands back.
/// </summary>
public record CheckInSetupDto(
    string CheckInFrom,
    string CheckInTo,
    string CheckOutBefore,
    string Method,
    string? AddressLine,
    string? Directions,
    string? WifiName,
    string? WifiPassword,
    string? ApplianceNotes,
    /// <summary>docs/01 CĐ-04 — stored here, released only inside the 48-hour window.</summary>
    string? DoorCode,
    string? HostPhone);

/// <summary>
/// docs/01 CĐ-03 — the guide as one guest reads it, already filtered by
/// docs/03 §10. A field that guest may not see is not blanked here, it is
/// absent: the server never sends a door code it has decided to withhold.
/// </summary>
public record CheckInGuideDto(
    string WindowLabel,
    string MethodLabel,
    string? AddressLine,
    string? Directions,
    string? WifiName,
    string? WifiPassword,
    IReadOnlyList<string> ApplianceNotes,
    string? HostPhone,
    /// <summary>docs/01 CĐ-04 — null until 48 hours before check-in.</summary>
    string? DoorCode,
    /// <summary>True when there is a code to wait for, so the page can say so.</summary>
    bool DoorCodeExpected,
    /// <summary>When the code will appear, while it is still being withheld.</summary>
    string? DoorCodeNote);

/// <summary>docs/01 CN-12 — the declarations a host must make before publishing.</summary>
public record LegalDeclarationDto(
    string? LicenseNumber,
    bool HasSecurityCameras,
    string? SecurityCameraNote,
    bool HasWeaponsOnProperty,
    bool HasDangerousAnimals);

public record CalendarBlockDto(int Id, DateOnly From, DateOnly To, string? Note);

public record PriceRuleDto(int Id, string Name, DateOnly From, DateOnly To, decimal NightlyRate);

public record CreatePriceRuleRequest(int ListingId, string? Name, DateOnly From, DateOnly To, decimal NightlyRate);

/// <param name="Cleanliness">docs/03 §7 — sạch sẽ, 1–5.</param>
/// <param name="Communication">docs/03 §7 — giao tiếp, 1–5.</param>
/// <param name="HouseRules">docs/03 §7 — tuân thủ nội quy, 1–5.</param>
public record ReviewGuestRequest(
    double Rating, string Text, bool WouldHostAgain,
    int? Cleanliness = null, int? Communication = null, int? HouseRules = null);

/// <summary>docs/03 §7 — what hosts said about someone as a guest, heading by heading.</summary>
public record GuestScoresDto(
    int Reviews, double? Cleanliness, double? Communication, double? HouseRules, int WouldHostAgainPercent);

public record CreateBlockRequest(int ListingId, DateOnly From, DateOnly To, string? Note);

/// <summary>docs/01 TK-12 — whether this account is paused, and whether it may be.</summary>
public record AccountPauseDto(
    bool IsPaused,
    DateTime? PausedAt,
    int HiddenListings,
    int LiveBookings,
    bool CanPause,
    string? Blocker,
    string Notice);

/* ---- docs/02 H1: everything one person's reviews add up to ---------------- */

/// <summary>One stay still waiting to be reviewed, and how long is left.</summary>
public record ReviewTodoDto(
    int BookingId,
    string Reference,
    string ListingTitle,
    string? ListingImage,
    DateOnly CheckOut,
    DateTime Deadline,
    int DaysLeft,
    /// <summary>"guest" writes about the stay, "host" writes about the guest.</summary>
    string Side,
    string? CounterpartName);

/// <summary>One review this person wrote, or one written about them.</summary>
public record MyReviewDto(
    int Id,
    int? BookingId,
    string Text,
    double Rating,
    string When,
    DateTime CreatedAt,
    string? ListingTitle,
    /// <summary>Who wrote it, for the ones about this person.</summary>
    string? AuthorName,
    /// <summary>docs/03 §7 — false while the other side has not written theirs.</summary>
    bool IsPublic,
    /// <summary>docs/01 ĐG-08 — still inside the 48 hours, and still unpublished.</summary>
    bool CanEdit,
    /// <summary>docs/01 TĐ-12 — the host's public answer, when there is one.</summary>
    string? HostReply,
    /// <summary>docs/01 ĐG-06 — set only on a host's review of a guest.</summary>
    bool? WouldHostAgain);

/// <summary>docs/02 H1 — the three groups the reviews screen is made of.</summary>
public record MyReviewsDto(
    IReadOnlyList<ReviewTodoDto> ToWrite,
    IReadOnlyList<MyReviewDto> Written,
    IReadOnlyList<MyReviewDto> AboutMe);

/// <summary>
/// docs/01 ĐG-08 — a guest's own review, read back so they can correct it
/// inside the 48 hours. <c>IsPublic</c> and <c>EditableUntil</c> are both said
/// out loud rather than left to the caller to work out: once the other side has
/// written theirs the review stands, whatever the clock says.
/// </summary>
public record GuestReviewDto(
    int Id, string Text, double Rating,
    double Cleanliness, double Accuracy, double CheckIn,
    double Communication, double Location, double Value,
    string? PrivateNote, DateTime? EditableUntil, bool IsPublic, bool CanEdit);

/// <summary>
/// docs/01 ĐG-07 — one review as the host sees it, with the two facts that
/// decide whether the reply box is offered: has it been answered already, and
/// is it still inside the thirty days of docs/03 §7.
/// </summary>
public record HostReviewDto(
    int Id, int ListingId, string ListingTitle, string AuthorName,
    double Rating, string Text, DateTime CreatedAt,
    string? HostReply, DateTime? HostRepliedAt,
    bool CanReply, DateTime ReplyDeadline);

public record HostBookingDto(
    int Id,
    string Reference,
    int ListingId,
    string ListingTitle,
    string GuestName,
    string? GuestEmail,
    string? GuestNote,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    int Guests,
    decimal Total,
    decimal HostPayout,
    string Status,
    string StatusLabel,
    string StatusBadge,
    string PaymentStatus,
    /// <summary>Set while a request is still inside its 24-hour window.</summary>
    DateTime? RequestExpiresAt,
    DateTime CreatedAt,
    /// <summary>docs/01 CĐ-06 — a change the guest asked for, awaiting the host.</summary>
    PendingChangeDto? PendingChange = null,
    /// <summary>docs/07 §2.5 — the guest is paying this one at the door.</summary>
    bool PaidAtProperty = false,
    /// <summary>docs/07 §2.5 — set once the host confirmed the cash is in hand.</summary>
    DateTime? CashCollectedAt = null,
    /// <summary>docs/07 §2.5 — a phone for a guest who booked without an account.</summary>
    string? GuestPhone = null)
{
    public StayDetailsDto? Details { get; init; }
}

public record PendingChangeDto(
    int Id, DateOnly NewCheckIn, DateOnly NewCheckOut, int NewGuests,
    decimal Difference, string DifferenceLabel);

public record HostDashboardDto(
    int ListingCount,
    int PublishedCount,
    int UpcomingBookings,
    decimal EarningsToDate,
    decimal EarningsUpcoming,
    double AverageRating,
    int TotalReviews,
    int UnreadMessages,
    IReadOnlyList<HostListingDto> Listings,
    IReadOnlyList<HostBookingDto> Bookings,
    IReadOnlyList<MonthlyEarningDto> EarningsByMonth);

public record MonthlyEarningDto(string Month, decimal Amount, int Nights);

/* --------------------------------------------------------- host operations */

/// <summary>docs/01 QL-01 — the day's work, grouped the way a host thinks about it.</summary>
public record TodayBoardDto(
    IReadOnlyList<TodayItemDto> Arriving,
    IReadOnlyList<TodayItemDto> InHouse,
    IReadOnlyList<TodayItemDto> Leaving,
    IReadOnlyList<TodayItemDto> AwaitingAnswer,
    int NeedsAnswer);

public record TodayItemDto(
    int BookingId, string Reference, string ListingTitle, string GuestName,
    DateOnly CheckIn, DateOnly CheckOut, int Nights, int Guests,
    string What, string StatusLabel, string StatusBadge);

/// <summary>docs/01 QL-04 — every listing's month, side by side.</summary>
public record MultiCalendarDto(DateOnly From, int Days, IReadOnlyList<MultiCalendarRowDto> Rows);

public record MultiCalendarRowDto(
    int ListingId, string Title, bool IsPublished, IReadOnlyList<MultiCalendarCellDto> Days);

public record MultiCalendarCellDto(
    DateOnly Date, decimal Rate, string RateSource,
    /// <summary>open / booked / blocked.</summary>
    string State,
    string? BookingReference);

/// <summary>docs/01 QL-06 and QL-07 — the rules that decide who can book which nights.</summary>
public record CalendarRulesDto(
    int MinNights,
    int MaxNights,
    int AdvanceNoticeHours,
    int? SameDayCutoffHour,
    int CalendarVisibilityMonths,
    int TurnoverDays,
    /// <summary>Bitmask over DayOfWeek; Sunday is bit 0.</summary>
    int BlockedCheckInDays,
    int BlockedCheckOutDays,
    string TimeZoneId);

/// <summary>docs/01 QL-05 — one edit applied to a run of days.</summary>
public record BulkDayEditRequest(
    DateOnly From,
    DateOnly To,
    decimal? NightlyRate,
    int? MinNights,
    /// <summary>True blocks the range, false clears blocks, null leaves them alone.</summary>
    bool? Blocked,
    string? Label);

/// <summary>docs/01 QL-20 — where the money goes, and when.</summary>
public record PayoutSettingsDto(
    string? BankName,
    string? AccountName,
    string? AccountLast4,
    string Schedule,
    IReadOnlyList<PayoutRowDto> Upcoming,
    /// <summary>docs/07 §12.2 — false until a test transfer has proved the account.</summary>
    bool Verified = false,
    /// <summary>Set while the three-day freeze after an account change is running.</summary>
    DateTime? FrozenUntil = null,
    /// <summary>docs/07 §17.4 — what still comes off the next transfer.</summary>
    decimal OwedToPlatform = 0m,
    /// <summary>docs/07 §12.3 — transfers already made, newest first.</summary>
    IReadOnlyList<PayoutRowDto>? History = null);

/// <summary>
/// One booking's share of a transfer. docs/07 §12.3 — the money may leave as a
/// single bank line, but the report stays per booking.
/// </summary>
public record PayoutRowDto(
    string Reference,
    string ListingTitle,
    DateOnly DueOn,
    decimal Amount,
    string Status,
    string? HoldReason = null,
    string? TransferReference = null,
    DateTime? PaidAt = null,
    int Attempts = 0,
    /// <summary>docs/07 §17.4 — kept back against the host's debt, so it never reached the bank.</summary>
    decimal Deducted = 0m);

public record SavePayoutRequest(string? BankName, string? AccountName, string? AccountNumber, string? Schedule);

/// <summary>docs/03 §8 — the four criteria and where the host stands on each.</summary>
public record SuperhostProgressDto(
    bool IsSuperhost,
    bool WouldQualify,
    DateOnly NextReview,
    IReadOnlyList<SuperhostCriterionDto> Criteria);

public record SuperhostCriterionDto(string Key, string Label, string Current, string Target, bool Met);

/* ------------------------------------------------------------ notifications */

public record NotificationDto(
    int Id, string Kind, string Title, string Body, string? Link, bool Unread, DateTime CreatedAt);

public record NotificationFeedDto(int Unread, IReadOnlyList<NotificationDto> Items);

/* ------------------------------------------------------------ TC-09 coupons */

public record CouponDto(
    int Id, string Code, string Campaign, string Kind, decimal Value,
    decimal? MaxDiscount, decimal? MinBookingTotal,
    DateTime? StartsAt, DateTime? EndsAt,
    int? MaxRedemptions, int? MaxPerUser, int TimesUsed, bool IsActive, DateTime CreatedAt);

public record SaveCouponRequest(
    string Code, string? Campaign, string Kind, decimal Value,
    decimal? MaxDiscount = null, decimal? MinBookingTotal = null,
    DateTime? StartsAt = null, DateTime? EndsAt = null,
    int? MaxRedemptions = null, int? MaxPerUser = null);

/* ------------------------------------------------------------------ reports */

/// <summary>docs/01 AT-02 — Target is Listing, User, Message or Review.</summary>
public record CreateReportRequest(string Target, int SubjectId, string Reason, string? Detail);

public record ReportReasonsDto(string Target, string TargetLabel, IReadOnlyList<string> Reasons);

public record ReportDto(
    int Id, string Target, string TargetLabel, int? SubjectId, string SubjectTitle,
    string Reason, string? Detail,
    string Status, string? Resolution, string ReporterName, DateTime CreatedAt);

public record ResolveReportRequest(string Status, string? Resolution);

/* -------------------------------------------------------- resolution centre */

public record OpenResolutionRequest(
    int BookingId, string Kind, decimal AmountClaimed, string Description, IReadOnlyList<string>? EvidenceUrls);

public record RespondResolutionRequest(bool Accept, string? Note);

public record DecideResolutionRequest(decimal AmountAwarded, string Decision);

/// <summary>docs/01 AT-04 — one claim, everything both sides and an admin need to see.</summary>
public record ResolutionCaseDto(
    int Id,
    string Reference,
    int BookingId,
    string BookingReference,
    string ListingTitle,
    string Kind,
    string KindLabel,
    string Status,
    string StatusLabel,
    string StatusBadge,
    decimal AmountClaimed,
    decimal AmountAwarded,
    string Description,
    IReadOnlyList<string> EvidenceUrls,
    string OpenedByName,
    bool OpenedByHost,
    DateTime ResponseDueAt,
    string? Response,
    DateTime? RespondedAt,
    string? Decision,
    string? DecidedByName,
    DateTime? DecidedAt,
    /// <summary>The viewer is the one who owes an answer right now.</summary>
    bool NeedsMyResponse,
    bool CanWithdraw,
    IReadOnlyList<ResolutionEventDto> History,
    DateTime CreatedAt);

public record ResolutionEventDto(string FromLabel, string ToLabel, string Actor, string Note, DateTime At);

/* -------------------------------------------------------------------- admin */

/// <summary>
/// The live visitor count for the admin dashboard.
///
/// <paramref name="Since"/> and <paramref name="WindowMinutes"/> travel with the
/// numbers on purpose: "12 người" means nothing without "trong 5 phút qua", and
/// a peak means nothing without knowing it is measured from the last restart.
/// A figure whose definition is left in the code gets read as whatever the
/// person looking at it assumes.
/// </summary>
public record PresenceDto(
    int Total, int SignedIn, int Guests,
    int Peak, DateTime Since, int WindowMinutes, DateTime At);

public record AdminOverviewDto(
    int Users, int Hosts, int Listings, int PublishedListings, int Drafts,
    int Bookings, int ActiveBookings, decimal GrossVolume, decimal PlatformRevenue,
    int OpenReports, int QueuedEmails,
    IReadOnlyList<AdminListingDto> RecentListings,
    IReadOnlyList<ReportDto> Reports,
    LedgerReportDto Ledger,
    /// <summary>docs/01 QT-09 — who did what, newest first.</summary>
    IReadOnlyList<StayHost.Web.Services.AdminAudit.AdminAuditRow> AuditLog,
    /// <summary>docs/01 QT-06 — fee rates and the regional tax rules.</summary>
    PlatformSettingsDto Settings);

public record PlatformSettingsDto(
    decimal GuestServiceFeeRate,
    decimal HostServiceFeeRate,
    int MaxDiscountPercent,
    decimal DefaultCleaningFee,
    IReadOnlyList<TaxRuleDto> TaxRules,
    /// <summary>docs/01 QT-06/TC-12 — the display rates, editable next to the taxes.</summary>
    IReadOnlyList<ExchangeRateDto> ExchangeRates);

/// <summary>
/// One display rate on the config screen. Stale is computed server-side from
/// Fx.RefreshInterval (docs/07 §6's six hours) so the screen and the rule
/// cannot disagree about what "old" means.
/// </summary>
public record ExchangeRateDto(
    int Id, string Code, string Label, string Symbol,
    decimal RateFromVnd, int SortOrder, bool IsActive,
    string Source, DateTime UpdatedAt, bool Stale,
    decimal? FeedRate, DateTime? FeedFetchedAt);

public record SaveExchangeRateRequest(decimal RateFromVnd, bool IsActive);

public record TaxRuleDto(
    int Id, string Country, string? City, string Name,
    string Method, string Base, decimal Value, int SortOrder, bool IsActive);

/// <summary>
/// The daily reconciliation docs/03 §5 asks for. <c>Imbalance</c> must be zero;
/// anything else means a transaction was written without its other half.
/// </summary>
public record LedgerReportDto(
    decimal Imbalance,
    int Entries,
    int Transactions,
    IReadOnlyList<LedgerAccountDto> Accounts);

public record LedgerAccountDto(string Account, string Label, decimal Debits, decimal Credits, decimal Balance);

public record AdminListingDto(
    int Id, string Slug, string Title, string City, string HostName,
    bool IsPublished, double Rating, int ReviewCount, decimal PricePerNight, DateTime CreatedAt);

/// <summary>docs/01 AT-01 — one listing waiting in the pre-publish review queue.</summary>
public record PendingListingDto(
    int Id, string Slug, string Title, string City, string HostName, int HostUserId,
    decimal PricePerNight, string? CoverImage, DateTime? SubmittedForReviewAt);

/// <summary>docs/01 AT-01 — an admin's decision on a listing in review.</summary>
public record ModerationDecisionRequest(string? Reason);

/* ---------------------------------------------------------------- wishlists */

public record WishlistDto(
    int Id,
    string Name,
    bool IsDefault,
    int Count,
    IReadOnlyList<string> CoverImages,
    /// <summary>docs/01 YT-05 — set when the list has a live share link.</summary>
    string? ShareToken = null);

public record WishlistDetailDto(WishlistDto List, IReadOnlyList<WishlistEntryDto> Items);

/// <summary>docs/01 YT-03 — a saved place plus the guest's private note on it.</summary>
public record WishlistEntryDto(
    ListingCardDto Card, string? Note,
    // docs/01 YT-06 — group votes; null outside a shared list.
    int Up = 0, int Down = 0, bool? MyVote = null);

/// <summary>docs/01 YT-06 — a group vote on a place in a shared wishlist.</summary>
public record WishlistVoteRequest(int ListingId, bool Up);

public record SaveWishlistRequest(string Name);

public record SaveWishlistNoteRequest(string? Note);

/* ----------------------------------------------------------------- messages */

public record ThreadSummaryDto(
    int Id,
    int ListingId,
    string ListingSlug,
    string ListingTitle,
    string ListingImage,
    string CounterpartName,
    string CounterpartInitials,
    bool ViewerIsHost,
    string? LastMessage,
    DateTime LastMessageAt,
    int UnreadCount,
    /// <summary>docs/01 TN-05 — the other side spoke last; awaiting the viewer.</summary>
    bool NeedsReply = false,
    bool IsArchived = false);

public record MessageDto(
    int Id, int SenderUserId, string SenderName, string Body, DateTime SentAt, bool Mine,
    /// <summary>Written by the platform, not a person (docs/01 TN-04).</summary>
    bool IsSystem,
    /// <summary>True when contact details in this message are currently masked (TN-07).</summary>
    bool ContactsMasked,
    /// <summary>docs/01 TN-02 — photos sent with the message.</summary>
    IReadOnlyList<string> Attachments);

public record ThreadDetailDto(
    ThreadSummaryDto Summary,
    IReadOnlyList<MessageDto> Messages,
    /// <summary>False until the guest has a confirmed booking at this listing.</summary>
    bool ContactsUnlocked,
    /// <summary>docs/01 TN-03 — the order this conversation is about, if there is one.</summary>
    ThreadBookingDto? Booking,
    /// <summary>docs/01 TN-08 — the host's saved phrases; empty for a guest.</summary>
    IReadOnlyList<QuickReplyDto> QuickReplies,
    /// <summary>docs/01 ĐP-17 — private offers on this thread, newest first.</summary>
    IReadOnlyList<SpecialOfferDto> Offers);

/* ---------------------------------------------------------- ĐP-17 offers */

public record SpecialOfferDto(
    int Id, DateOnly CheckIn, DateOnly CheckOut, int Guests, int Nights,
    decimal NightlyRate, decimal StayTotal, string Status, string StatusLabel,
    bool IsLive, DateTime ExpiresAt, int? BookingId);

public record SendOfferRequest(DateOnly CheckIn, DateOnly CheckOut, int Guests, decimal NightlyRate);

/// <summary>docs/01 TN-03 — a compact order card, with the actions worth taking from here.</summary>
public record ThreadBookingDto(
    int Id, string Reference, DateOnly CheckIn, DateOnly CheckOut, int Nights, int Guests,
    decimal Total, string StatusLabel, string StatusBadge, bool NeedsHostAnswer);

public record QuickReplyDto(int Id, string Title, string Body, int SortOrder);

public record SaveQuickReplyRequest(string Title, string Body, int SortOrder);

public record SendMessageRequest(int? ThreadId, int? ListingId, string Body, IReadOnlyList<string>? Attachments = null);

public record ReplyToReviewRequest(string Text);

/* ------------------------------------------------------------- guest review */

public record SubmitReviewRequest(
    int BookingId,
    double Rating,
    string Text,
    double Cleanliness,
    double Accuracy,
    double CheckIn,
    double Communication,
    double Location,
    double Value,
    /// <summary>docs/01 ĐG-05 — feedback for the host alone, never published.</summary>
    string? PrivateNote = null,
    /// <summary>
    /// docs/01 TĐ-11 — the interface language the writer was using. Sent by the
    /// client because nothing on the server knows it: the choice lives in the
    /// browser, not on the account.
    /// </summary>
    string? Language = null);

public record CategoryDto(string Key, string Label, string Icon, int Count);

public record AmenityDto(string Key, string Label, string Icon, string Group);

public record MetaDto(
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<AmenityDto> Amenities,
    IReadOnlyList<AmenityDto> QuickAmenities,
    IReadOnlyList<string> Cities,
    IReadOnlyList<RoomTypeDto> RoomTypes,
    decimal MinPrice,
    decimal MaxPrice,
    IReadOnlyList<int> PriceHistogram,
    IReadOnlyList<CurrencyDto> Currencies,
    IReadOnlyList<LanguageDto> Languages,
    FeesDto Fees,
    /// <summary>
    /// docs/01 TM-26 — the city landing pages that have something to show, with
    /// the slug the server itself would route on. The footer links every one of
    /// them, which is how a crawler reaches a city page at all: a sitemap lists
    /// addresses, but a search engine follows links, and until now only six
    /// cities were hard-coded into the footer while eighteen existed.
    /// </summary>
    IReadOnlyList<CityLinkDto>? CityLinks = null);

/// <summary>One city landing page: what to call it, where it lives, how big it is.</summary>
public record CityLinkDto(string Name, string Slug, int Count);

/// <summary>
/// The fee constants the client needs to explain a price. The authoritative
/// numbers still live in <c>Pricing</c>; these are published so the UI never
/// hard-codes a rate of its own (docs/00 §6.8).
/// </summary>
public record FeesDto(
    decimal GuestServiceFeeRate,
    decimal HostServiceFeeRate,
    int MaxDiscountPercent,
    decimal DefaultCleaningFee);

public record RoomTypeDto(string Key, string Label, string Hint);

public record CurrencyDto(string Code, string Label, string Symbol, decimal RateFromVnd);

public record LanguageDto(string Code, string Label, string Region);

public record ListingCardDto(
    int Id,
    string Slug,
    string Title,
    string City,
    string Country,
    string TypeKey,
    string TypeLabel,
    string RoomTypeLabel,
    int Bedrooms,
    int Beds,
    int Bathrooms,
    int MaxGuests,
    decimal PricePerNight,
    /// <summary>Pre-discount nightly rate, or null when the listing is not on offer.</summary>
    decimal? OriginalPricePerNight,
    int DiscountPercent,
    bool InstantBook,
    double Rating,
    int ReviewCount,
    bool IsSuperhost,
    bool IsGuestFavorite,
    double Latitude,
    double Longitude,
    string? Highlight,
    IReadOnlyList<string> Images,
    IReadOnlyList<string> AmenityKeys,
    bool IsFavorite,
    /// <summary>Cleaning fee, so a card can show the same all-in figure as checkout.</summary>
    decimal CleaningFee,
    /// <summary>
    /// All-in total for the searched dates, priced by the same engine as the
    /// room page and checkout. Null when the search carried no dates.
    /// </summary>
    decimal? StayTotal,
    /// <summary>
    /// The stay this card was matched on. Only set when a flexible search
    /// (docs/01 TM-06, TM-07) landed it on dates other than the ones shown at
    /// the top of the page.
    /// </summary>
    DateOnly? StayCheckIn = null,
    DateOnly? StayCheckOut = null,
    /// <summary>
    /// docs/03 §4 — whether this place may be advertised as free cancellation.
    /// The card used to print "Huỷ miễn phí" on every result, including the
    /// non-refundable ones, so the flag has to travel with the card.
    /// </summary>
    bool FreeCancellation = false,
    /// <summary>The one-line promise from <c>Cancellation.Headline</c>.</summary>
    string CancellationHeadline = "")
{
    /// <summary>"Cách trung tâm 1,2 km" — null when the city's centre is not known.</summary>
    public double? FromCentreKm { get; init; }
    public string? FromCentreLabel { get; init; }
    /// <summary>The guest may pay the host on arrival (docs/07 §2.5) — "không cần trả trước".</summary>
    public bool PayAtProperty { get; init; }
}

public record HomeSectionDto(
    string Key,
    string Title,
    string? Subtitle,
    string Href,
    IReadOnlyList<ListingCardDto> Items);

public record InspirationGroupDto(string Tab, IReadOnlyList<InspirationLinkDto> Links);

public record InspirationLinkDto(string Title, string Subtitle, string Href);

public record HomeDto(
    IReadOnlyList<HomeSectionDto> Sections,
    IReadOnlyList<InspirationGroupDto> Inspiration);

public record SearchResultDto(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<ListingCardDto> Items,
    /// <summary>Only present when nothing matched (docs/01 TM-22).</summary>
    NoResultsDto? NoResults = null,
    /// <summary>Only present when the guest searched flexible dates.</summary>
    FlexibleDatesDto? Dates = null);

/// <summary>
/// docs/01 TM-06 and TM-07 — what a loose "a week sometime in October" was
/// turned into, so the page can say which stay it is pricing.
/// </summary>
public record FlexibleDatesDto(
    string Length,
    string Label,
    int Nights,
    DateOnly CheckIn,
    DateOnly CheckOut,
    /// <summary>How many alternative stays were considered.</summary>
    int Options);

/// <summary>
/// docs/01 TM-22 — when a search comes back empty, say which filter is doing
/// the blocking and where there is something nearby, rather than a dead end.
/// </summary>
public record NoResultsDto(
    IReadOnlyList<BlockingFilterDto> BlockingFilters,
    IReadOnlyList<NearbyAreaDto> NearbyAreas);

/// <summary>A filter that, dropped on its own, would bring back <c>Count</c> stays.</summary>
public record BlockingFilterDto(string Key, string Label, int Count);

public record NearbyAreaDto(string City, int Count, decimal FromPrice);

/// <summary>One night on the room calendar: its rate and whether it is for sale.</summary>
public record CalendarNightDto(
    DateOnly Date,
    decimal Rate,
    /// <summary>day / season / weekend / base — where the rate came from.</summary>
    string RateSource,
    bool Available,
    int MinNights);

/// <summary>docs/01 TĐ-09 — a run of free nights offered when the chosen dates are taken.</summary>
public record OpeningDto(DateOnly From, DateOnly To, int Nights);

public record ListingCalendarDto(
    int ListingId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CalendarNightDto> Nights,
    IReadOnlyList<OpeningDto> NextOpenings);

public record HostDto(
    int Id,
    string Name,
    string Initials,
    bool IsSuperhost,
    int YearsHosting,
    string? Bio,
    string ResponseRate,
    string ResponseTime,
    string JoinedLabel,
    int ListingCount,
    double AverageRating,
    int TotalReviews,
    /// <summary>Null for seeded demo hosts; set when the host has a real account to message.</summary>
    int? UserId,
    /// <summary>docs/01 TK-04 — the photo they uploaded, if they uploaded one.</summary>
    string? AvatarUrl = null,
    /// <summary>docs/01 TĐ-14 — the languages they speak, already labelled.</summary>
    IReadOnlyList<string>? Languages = null,
    /// <summary>docs/01 TĐ-14 and QL-19 — who else answers for this place.</summary>
    IReadOnlyList<string>? CoHosts = null);

/// <summary>docs/01 TĐ-13 — "khoảng cách tới các điểm chính".</summary>
public record LandmarkDto(string Name, string Distance);

public record ReviewDto(
    int Id,
    string AuthorName,
    string AuthorInitials,
    string? AuthorLocation,
    string When,
    string Text,
    double Rating,
    /// <summary>docs/01 TĐ-12 — the host's single public answer.</summary>
    string? HostReply,
    DateTime? HostRepliedAt,
    /// <summary>docs/01 TK-05 — set when the author has an account to open. Null for seeded reviews.</summary>
    int? AuthorUserId = null,
    /// <summary>docs/01 TĐ-11 — what the reader filters on. Never null: guessed when unstored.</summary>
    string Language = "en");

/// <summary>docs/01 TĐ-21 — one subject the reviews keep raising, and its own score.</summary>
public record ReviewThemeDto(string Key, string Label, int Mentions, double Rating);

public record RatingBreakdownDto(
    double Cleanliness,
    double Accuracy,
    double CheckIn,
    double Communication,
    double Location,
    double Value,
    /// <summary>docs/01 TĐ-10 — how many reviews gave 5, 4, 3, 2 and 1 star.</summary>
    IReadOnlyList<int> StarCounts);

public record AmenityGroupDto(string Group, IReadOnlyList<AmenityDto> Items);

/// <summary>
/// docs/01 TĐ-04 — the amenity list shows everything, with the ones this place
/// does not have struck through, so a guest can see what is missing.
/// </summary>
public record AmenityAvailabilityDto(string Key, string Label, string Icon, string Group, bool Available);

/// <summary>docs/01 TĐ-05 — which beds are in which room.</summary>
public record BedroomDto(string Name, IReadOnlyList<string> Beds);

public record ListingDetailDto(
    ListingCardDto Card,
    string Description,
    string CancellationPolicy,
    IReadOnlyList<string> HouseRules,
    IReadOnlyList<string> SafetyInfo,
    IReadOnlyList<AmenityGroupDto> AmenityGroups,
    /// <summary>Every filterable amenity, marked present or missing (TĐ-04).</summary>
    IReadOnlyList<AmenityAvailabilityDto> AllAmenities,
    IReadOnlyList<BedroomDto> Bedrooms,
    IReadOnlyList<ReviewDto> Reviews,
    RatingBreakdownDto RatingBreakdown,
    HostDto Host,
    IReadOnlyList<ListingCardDto> Similar,
    /// <summary>Nights already taken by a live booking; the picker greys these out.</summary>
    IReadOnlyList<DateOnly> UnavailableDates,
    /// <summary>
    /// docs/01 MR-08 — empty for an ordinary place. When it has rows the guest
    /// must pick one before checkout (MR-09).
    /// </summary>
    IReadOnlyList<HotelRoomDto>? RoomTypes = null,
    /// <summary>
    /// docs/01 CĐ-03 — the arrival hours, which are public: a guest decides
    /// whether a place works for a late flight before booking, not after. The
    /// rest of the guide stays behind the confirmation gate of docs/03 §10.
    /// </summary>
    string CheckInWindow = "",
    /// <summary>docs/01 TĐ-13 — nearest landmarks, closest first.</summary>
    IReadOnlyList<LandmarkDto>? Landmarks = null,
    /// <summary>docs/01 ĐG-12 — public notes about host cancellations in the last year.</summary>
    IReadOnlyList<string>? CancellationNotes = null,
    /// <summary>docs/01 TĐ-22 — the host's local guidebook, grouped and in display order.</summary>
    IReadOnlyList<GuidebookGroupDto>? Guidebook = null,
    /// <summary>
    /// docs/01 TĐ-23 — set only when <see cref="StayHost.Domain.Scarcity"/> says the
    /// near calendar is full enough to say so. Null is the ordinary case.
    /// </summary>
    RareFindDto? RareFind = null,
    /// <summary>
    /// docs/01 TĐ-21 — what the reviews keep saying, strongest first. Empty
    /// until there are enough of them to be talking about anything.
    /// </summary>
    IReadOnlyList<ReviewThemeDto>? ReviewThemes = null);

/// <summary>docs/01 TĐ-22 — one heading of a guidebook and what is under it.</summary>
public record GuidebookGroupDto(string Category, string Label, IReadOnlyList<GuidebookPlaceDto> Places);

public record GuidebookPlaceDto(
    int Id,
    string Category,
    string Name,
    string? Note,
    string? Address,
    double? Latitude,
    double? Longitude,
    /// <summary>"1,2 km" from the listing, or null when the entry carries no pin.</summary>
    string? Distance,
    int SortOrder);

/// <summary>docs/01 TĐ-23 — the badge and the sentence that justifies it.</summary>
public record RareFindDto(string Label, string Reason, int FreeNights, int WindowNights);

/// <summary>
/// docs/03 §4 — the event an admin is recognising. Free text and required: a
/// full refund plus a payout from the fund should never rest on a dropdown
/// nobody had to think about.
/// </summary>
public record ForceMajeureRequest(string? Reason);

/// <summary>
/// docs/09 §3.6 (DV-D) — what the provider says about a site that was not what
/// the guest declared. The note is optional: arriving to no kitchen is the whole
/// report, and demanding an essay would only delay it.
/// </summary>
public record MisdeclaredConditionsRequest(string? Note);

/// <summary>
/// docs/09 §3.5 — one job as the person doing it needs to see it.
///
/// This did not exist. A provider could post a service, be booked, be paid and
/// never once be shown who had booked them, where to go, or the allergy note
/// docs/09 §3.5 makes the guest fill in *for them*. The guest-facing
/// <see cref="ServiceBookingDto"/> is no use here: it carries neither the guest
/// nor what the provider actually earns.
/// </summary>
public record ProviderJobDto(
    int Id,
    string Reference,
    string OfferingTitle,
    string GuestName,
    DateTime StartsAt,
    DateTime EndsAt,
    int Quantity,
    string Unit,
    string Address,
    /// <summary>The mandatory note for the categories of docs/09 §3.5, when there is one.</summary>
    string? Note,
    /// <summary>Only after the job is confirmed, per docs/03 §10 — same gate as a stay.</summary>
    string? GuestPhone,
    decimal Total,
    decimal ProviderPayout,
    string Status,
    string StatusLabel,
    string StatusBadge,
    string? CancelReason,
    /// <summary>Whether docs/09 §3.6 (DV-D) may still be reported on this job.</summary>
    bool CanReportMisdeclared)
{
    /// <summary>docs/09 §3.5 — waiting on this provider's yes, until <see cref="RespondBy"/>.</summary>
    public bool CanRespond { get; init; }
    public DateTime? RespondBy { get; init; }

    /// <summary>docs/09 §3.6 — the provider may still pull out of this job.</summary>
    public bool CanCancel { get; init; }
}

public record ProviderJobDecisionRequest(string? Reason);

/// <summary>
/// docs/01 TĐ-22 — what a host posts when writing one guidebook entry.
///
/// <c>Category</c> travels as its enum name, the way it comes back in
/// <see cref="GuidebookPlaceDto"/>; the controller parses it, so an unknown
/// value is a bad request rather than a silent zero.
/// </summary>
public record GuidebookPlaceRequest(
    string Category,
    string Name,
    string? Note,
    string? Address,
    double? Latitude,
    double? Longitude);

public record HotelRoomDto(
    int Id,
    string Name,
    string Summary,
    int Inventory,
    /// <summary>How many of this kind are still free on the busiest night of the searched stay.</summary>
    int Available,
    int MaxGuests,
    int Beds,
    double SizeSqm,
    decimal PricePerNight,
    string? ImageUrl,
    IReadOnlyList<string> Features);

public record QuoteRequest(int ListingId, DateOnly CheckIn, DateOnly CheckOut, int Guests);

/* ---- docs/01 MR-10: best-price guarantee -------------------------------- */

public record PriceMatchDto(
    int Id,
    int BookingId,
    string Reference,
    string CompetitorUrl,
    decimal CompetitorNightlyRate,
    decimal OurNightlyRate,
    decimal Difference,
    string Status,
    string StatusLabel,
    string? Decision,
    DateTime CreatedAt);

public record SubmitPriceMatchRequest(string? CompetitorUrl, decimal CompetitorNightlyRate);

/// <summary>One row of the price breakdown, already rounded. Negative means a reduction.</summary>
public record PriceLineDto(string Key, string Label, decimal Amount);

/* ------------------------------------------------------------ AT-09 support */

public record CreateSupportTicketRequest(string? Topic, string Subject, string Message, int? BookingId = null);
public record ResolveSupportTicketRequest(string? Reply);
public record SupportTicketDto(
    int Id, string Subject, string Message, bool Urgent,
    string RequesterName, string? BookingReference, DateTime CreatedAt);

/* ------------------------------------------------------ CĐ-06 change request */

public record ChangeBookingRequest(
    DateOnly CheckIn, DateOnly CheckOut, int Guests,
    int? Adults = null, int Children = 0, int Infants = 0, int Pets = 0);

public record ChangeRequestDto(
    int Id, DateOnly NewCheckIn, DateOnly NewCheckOut, int NewGuests,
    decimal NewTotal, decimal Difference, string DifferenceLabel,
    string Status, bool IsLive, DateTime ExpiresAt);

public record RespondChangeRequest(bool Accept);

/* ------------------------------------------------------- TM-26 city landing */

public record CityPageDto(
    string City, string Blurb, int Count, IReadOnlyList<ListingCardDto> Listings,
    /// <summary>1-based, already clamped to what exists.</summary>
    int Page = 1,
    int PageSize = 12,
    /// <summary>Never below 1, so an empty city still has a page 1.</summary>
    int TotalPages = 1);

/* ------------------------------------------------- QL-16 listing performance */

public record ListingPerformanceDto(
    int ListingId, string Title, bool IsPublished,
    int Views, int Saves, int Bookings,
    double ConversionPercent, double OccupancyPercent,
    /// <summary>
    /// docs/02 G7 — what the room actually sold for per night over the window.
    /// Zero when nothing sold, which is a fact rather than a gap.
    /// </summary>
    decimal AvgNightlyRate,
    /// <summary>The going rate for comparable places in the same city (CN-10's sample).</summary>
    decimal MarketMedian,
    /// <summary>How many places that median rests on. A small sample says so.</summary>
    int MarketSample);

/* ----------------------------------------------------- docs/02 G7 — báo cáo */

/// <summary>
/// One month of a host's earnings, split by whether the money has actually
/// arrived.
///
/// The split is the point of the block. <see cref="PayoutStatus.Paid"/> is the
/// only state that means the bank moved it — <c>Sent</c> is a line on a file
/// somebody still has to put through internet banking, and telling a host that
/// is "đã trả" would be the screen making a promise the ledger has not posted.
/// </summary>
public record ReportMonthDto(
    string Label, int Year, int Month,
    decimal Paid, decimal Upcoming, int Nights);

/// <summary>
/// docs/02 G7 — the six category scores of a month's reviews, so a host can see
/// which one is sliding rather than only that the average moved.
/// </summary>
public record ReportReviewMonthDto(
    string Label, int Year, int Month, int Count,
    double Overall, double Cleanliness, double Accuracy,
    double CheckIn, double Communication, double Location, double Value);

/// <summary>
/// docs/02 G7 — the whole report: money over time, how each listing is doing
/// against its own market, and where the reviews are heading.
/// </summary>
public record HostReportDto(
    int WindowDays,
    IReadOnlyList<ReportMonthDto> Months,
    IReadOnlyList<ListingPerformanceDto> Listings,
    IReadOnlyList<ReportReviewMonthDto> Reviews,
    /// <summary>Reviews behind the trend. Under this there is no trend to read.</summary>
    int ReviewCount);

/* ------------------------------------------------------- TC-04 tax report */

public record TaxReportMonthDto(
    int Month, string Label, int Stays,
    decimal GuestPaid, decimal Tax, decimal HostServiceFee, decimal HostPayout);

public record TaxReportLineDto(string Name, decimal Amount, int Stays);

public record TaxReportDto(
    int Year,
    /// <summary>Years with anything in them, newest first, for the picker.</summary>
    IReadOnlyList<int> Years,
    IReadOnlyList<TaxReportMonthDto> Months,
    IReadOnlyList<TaxReportLineDto> Taxes,
    int Stays,
    decimal GuestPaid,
    decimal RoomSubtotal,
    decimal GuestServiceFee,
    decimal Tax,
    decimal HostServiceFee,
    decimal HostPayout,
    string Note);

public record QuoteDto(
    int ListingId,
    int Nights,
    int Guests,
    decimal PricePerNight,
    decimal RoomBeforeDiscount,
    decimal RoomDiscount,
    int DiscountPercent,
    decimal ExtraGuestFee,
    decimal PetFee,
    decimal CleaningFee,
    decimal Subtotal,
    decimal ServiceFee,
    decimal Tax,
    decimal Total,
    decimal HostServiceFee,
    decimal HostPayout,
    /// <summary>Exactly what to render; the total is the sum of these.</summary>
    IReadOnlyList<PriceLineDto> Lines,
    bool GuestsExceeded,
    int MaxGuests,
    int MinNights,
    bool BelowMinNights,
    string CancellationTier,
    string CancellationSummary,
    /// <summary>docs/01 ĐP-09 — set when a code was applied; CouponError when one was refused.</summary>
    bool CouponApplied = false,
    decimal CouponDiscount = 0,
    string? CouponError = null);

/// <summary>Card details captured when the guest actually pays, not when the hold started.</summary>
public record PayBookingRequest(
    string? PaymentMethod,
    string? CardLast4,
    /// <summary>docs/01 ĐP-06 — take a deposit now instead of the whole amount.</summary>
    bool PayDeposit = false,
    /// <summary>How much of a deposit. Held to at least half the total.</summary>
    decimal? DepositAmount = null,
    /// <summary>
    /// docs/07 §7 — sent by the client so a retried request is recognised as the
    /// same attempt. Omitted, the server derives one from the booking and amount.
    /// </summary>
    string? IdempotencyKey = null,
    /// <summary>
    /// docs/07 §5 — the OTP the guest read off their bank's page. Absent on the
    /// first request: that one is what sends them there.
    /// </summary>
    string? AuthenticationCode = null,
    /// <summary>
    /// docs/07 §4 — keep this card for next time. With a licensed gateway it also
    /// decides whether this platform ever learns the card's last four digits, so
    /// it is not only a convenience (docs/07 §15.5).
    /// </summary>
    bool SaveCard = false,
    /// <summary>A card the guest already saved, to pay with instead of a new one.</summary>
    int? SavedCardId = null);

/// <summary>
/// docs/07 §5 — what the guest gets back when their bank wants a code. Carries
/// the attempt key so coming back to a closed tab resumes the same attempt.
/// </summary>
/// <summary>What the guest types on the bank's page (docs/07 §5).</summary>
public record BankOtpRequest(string AttemptKey, string? Code);

public record CardAuthChallengeDto(
    string AttemptKey,
    DateTime? HoldExpiresAt,
    int Attempts,
    int AttemptsLeft,
    string Message);

public record RefundPreviewDto(
    decimal Refund,
    decimal Penalty,
    decimal Total,
    string Explanation,
    decimal RoomRefund,
    decimal CleaningRefund,
    decimal ServiceFeeRefund,
    decimal TaxRefund,
    decimal GoodwillCredit,
    /* ------------------------------------------------------ docs/07 §10 */
    /// <summary>Back to the card the stay was paid with.</summary>
    decimal ToCard = 0,
    /// <summary>Back to the Staylio balance, which is where balance-funded money returns.</summary>
    decimal ToCredit = 0,
    /// <summary>Said before the guest confirms: where each part goes and how long it takes.</summary>
    string RefundTiming = "");

public record CreateBookingRequest(
    int ListingId,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Guests,
    string? GuestName,
    string? GuestEmail,
    string? GuestNote,
    string? PaymentMethod,
    string? CardLast4,
    int? Adults = null,
    int Children = 0,
    int Infants = 0,
    int Pets = 0,
    /// <summary>docs/01 MR-09 — which kind of room, when the listing is a hotel.</summary>
    int? RoomTypeId = null,
    /// <summary>
    /// docs/07 §6 — what the guest was reading prices in. The RATE is looked up
    /// server-side and stamped from exchange_rates: a rate the guest's own
    /// browser supplied is worthless as the evidence this column exists to be,
    /// and the field that once accepted one is gone so no caller can bring the
    /// trust back.
    /// </summary>
    string? DisplayCurrency = null,
    /// <summary>Spend the guest's balance on this booking, up to the room charge.</summary>
    bool UseCredit = false,
    /// <summary>docs/01 ĐP-09 — a promo code, applied before the balance.</summary>
    string? CouponCode = null,
    /// <summary>docs/01 ĐP-17 — a host's private offer being booked at its price.</summary>
    int? OfferId = null,
    /// <summary>docs/01 ĐP-10 — the guest ticked "I agree to the house rules".</summary>
    bool AgreedToRules = false,
    /// <summary>
    /// docs/07 §2.5 — how to reach somebody who booked without an account. An
    /// account already carries a phone; a stranger has to leave one.
    /// </summary>
    string? GuestPhone = null,
    /* StayDetails — told to the host, never priced. */
    int? EstimatedArrivalHour = null,
    string? StayingGuestName = null,
    bool IsBusinessTrip = false,
    IReadOnlyList<string>? SpecialRequests = null);

/// <summary>The same details, corrected after booking while the stay is ahead.</summary>
public record StayDetailsRequest(
    int? EstimatedArrivalHour, string? StayingGuestName, bool IsBusinessTrip,
    IReadOnlyList<string>? SpecialRequests);

/// <summary>What the host and the guest read back: keys for the form, labels for the eye.</summary>
public record StayDetailsDto(
    int? EstimatedArrivalHour, string? ArrivalLabel, string? StayingGuestName,
    bool IsBusinessTrip, IReadOnlyList<string> SpecialRequests, IReadOnlyList<string> SpecialRequestLabels)
{
    public static StayDetailsDto Of(StayHost.Domain.Booking b) => new(
        b.EstimatedArrivalHour, StayHost.Domain.StayDetails.ArrivalLabel(b.EstimatedArrivalHour), b.StayingGuestName,
        b.IsBusinessTrip, StayHost.Domain.StayDetails.Keys(b.SpecialRequests), StayHost.Domain.StayDetails.Labels(b.SpecialRequests));
}

/// <summary>docs/07 §2.5 — what the host just recorded, and what it costs them in fees.</summary>
public record CashCollectedDto(
    string Reference, decimal Collected, decimal FeesBilled, DateTime At, bool AlreadyRecorded);

/// <summary>docs/07 §2.5 — the two things a guest with no account has to find a booking with.</summary>
public record LookupBookingRequest(string? Reference, string? Email);

public record BookingDto(
    int Id,
    string Reference,
    int ListingId,
    string ListingTitle,
    string ListingCity,
    string ListingImage,
    string ListingSlug,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    int Guests,
    decimal Subtotal,
    decimal CleaningFee,
    decimal ServiceFee,
    decimal Tax,
    decimal Total,
    decimal RefundedAmount,
    decimal GoodwillCredit,
    /// <summary>The rows exactly as they were quoted, so an old receipt still adds up.</summary>
    IReadOnlyList<PriceLineDto> Lines,
    string CancellationTier,
    string CancellationSummary,
    string Status,
    /// <summary>The Vietnamese wording; the server owns it so every screen agrees.</summary>
    string StatusLabel,
    /// <summary>pending / confirmed / cancelled — the badge classes the UI already has.</summary>
    string StatusBadge,
    string PaymentStatus,
    string? PaymentReference,
    string? PaymentMethod,
    string? CardLast4,
    bool HasReview,
    bool CanReview,
    bool CanCancel,
    /// <summary>When the 15-minute payment hold lapses, if one is running.</summary>
    DateTime? HoldExpiresAt,
    /// <summary>When an unanswered request to book expires.</summary>
    DateTime? RequestExpiresAt,
    /// <summary>Append-only history, oldest first (docs/00 §6.2).</summary>
    IReadOnlyList<BookingEventDto> History,
    string? GuestNote,
    string HostName,
    DateTime CreatedAt,
    // --- docs/01 ĐP-06: paying a deposit now and the rest later.
    decimal DepositPaid = 0,
    decimal BalanceDue = 0,
    DateOnly? BalanceDueOn = null,
    string BalanceStatus = "None",
    string BalanceLabel = "",
    /// <summary>
    /// docs/01 CĐ-03 — null until the stay is confirmed (docs/03 §10), so an
    /// unanswered request never carries an address.
    /// </summary>
    CheckInGuideDto? CheckInGuide = null,
    /// <summary>
    /// docs/07 §13 — set only by the pay endpoint, and only when the method the
    /// guest picked is served by a licensed gateway: where to send them so they
    /// can pay on that gateway's own page. Null everywhere else, including on the
    /// same booking read back a second later.
    /// </summary>
    string? GatewayRedirectUrl = null,
    /// <summary>What the gateway will know that payment by.</summary>
    string? GatewayOrderRef = null,
    /// <summary>
    /// docs/01 MR-10 — a hotel room, still inside the 24 hours in which a
    /// cheaper price elsewhere can be claimed. Said here so the form is offered
    /// only where the server would accept it, rather than everywhere and
    /// refused.
    /// </summary>
    bool CanPriceMatch = false,
    /// <summary>docs/07 §2.5 — the guest settles with the host on arrival.</summary>
    bool PaidAtProperty = false,
    /// <summary>docs/07 §2.5 — set once the host confirmed the cash is in hand.</summary>
    DateTime? CashCollectedAt = null)
{
    /// <summary>
    /// The party behind <see cref="Guests"/>. A change request that only knew
    /// the total sent the pets and children as zero, and accepting it dropped
    /// the pet fee from a stay that still had the pet.
    /// </summary>
    public int Adults { get; init; }
    public int Children { get; init; }
    public int Infants { get; init; }
    public int Pets { get; init; }
    public StayDetailsDto? Details { get; init; }
}

public record BookingEventDto(
    string? FromStatus, string FromLabel, string ToStatus, string ToLabel,
    string Actor, string Reason, DateTime At);

/* ---- docs/01 QL-19: co-hosts --------------------------------------------- */

public record ScopeOptionDto(string Key, string Label);

public record CoHostDto(
    int Id,
    string Email,
    string? Name,
    /// <summary>Null means every listing this host has, now and later.</summary>
    int? ListingId,
    string? ListingTitle,
    IReadOnlyList<string> Scopes,
    string ScopeLabel,
    string Status,
    string StatusLabel,
    DateTime InvitedAt,
    /* docs/02 G8 — the optional cut of the owner's earnings. */
    string PayoutKind = "none",
    decimal PayoutPercent = 0m,
    decimal PayoutFixed = 0m,
    string PayoutStatus = "none",
    string PayoutStatusLabel = "Không chia thu nhập",
    DateTime? PayoutConfirmBy = null,
    /// <summary>What this person has actually been paid so far, across every stay.</summary>
    decimal PaidToDate = 0m);

/// <summary>The same grant seen from the other side, by the person invited.</summary>
public record CoHostInviteDto(
    int Id,
    string Token,
    string OwnerName,
    int? ListingId,
    string? ListingTitle,
    string ScopeLabel,
    string Status,
    string StatusLabel,
    string PayoutKind = "none",
    decimal PayoutPercent = 0m,
    decimal PayoutFixed = 0m,
    string PayoutStatus = "none",
    string PayoutStatusLabel = "Không chia thu nhập",
    DateTime? PayoutConfirmBy = null,
    decimal PaidToDate = 0m,
    /// <summary>
    /// docs/07 §19.3 — a share cannot be paid to somebody with nowhere to send
    /// it, so the confirm screen has to say whether that is set up.
    /// </summary>
    bool HasPayoutAccount = false);

/// <summary>One stay a co-host was paid a share of (docs/07 §19).</summary>
public record CoHostEarningDto(
    long Id,
    string BookingReference,
    string? ListingTitle,
    DateOnly CheckIn,
    decimal Amount,
    /// <summary>
    /// The terms as they stood for this stay, in parts. The screen builds its
    /// own sentence: the Vietnamese one carries a percentage inside it, and a
    /// dictionary cannot key on "20% mỗi đơn".
    /// </summary>
    string Kind,
    decimal Percent,
    decimal Fixed,
    string Status,
    string StatusLabel,
    DateTime? PaidOutAt,
    decimal ClawedBack);

public record CoHostBoardDto(
    IReadOnlyList<CoHostDto> Invited,
    IReadOnlyList<CoHostInviteDto> Helping,
    IReadOnlyList<ScopeOptionDto> Scopes,
    IReadOnlyList<PayoutKindOptionDto> PayoutKinds,
    IReadOnlyList<CoHostEarningDto> Earnings,
    decimal EarnedToDate,
    /// <summary>
    /// The percentages the owner has actually promised away, when they add up to
    /// more than 100 (docs/07 §19.1); zero when they do not. A number rather than
    /// a sentence: the warning reads differently in each language and the number
    /// belongs in the middle of it.
    /// </summary>
    decimal OvercommittedPercent = 0m);

public record PayoutKindOptionDto(string Key, string Label, bool NeedsPercent, bool NeedsAmount);

public record InviteCoHostRequest(string? Email, int? ListingId, IReadOnlyList<string>? Scopes);

/// <summary>docs/02 G8 — the owner proposing a share of what they earn.</summary>
public record CoHostPayoutRequest(string? Kind, decimal Percent, decimal Amount);

/* ---- docs/01 QL-10: calendar sync ---------------------------------------- */

public record CalendarFeedDto(
    int Id,
    string Label,
    string Url,
    DateTime? LastSyncedAt,
    /// <summary>Set when the last attempt failed; the blocks from before are kept.</summary>
    string? LastError,
    int EventCount,
    /// <summary>docs/01 QL-11 — set when the import clashes with a confirmed booking.</summary>
    string? OverlapWarning = null);

public record CalendarSyncDto(
    int ListingId,
    string Title,
    /// <summary>The address other platforms subscribe to. The token in it is the credential.</summary>
    string ExportUrl,
    IReadOnlyList<CalendarFeedDto> Feeds);

public record AddCalendarFeedRequest(string? Url, string? Label);

/* ---- docs/01 AT-07: the help centre ------------------------------------- */

public record HelpArticleDto(
    string Slug,
    string Title,
    string Category,
    string Audience,
    string AudienceLabel,
    string Summary,
    /// <summary>Only filled in when a single article was asked for.</summary>
    string? Body,
    DateTime UpdatedAt);

public record HelpCategoryDto(string Name, int Count);

public record HelpIndexDto(
    IReadOnlyList<HelpArticleDto> Articles,
    IReadOnlyList<HelpCategoryDto> Categories,
    int Total);

/* ---- docs/01 AT-11: anomaly detection ----------------------------------- */

public record RiskFlagDto(
    int Id,
    int UserId,
    string UserName,
    string UserEmail,
    int? BookingId,
    string? BookingReference,
    string Kind,
    string Severity,
    string SeverityLabel,
    string SeverityBadge,
    string Summary,
    string Detail,
    string Status,
    string? Resolution,
    DateTime CreatedAt);

public record ResolveRiskFlagRequest(string? Resolution, bool Acted = false);

/* ---- docs/01 ĐP-07: one booking, up to sixteen payers -------------------- */

public record BillShareDto(
    int Id,
    string Email,
    string? Name,
    decimal Amount,
    string Status,
    string StatusLabel,
    /// <summary>The link this person follows. Whoever holds it can pay this share and nothing else.</summary>
    string Link,
    DateTime? PaidAt);

public record BillSplitDto(
    int Id,
    int BookingId,
    string Reference,
    decimal Total,
    string Status,
    string StatusLabel,
    DateTime ExpiresAt,
    IReadOnlyList<BillShareDto> Shares);

public record OpenSplitRequest(IReadOnlyList<string>? Emails);

/// <summary>What someone sees when they open their link, with no account.</summary>
public record ShareInviteDto(
    string Token,
    string Reference,
    string ListingTitle,
    string City,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    int Guests,
    decimal Total,
    decimal Amount,
    string ShareStatus,
    string ShareStatusLabel,
    string SplitStatus,
    string SplitStatusLabel,
    int PaidCount,
    int PeopleCount,
    DateTime ExpiresAt,
    /// <summary>docs/07 §13 — where to send the payer when a licensed gateway takes the money.</summary>
    string? GatewayRedirectUrl = null,
    string? GatewayOrderRef = null);

public record PayShareRequest(string? Name, string? CardLast4, string? PaymentMethod = null);

/* ---- docs/01 MR-01 → MR-04: experiences ---------------------------------- */

public record ExperienceCardDto(
    int Id,
    string Slug,
    string Title,
    string City,
    string Summary,
    int DurationMinutes,
    int MaxGroup,
    decimal PricePerPerson,
    double Rating,
    int ReviewCount,
    string HostName,
    IReadOnlyList<string> Images,
    /// <summary>Sessions still on sale, so a card can say "còn 3 suất".</summary>
    int OpenSlots);

public record ExperienceSlotDto(
    int Id,
    DateTime StartsAt,
    int Capacity,
    int SeatsTaken,
    int SeatsLeft,
    bool IsPrivate,
    string Status,
    string? CancelReason);

/// <summary>docs/01 MR-01 — one stop on the session, in the order it happens.</summary>
public record ExperienceStepDto(string Title, string Description, string? ImageUrl);

public record ExperienceDetailDto(
    int Id,
    string Slug,
    string Title,
    string City,
    string Country,
    string Summary,
    string Description,
    int DurationMinutes,
    int MaxGroup,
    int MinGuests,
    IReadOnlyList<string> Languages,
    int MinAge,
    string MeetingPoint,
    double Latitude,
    double Longitude,
    IReadOnlyList<string> Included,
    decimal PricePerPerson,
    decimal? PrivateGroupPrice,
    bool IsPublished,
    double Rating,
    int ReviewCount,
    string HostName,
    string HostInitials,
    IReadOnlyList<string> Images,
    IReadOnlyList<ExperienceSlotDto> Slots,
    // docs/09 §2.1–§2.3 — what the activity is and the papers its band demands,
    // so the host's editor prefills from the server rather than from a local
    // cache that a different browser would not have.
    string Category = "",
    bool AllowsChildren = false,
    string? LicenceName = null,
    DateOnly? LicenceExpiresOn = null,
    string? InsurancePolicy = null,
    DateOnly? InsuranceExpiresOn = null,
    string? SafetyPlan = null,
    string? EmergencyPhone = null,
    // docs/09 §2.2 — where it stands with the reviewer, so a submission waiting
    // in the queue does not look the same as one that was turned down.
    string ModerationStatus = "Draft",
    string? ReviewerNote = null,
    DateTime? SubmittedForReviewAt = null,
    // docs/01 MR-01 — what happens, in order. Empty when the host has not written
    // one, and the page then leaves the section out rather than showing a heading
    // with nothing under it.
    IReadOnlyList<ExperienceStepDto>? Itinerary = null);

public record ExperienceQuoteDto(
    int SlotId,
    DateTime StartsAt,
    int Seats,
    bool Private,
    decimal PerSeat,
    decimal Subtotal,
    decimal ServiceFee,
    decimal Tax,
    decimal Total,
    IReadOnlyList<PriceLineDto> Lines,
    bool CanBook,
    string? Reason);

public record ExperienceBookingDto(
    int Id,
    string Reference,
    int ExperienceId,
    string Title,
    string City,
    string Slug,
    DateTime StartsAt,
    int DurationMinutes,
    int Seats,
    bool Private,
    decimal Subtotal,
    decimal ServiceFee,
    decimal Tax,
    decimal Total,
    decimal RefundedAmount,
    string Status,
    string StatusLabel,
    string StatusBadge,
    string? CancelReason,
    DateTime CreatedAt,
    // docs/09 §2.9–§2.10 — whether the host marked them present, and whether they
    // have already written their review, so a ticket never offers a form the
    // server is only going to refuse.
    bool? Attended = null,
    bool HasReview = false)
{
    /// <summary>docs/07 §13 — where to send the guest when a licensed gateway takes the money.</summary>
    public string? GatewayRedirectUrl { get; init; }

    /// <summary>What the gateway will know that payment by.</summary>
    public string? GatewayOrderRef { get; init; }
}

public record BookExperienceRequest(
    int Seats,
    bool Private = false,
    string? PaymentMethod = null,
    string? CardLast4 = null,
    /// <summary>docs/09 §2.7 (MR-E-06) — the ten-minute hold these seats came from.</summary>
    int? HoldId = null);

/// <summary>docs/09 §2.7 — seats taken off a session while the guest pays.</summary>
public record HoldSeatsRequest(int Seats, bool Private = false);

public record ExperienceHoldDto(int HoldId, int SlotId, int Seats, bool Private, DateTime ExpiresAt);

public record SaveExperienceRequest(
    int? Id,
    string? Title,
    string? City,
    string? Summary,
    string? Description,
    int DurationMinutes,
    int MaxGroup,
    int MinGuests,
    IReadOnlyList<string>? Languages,
    int MinAge,
    string? MeetingPoint,
    double Latitude,
    double Longitude,
    IReadOnlyList<string>? Included,
    decimal PricePerPerson,
    decimal? PrivateGroupPrice,
    IReadOnlyList<string>? Images,
    bool Publish = false,
    // docs/09 §2.1–§2.3 — the activity and the papers its risk band demands.
    string? Category = null,
    bool AllowsChildren = false,
    string? LicenceName = null,
    DateOnly? LicenceExpiresOn = null,
    string? InsurancePolicy = null,
    DateOnly? InsuranceExpiresOn = null,
    string? SafetyPlan = null,
    string? EmergencyPhone = null,
    IReadOnlyList<ExperienceStepDto>? Itinerary = null);

/// <summary>docs/09 §2.2 (MR-E-03) — one row of the reviewer's queue.</summary>
public record PendingExperienceDto(
    int Id, string Slug, string Title, string City,
    string Category, string RiskLabel, bool AllowsChildren,
    string? LicenceName, DateOnly? LicenceExpiresOn,
    string? InsurancePolicy, DateOnly? InsuranceExpiresOn,
    string? SafetyPlan, string? EmergencyPhone,
    string HostName, int HostUserId, string? CoverImage,
    DateTime? SubmittedAt, int ReviewWorkingDays);

/// <summary>The reviewer's answer: approve · changes · reject, with a reason.</summary>
public record ReviewExperienceRequest(string? Decision, string? Note);

/// <summary>docs/09 §2.9 (MR-E-09) — one guest on the host's register.</summary>
public record SessionGuestDto(
    int BookingId, string Reference, string GuestName, int GuestUserId,
    int Seats, bool IsPrivate, bool? Attended, DateTime? MarkedAt);

/// <summary>The whole register for one session, with whether it may be taken yet.</summary>
public record SessionRosterDto(
    int SlotId, string Title, DateTime StartsAt, DateTime EndsAt,
    int Capacity, int SeatsTaken, bool CanMark, int LateAllowanceMinutes,
    IReadOnlyList<SessionGuestDto> Guests);

/// <summary>docs/09 §2.9 — present or absent.</summary>
public record MarkAttendanceRequest(bool Attended);

/// <summary>docs/09 §2.10 (MR-E-11) — the four criteria, each 1–5.</summary>
public record SubmitExperienceReviewRequest(
    int Host, int AsDescribed, int Safety, int Value, string? Comment);

public record ExperienceReviewDto(
    int Id, string AuthorName, string? AuthorAvatarUrl,
    int Host, int AsDescribed, int Safety, int Value,
    string Comment, DateTime CreatedAt);

/// <summary>
/// docs/09 §2.5 (MR-E-04) — sessions added one at a time, or described once as a
/// repeating pattern: which weekdays (Monday = bit 0), at what time, for how many
/// weeks from when.
/// </summary>
public record AddSlotsRequest(
    IReadOnlyList<DateTime>? StartsAt,
    int? Capacity,
    int RepeatWeekdayMask = 0,
    TimeOnly? RepeatAt = null,
    DateOnly? RepeatFrom = null,
    int RepeatWeeks = 0);

/* ---- docs/01 MR-05 → MR-07: services ------------------------------------ */

public record ServiceCardDto(
    int Id,
    string Slug,
    string Title,
    string Category,
    string City,
    string Summary,
    decimal BasePrice,
    string Pricing,
    string PricingLabel,
    string Unit,
    bool TravelsToGuest,
    int ServiceRadiusKm,
    /// <summary>docs/01 MR-07 — run by somebody else, with the platform on commission.</summary>
    bool IsPartner,
    string? PartnerName,
    double Rating,
    int ReviewCount,
    string HostName,
    IReadOnlyList<string> Images,
    /// <summary>How long one booking runs, so a card can say "1 giờ" the way the price says "/ buổi".</summary>
    int DurationMinutes = 0,
    string? HostAvatarUrl = null);

/// <summary>A stretch of the provider's diary that is already spoken for.</summary>
public record BusySlotDto(DateTime From, DateTime To);

public record ServiceDetailDto(
    int Id,
    string Slug,
    string Title,
    string Category,
    string City,
    string Country,
    string Summary,
    string Description,
    decimal BasePrice,
    string Pricing,
    string PricingLabel,
    string Unit,
    int MinQuantity,
    int MaxQuantity,
    int DurationMinutes,
    bool TravelsToGuest,
    int ServiceRadiusKm,
    double Latitude,
    double Longitude,
    int OpensAtHour,
    int ClosesAtHour,
    bool IsPartner,
    string? PartnerName,
    bool IsPublished,
    double Rating,
    int ReviewCount,
    string HostName,
    string HostInitials,
    IReadOnlyList<string> Images,
    IReadOnlyList<BusySlotDto> Busy,
    /// <summary>docs/09 §3.5 — the note this category makes mandatory, or null.</summary>
    string? RequiredNote,
    // docs/09 §3.3–§3.4 — the extras, what the place must have, and how far the
    // provider will travel for a fee.
    IReadOnlyList<ServiceAddOnDto>? AddOns = null,
    IReadOnlyList<string>? OnSiteRequirements = null,
    decimal TravelFeePerKm = 0,
    int MaxTravelKm = 0,
    int WorkingDaysMask = 127,
    int MaxJobsPerDay = 0,
    string? CertificateName = null,
    DateOnly? CertificateExpiresOn = null,
    /// <summary>0 means the platform's own 30-minute gap between jobs.</summary>
    int BufferMinutes = 0,
    // The provider as a person, for the identity card the page opens with and the
    // "Trình độ của tôi" block: docs/09 §3.2 sells a service on who is coming.
    string? HostAvatarUrl = null,
    int HostYears = 0,
    string? HostBio = null,
    bool HostIsSuperhost = false,
    int? HostUserId = null,
    /// <summary>docs/09 §3.5 — the provider accepts each job before it is confirmed.</summary>
    bool RequiresConfirmation = false);

public record ServiceQuoteDto(
    int OfferingId,
    DateTime StartsAt,
    int DurationMinutes,
    int Quantity,
    decimal Subtotal,
    decimal ServiceFee,
    decimal Tax,
    decimal Total,
    IReadOnlyList<PriceLineDto> Lines,
    bool CanBook,
    string? Reason);

public record ServiceBookingDto(
    int Id,
    string Reference,
    int OfferingId,
    string Title,
    string Slug,
    string Category,
    string City,
    DateTime StartsAt,
    int DurationMinutes,
    int Quantity,
    string Unit,
    string Address,
    string? Note,
    decimal Subtotal,
    decimal ServiceFee,
    decimal Tax,
    decimal Total,
    decimal RefundedAmount,
    string Status,
    string StatusLabel,
    string StatusBadge,
    string? CancelReason,
    DateTime CreatedAt,
    /// <summary>docs/09 §5 — set once this job has been scored, so nobody is asked twice.</summary>
    bool HasReview = false)
{
    /// <summary>docs/07 §13 — where to send the guest when a licensed gateway takes the money.</summary>
    public string? GatewayRedirectUrl { get; init; }

    /// <summary>What the gateway will know that payment by.</summary>
    public string? GatewayOrderRef { get; init; }
}

/// <summary>
/// docs/07 §2.3 — everything a page needs to draw a VietQR and tell the guest
/// what they are about to do. <c>Payload</c> is the string that goes into the QR
/// image; the rest is for the humans who would rather type it into their banking
/// app by hand, or check that the name on the screen is who they meant to pay.
/// </summary>
public record BankTransferQrDto(
    string Payload,
    string BankName,
    string AccountNumber,
    string AccountName,
    string Memo,
    decimal Amount,
    /// <summary>When the booking stops holding its dates or seats, so the page can count down.</summary>
    DateTime? ExpiresAt);

/// <summary>
/// docs/07 §2.3 — what the guest's QR page asks while it waits: has the money
/// been found, is it still worth waiting, and where to go once it has.
/// </summary>
public record BankTransferStatusDto(
    string Status,
    string StatusLabel,
    bool Confirmed,
    bool StillWaiting,
    DateTime? ExpiresAt,
    string NextUrl);

/* ---------------------------------------- docs/07 §2.3, the finance desk's side */

/// <summary>One booking waiting for its money, and how much.</summary>
public record AwaitedTransferDto(string Reference, decimal Amount);

/// <summary>A credit read off a statement that a person still has to answer for.</summary>
public record BankCreditDto(
    long Id, string BankReference, decimal Amount, string Description,
    string Verdict, string VerdictLabel, string? MatchedReference, decimal Expected,
    DateTime ImportedAt);

public record BankTransferDeskDto(
    IReadOnlyList<AwaitedTransferDto> Awaited,
    IReadOnlyList<BankCreditDto> Open);

public record StatementLineDto(string? BankReference, decimal Amount, string? Description);

public record ImportStatementRequest(List<StatementLineDto>? Lines);

public record BankImportRowDto(
    string BankReference, decimal Amount, string Description,
    string Verdict, string VerdictLabel, string? MatchedReference, decimal Expected,
    string Explanation);

public record BankImportResultDto(
    int Settled, int Pending, int Skipped, IReadOnlyList<BankImportRowDto> Rows);

public record ResolveCreditRequest(string? Note);

/// <summary>
/// docs/07 §15.4 — a statement of what left the account, read against the
/// transfers the platform said it made. `Note` is required for the same reason
/// the button requires one: settling a batch posts a ledger entry.
/// </summary>
public record ReconcilePayoutsRequest(string? Note, List<StatementLineDto>? Lines);

public record PayoutReconcileRowDto(
    string BankReference, decimal Amount, string Description,
    string Verdict, string VerdictLabel, string? MatchedReference, decimal Expected,
    string Explanation);

public record PayoutReconcileResultDto(
    int Settled, int Pending, int Skipped, IReadOnlyList<PayoutReconcileRowDto> Rows,
    /// <summary>What the statement did not account for — the half a statement cannot show.</summary>
    IReadOnlyList<PayoutAwaitingBankDto> StillAwaitingBank);

public record PayoutAwaitingBankDto(
    long Id, string Reference, string HostName, decimal Amount,
    string BankName, string AccountName, DateOnly DueOn, int DaysWaiting);

/// <summary>
/// docs/07 §13 — one bank transfer the platform owes a host.
///
/// The account number is masked; the whole number exists only in the file, which
/// is a separate and audited action (docs/07 §14.3).
/// </summary>
public record PayoutBatchDto(
    long Id, string Reference, string HostName, string AccountName, string BankName,
    string AccountMasked, decimal Amount, decimal Deducted, int BookingCount,
    string Status, string StatusLabel, DateOnly DueOn, DateTime? SettledAt, string? Note);

/// <summary>
/// <paramref name="Blocked"/> is the sentence to put at the top when the server
/// cannot produce transfers at all — an empty list otherwise reads as "nothing
/// to pay" when it means "cannot pay anybody".
/// </summary>
public record PayoutBatchesDto(
    IReadOnlyList<PayoutBatchDto> Batches, int Waiting, decimal WaitingAmount, string? Blocked);

/// <summary>docs/09 §5 — one guest's word on one job, on the four service headings.</summary>
public record ServiceReviewDto(
    int Id, string AuthorName, string? AuthorAvatarUrl,
    int Skill, int AsDescribed, int Punctuality, int Value,
    string Comment, DateTime CreatedAt);

public record SubmitServiceReviewRequest(
    int Skill, int AsDescribed, int Punctuality, int Value, string? Comment);

public record QuoteServiceRequest(
    DateTime StartsAt,
    int Quantity,
    string? Address,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<int>? AddOnIds = null,
    bool ConditionsConfirmed = false);

public record BookServiceRequest(
    DateTime StartsAt,
    int Quantity,
    string? Address,
    double? Latitude,
    double? Longitude,
    string? Note,
    string? PaymentMethod,
    string? CardLast4,
    // docs/09 §3.3 — the extras ticked, and the guest's word that the place has
    // what the job needs (MR-S-03, MR-S-07).
    IReadOnlyList<int>? AddOnIds = null,
    bool ConditionsConfirmed = false);

/// <summary>docs/09 §3.3 (MR-S-03) — one paid extra as the provider defines it.</summary>
public record ServiceAddOnDto(int Id, string Name, decimal Price);

public record SaveServiceAddOnRequest(string? Name, decimal Price);

/// <summary>docs/09 §3.2–§3.4 (MR-S-01) — a provider listing their own service.</summary>
public record SaveServiceRequest(
    int? Id,
    string? Title,
    string? Category,
    string? City,
    string? Summary,
    string? Description,
    string? Pricing,
    decimal BasePrice,
    int MinQuantity,
    int MaxQuantity,
    int DurationMinutes,
    bool TravelsToGuest,
    int ServiceRadiusKm,
    double Latitude,
    double Longitude,
    int OpensAtHour,
    int ClosesAtHour,
    IReadOnlyList<string>? Images,
    bool Publish = false,
    decimal TravelFeePerKm = 0,
    int MaxTravelKm = 0,
    int WorkingDaysMask = 127,
    int BufferMinutes = 0,
    int MaxJobsPerDay = 0,
    IReadOnlyList<string>? OnSiteRequirements = null,
    IReadOnlyList<SaveServiceAddOnRequest>? AddOns = null,
    string? CertificateName = null,
    DateOnly? CertificateExpiresOn = null,
    bool RequiresConfirmation = false);

/* ---- docs/09 §4 (MR-C-02): cross-sell from a stay ------------------------ */

/// <summary>
/// docs/09 §4 — what a guest who already has a stay could do while they are
/// there: experiences and services in that city, inside those dates. Both lists
/// are the very cards the browse pages use, so the trip page shows them with
/// the components already written instead of a second kind of card.
/// </summary>
public record StaySuggestionsDto(
    string City,
    /// <summary>The window relevance was decided on — the nights of the stay.</summary>
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ExperienceCardDto> Experiences,
    IReadOnlyList<ServiceCardDto> Services);

/* ---- gift cards, balance and referrals ---------------------------------- */

public record CreditEntryDto(
    long Id,
    /// <summary>Positive when granted, negative when spent.</summary>
    decimal Amount,
    string Reason,
    string ReasonLabel,
    string Memo,
    int? BookingId,
    DateTime CreatedAt,
    /// <summary>docs/01 TC-07 — set on a grant that lapses; null means it does not.</summary>
    DateTime? ExpiresAt);

public record GiftCardDto(
    int Id,
    string Code,
    decimal Amount,
    decimal Remaining,
    string RecipientEmail,
    string? RecipientName,
    string? Message,
    string Status,
    string StatusLabel,
    DateTime CreatedAt,
    DateTime? RedeemedAt);

public record ReferralDto(
    int Id,
    string Code,
    string InviteeEmail,
    string? InviteeName,
    string Status,
    string StatusLabel,
    decimal ReferrerReward,
    decimal InviteeReward,
    DateTime CreatedAt);

public record WalletDto(
    decimal Balance,
    IReadOnlyList<CreditEntryDto> Entries,
    IReadOnlyList<GiftCardDto> GiftCards,
    IReadOnlyList<ReferralDto> Referrals,
    decimal ReferrerReward,
    decimal InviteeReward,
    decimal MinGiftCard,
    decimal MaxGiftCard,
    /// <summary>docs/01 TC-07 — the next date something lapses, and how much.
    /// Both null while nothing on the account expires.</summary>
    DateTime? NextExpiryAt,
    decimal ExpiringAmount);

public record BuyGiftCardRequest(
    decimal Amount, string? RecipientEmail, string? RecipientName, string? Message,
    /// <summary>
    /// docs/01 TC-08 — how the buyer is paying. A card is a purchase, so this is
    /// no more optional than it is at a checkout; the sale used to take no
    /// payment at all. Defaults to "card" so an older client is asked for money
    /// rather than handed a free card.
    /// </summary>
    string? Method = null,
    /// <summary>Only reaches the stand-in gateway, which reads it to refuse the test card.</summary>
    string? CardLast4 = null);

/// <summary>
/// What came of ordering a card: either the card itself, paid for, or the
/// address of the gateway the buyer has to visit before it is worth anything.
/// </summary>
public record GiftCardPurchaseDto(
    GiftCardDto? Card,
    /// <summary>Where to send the buyer, when a licensed gateway is taking the money.</summary>
    string? GatewayRedirectUrl = null);
public record RedeemGiftCardRequest(string? Code);
public record InviteFriendRequest(string? Email);

/* ---- docs/06 — Staylio Shield ------------------------------------------------ */

public record ShieldEvidenceDto(int Id, string Url, string? Caption, string Kind);

public record ShieldItemDto(
    int Id,
    string Name,
    decimal Value,
    /// <summary>docs/06 §3.2 C-E — declared on the listing before the guest arrived.</summary>
    bool DeclaredOnListing,
    /// <summary>What the per-item ceiling let through.</summary>
    decimal Allowed);

public record ShieldEventDto(
    int Id,
    string? FromStatus,
    string ToStatus,
    string ToStatusLabel,
    string Actor,
    string Note,
    DateTime CreatedAt);

public record ShieldClaimDto(
    int Id,
    string Reference,
    int BookingId,
    string BookingReference,
    string ListingTitle,
    string ListingSlug,
    string Side,
    string Kind,
    string KindLabel,
    string Status,
    string StatusLabel,
    string StatusBadge,
    string Description,
    decimal Claimed,
    decimal ExpensesClaimed,
    decimal RehousingDifference,
    string Remedy,
    decimal Approved,
    decimal Deductible,
    decimal CreditGranted,
    decimal PaidFromFund,
    decimal RecoveredFromCounterparty,
    decimal RecoveredLater,
    /// <summary>docs/06 §3.1 C4 — who the damage was actually done to.</summary>
    string? ThirdPartyName,
    string? ThirdPartyContact,
    string? ThirdPartyKind,
    string? Decision,
    DateTime? DecidedAt,
    bool Appealed,
    /// <summary>docs/06 §7 — a flagged account never settles itself.</summary>
    bool NeedsManualReview,
    DateTime RespondBy,
    DateTime FirstResponseDueAt,
    DateTime DecisionDueAt,
    DateTime CreatedAt,
    string OpenedByName,
    bool OpenedByMe,
    IReadOnlyList<ShieldEvidenceDto> Evidence,
    IReadOnlyList<ShieldItemDto> Items,
    IReadOnlyList<ShieldEventDto> Events);

public record ShieldCaseTotalDto(string Kind, string Label, int Count, decimal PaidFromFund);

public record ShieldFundDto(
    decimal Balance,
    decimal ContributedThisMonth,
    decimal SpentThisMonth,
    decimal RecoveredThisMonth,
    /// <summary>docs/06 §5 — spending has passed the warning threshold for the month.</summary>
    bool Alarm,
    decimal ContributionRate,
    decimal AlarmRate,
    IReadOnlyList<ShieldCaseTotalDto> ByCase);

public record ShieldEvidenceInput(string? Url, string? Caption, string? Kind);

public record ShieldItemInput(string? Name, decimal Value, bool DeclaredOnListing);

public record OpenShieldClaimRequest(
    string? Kind,
    string? Description,
    /// <summary>docs/06 §2.2 — a safety matter or strangers inside skips the waiting period.</summary>
    bool Urgent = false,
    decimal ExpensesClaimed = 0,
    decimal RehousingDifference = 0,
    IReadOnlyList<ShieldEvidenceInput>? Evidence = null,
    IReadOnlyList<ShieldItemInput>? Items = null,
    /// <summary>docs/06 §3.1 C4 — required when the loss is somebody else's.</summary>
    string? ThirdPartyName = null,
    string? ThirdPartyContact = null,
    string? ThirdPartyKind = null);

public record RespondShieldRequest(string? Answer, decimal? AgreedAmount, string? Note);

public record DecideShieldRequest(
    bool Approve,
    string? Reason,
    /// <summary>Guest cases: Rehoused · SelfRehoused · Refunded (docs/06 §2.3).</summary>
    string? Remedy = null,
    int? NightsUnused = null,
    /// <summary>Host cases: what the arbitration allows before ceilings and the excess.</summary>
    decimal? ApprovedAmount = null,
    decimal? DepositAvailable = null,
    decimal? RecoverFromGuest = null);

public record RecoverShieldRequest(decimal Amount);

/// <summary>docs/06 §8 AT-06-01 — what the programme covers, in the platform's own words.</summary>
public record ShieldTermsDto(
    string Side,
    string Title,
    string Intro,
    IReadOnlyList<ShieldTermsSectionDto> Sections,
    IReadOnlyList<string> Exclusions,
    string Disclaimer);

public record ShieldTermsSectionDto(string Heading, IReadOnlyList<string> Points);

public record HostCancelRequest(string? Reason);

/* ---- docs/06 AT-06-08: finding somewhere else for a guest ---------------- */

public record RehousingOptionDto(
    int ListingId,
    string Slug,
    string Title,
    string City,
    string TypeLabel,
    int MaxGuests,
    int Bedrooms,
    double Rating,
    int ReviewCount,
    string? Image,
    /// <summary>All-in price for the nights the guest still has, priced by the usual engine.</summary>
    decimal Total,
    /// <summary>How much more than the original booking's share of those nights.</summary>
    decimal Difference,
    /// <summary>True when the difference sits inside what the platform will cover.</summary>
    bool WithinTopUp,
    /// <summary>Roughly how far from the place they booked.</summary>
    double DistanceKm);

public record RehousingDto(
    int ClaimId,
    string Reference,
    string OriginalTitle,
    string City,
    DateOnly From,
    DateOnly To,
    int Nights,
    int Guests,
    /// <summary>What the guest paid for the nights being replaced.</summary>
    decimal AlreadyPaid,
    /// <summary>The ceiling on the difference the platform will cover (docs/06 K-A).</summary>
    decimal TopUpCeiling,
    IReadOnlyList<RehousingOptionDto> Options);

/* --------------------------------------------------------- public profile */

/// <summary>
/// docs/01 TK-05, docs/02 C6 — one review as it appears on somebody's profile,
/// from either side of the stay. The listing is named when there is one, so a
/// reader can see which stay a host is being praised for.
/// </summary>
public record ProfileReviewDto(
    int Id,
    string AuthorName,
    string AuthorInitials,
    int? AuthorUserId,
    string When,
    string Text,
    double Rating,
    string? ListingTitle,
    string? ListingSlug);

/// <summary>
/// docs/01 TK-05, docs/02 C6 — everything a stranger may read about somebody.
/// Deliberately holds no email, no phone and no date of birth: this endpoint is
/// open, and anything in it is public whether or not a page renders it.
/// </summary>
public record PublicProfileDto(
    int Id,
    string DisplayName,
    string Initials,
    string? AvatarUrl,
    string JoinedLabel,
    /// <summary>docs/01 TK-05 — only what was actually proved.</summary>
    IReadOnlyList<string> Badges,
    string? Bio,
    IReadOnlyList<string> Languages,
    string? Location,
    string? Occupation,
    IReadOnlyList<string> Interests,
    bool IsHost,
    bool IsSuperhost,
    string? ResponseRate,
    string? ResponseTime,
    /// <summary>Average across the listings they host; null when they host none.</summary>
    double? Rating,
    int ReviewCount,
    IReadOnlyList<ListingCardDto> Listings,
    /// <summary>Written by guests about their places (docs/02 C6 "từ hai phía").</summary>
    IReadOnlyList<ProfileReviewDto> ReviewsAsHost,
    /// <summary>Written by hosts about them as a guest.</summary>
    IReadOnlyList<ProfileReviewDto> ReviewsAsGuest)
{
    public GuestScoresDto? GuestScores { get; init; }
}

/* ------------------------------------------------ docs/01 CN-08 and CN-10 */

/// <summary>docs/01 CN-08 — titles and a description built from what the wizard already knows.</summary>
public record CopySuggestionDto(IReadOnlyList<string> Titles, string Description, int TitleMax);

public record CopySuggestionRequest(
    string? TypeKey, string? RoomTypeKey, string? City,
    int Bedrooms, int MaxGuests, IReadOnlyList<string>? AmenityKeys);

/// <summary>
/// docs/01 CN-10 — what comparable places in the same city charge, so a host
/// setting a price is not guessing alone.
/// </summary>
public record MarketPriceDto(
    string City,
    /// <summary>How many listings the figures are based on. Small samples say so.</summary>
    int SampleSize,
    decimal Low,
    decimal Median,
    decimal High,
    /// <summary>Plain-language read on where the host's own number sits.</summary>
    string? Verdict);

/* -------------------------------------------- docs/01 CN-14, QL-09, QL-18 */

public record IncomeScenarioDto(string Label, int OccupancyPercent, decimal MonthlyNet, decimal AnnualNet);

/// <summary>docs/01 CN-14 — a what-if income estimate before publishing.</summary>
public record IncomeEstimateDto(IReadOnlyList<IncomeScenarioDto> Scenarios);

public record PriceSuggestionDto(decimal SuggestedPrice, bool IsFirm, string Rationale);

public record ImprovementDto(string Area, string Suggestion, string EstimatedImpact);

/// <summary>docs/01 QL-09 + QL-18 — advice shown on a host's own listing.</summary>
public record ListingAdviceDto(
    PriceSuggestionDto Price, MarketPriceDto Market, IReadOnlyList<ImprovementDto> Improvements);

/* ------------------------------------------------------- docs/01 QL-13 */

/// <summary>
/// What a host is told before they confirm cancelling a guest's stay: the money,
/// and the things that follow which are not money.
/// </summary>
public record HostCancelPreviewDto(
    string Reference,
    string GuestName,
    DateOnly CheckIn,
    int Nights,
    decimal GuestRefund,
    decimal GoodwillCredit,
    decimal HostPayoutLost,
    /// <summary>docs/06 K1 — inside 30 days of check-in a Staylio Shield case opens automatically.</summary>
    bool OpensShieldCase,
    /// <summary>docs/03 §8 — what this does to the self-cancellation criterion.</summary>
    string CancelRateNote,
    IReadOnlyList<string> Consequences);

/* -------------------------------------------------------- docs/07 §2 and §4 */

/// <summary>docs/07 §2 — what is offered, and what is refused with a reason.</summary>
public record PaymentCatalogueDto(
    IReadOnlyList<PaymentMethodDto> Methods,
    IReadOnlyList<string> NotAccepted,
    string RefusalReason);

public record PaymentMethodDto(
    string Key, string Group, string Label, string Hint, bool Savable,
    /// <summary>
    /// docs/07 §13 — true when a licensed gateway is wired behind this method, so
    /// paying with it leaves the site. The checkout uses it for one thing only:
    /// hiding the hand-written card fields, because on a live method the number
    /// is typed on the gateway's page and never on ours (docs/07 §14.2).
    /// </summary>
    bool Live = false,
    /// <summary>
    /// docs/07 §4 — the gateway behind this method can keep a card for next time.
    /// Off unless VNPay has enabled tokens for this merchant: offering a tick box
    /// their gateway then refuses is worse than not offering it.
    /// </summary>
    bool Tokens = false,
    /// <summary>
    /// docs/07 §13 — which licensed gateway is behind this method, so the
    /// checkout can name it before sending the guest away.
    ///
    /// It has to come from the server. The client used to map the method to a
    /// gateway itself — card and napas were spelled "VNPay" in a constant — and
    /// the moment those two rows were pointed at OnePay the checkout began
    /// promising a page it was not about to open. A guest told they are going to
    /// VNPay and landing on OnePay has every reason to think something is wrong.
    /// Null when the stand-in still owns the method.
    /// </summary>
    string? Provider = null);

/// <summary>
/// docs/07 §4 — everything the platform is allowed to show about a saved card.
/// There is no field for the number because there is no number.
/// </summary>
public record SavedCardDto(
    int Id,
    string Brand,
    string BrandLabel,
    string Last4,
    string Expiry,
    string? Nickname,
    bool IsDefault,
    bool IsExpired,
    bool ExpiringSoon,
    bool HasScheduledCharge,
    bool HasOpenBooking,
    /// <summary>
    /// docs/07 §4 with §14.2 — true when a licensed gateway holds this card and
    /// Staylio holds only four digits and a token. A card typed into the
    /// stand-in cannot be charged at VNPay and the other way round, so the
    /// checkout offers one kind or the other and never mixes them.
    /// </summary>
    bool GatewayHeld = false,
    /// <summary>Which gateway holds it. Null for a card typed into the stand-in.</summary>
    string? Provider = null);

public record SaveCardRequest(
    string Number, int ExpiryMonth, int ExpiryYear, string? Nickname, bool MakeDefault = false);

public record RenameCardRequest(string? Nickname);

/* --------------------------------------------- docs/07 §7, §11 and §15 A-* */

/// <summary>docs/07 TC-A-04 — the four numbers, plus the lines behind them.</summary>
public record FinanceReportDto(
    DateOnly From,
    DateOnly To,
    decimal FeeRevenue,
    decimal HeldForOthers,
    decimal TaxPayable,
    decimal Losses,
    /// <summary>Must be zero. Anything else means the books do not balance.</summary>
    decimal LedgerDifference,
    IReadOnlyList<FinanceLineDto> Lines);

public record FinanceLineDto(string Key, string Label, decimal Amount, string Group);

/// <summary>docs/07 §7 — the daily comparison with the gateway.</summary>
public record ReconciliationDto(
    DateOnly Day,
    bool Balanced,
    int OursCount,
    int TheirsCount,
    decimal OursTotal,
    decimal TheirsTotal,
    decimal Difference,
    string Summary,
    IReadOnlyList<DiscrepancyDto> Discrepancies);

public record DiscrepancyDto(
    string Kind, string KindLabel, string Reference, decimal Ours, decimal Theirs, decimal Difference);

/// <summary>docs/07 TC-A-02 — one transaction, as the finance desk needs to see it.</summary>
public record TransactionDto(
    int BookingId,
    string BookingReference,
    string PaymentReference,
    string? GuestEmail,
    string ListingTitle,
    decimal Amount,
    decimal Refunded,
    string Method,
    string? CardLast4,
    string PaymentStatus,
    string BookingStatus,
    string BookingStatusLabel,
    string PayoutStatus,
    string? PayoutHoldReason,
    string? PayoutReference,
    DateTime CreatedAt,
    /// <summary>
    /// docs/07 §6 — what the guest was reading prices in when they booked, at
    /// the platform's own rate of that instant. Evidence for "the price I was
    /// shown", never an input: refunds go back at the original amount in the
    /// original currency, and recomputing one through this rate would be a
    /// money bug that balances to zero.
    /// </summary>
    string? DisplayCurrency = null,
    decimal? DisplayRate = null);

public record ManualRefundRequest(decimal Amount, string? Reason);

public record AdjustPayoutRequest(bool Release, string? Reason);

/// <summary>docs/07 §11 — a guest has gone to their bank about a charge.</summary>
public record ChargebackDto(
    int Id,
    string BookingReference,
    string ListingTitle,
    decimal Amount,
    string Reason,
    string Status,
    string StatusLabel,
    DateTime ReceivedAt,
    DateTime EvidenceDueBy,
    bool EvidenceOverdue,
    string? Evidence,
    bool HostAtFault,
    IReadOnlyList<string> Checklist);

public record OpenChargebackRequest(string BookingReference, decimal Amount, string? Reason);

public record ChargebackEvidenceRequest(string? Evidence);

public record DecideChargebackRequest(bool Won, bool HostAtFault);

/* ------------------------------------------------------------- docs/08 */

public record AdminUserRowDto(
    int Id, string FullName, string Email, string? Phone, string Role,
    string StatusLabel, bool IdentityVerified, DateTime JoinedAt);

public record AdminListingRowDto(int Id, string Title, string City, bool Published, double Rating, int ReviewCount);

public record AdminSessionRowDto(string Device, DateTime At, bool Active, string? Ip = null);

/// <summary>docs/08 §5 — one entry on somebody's record.</summary>
public record SanctionRowDto(
    int Id, string Level, string LevelLabel, string? RestrictionLabel,
    string Policy, string Reason, string? LiftedWhen,
    string DecidedBy, DateTime CreatedAt, DateTime? ExpiresAt,
    DateTime? LiftedAt, string? LiftedReason, bool OverturnedOnAppeal, bool Severe);

/// <summary>docs/08 §4 — everything the console may show about one person.</summary>
public record AdminUserDto(
    int Id,
    string FullName,
    string? DisplayName,
    string Email,
    string? Phone,
    string Role,
    string StatusLabel,
    bool IsLocked,
    DateTime? SuspendedUntil,
    bool EmailConfirmed,
    bool PhoneConfirmed,
    bool IdentityVerified,
    DateTime JoinedAt,
    DateTime? LastSeenAt,
    bool IsHost,
    bool IsSuperhost,
    IReadOnlyList<AdminListingRowDto> Listings,
    int Bookings,
    int Cancellations,
    double CancellationRate,
    int ReviewsWritten,
    int ReportsAgainst,
    IReadOnlyList<SanctionRowDto> Sanctions,
    decimal Balance,
    /// <summary>Null unless the reader holds the Finance role (docs/08 §2).</summary>
    string? PayoutAccountLast4,
    string? PayoutBankName,
    IReadOnlyList<string> Cards,
    IReadOnlyList<AdminSessionRowDto> Sessions,
    IReadOnlyList<AdminUserRowDto> RelatedAccounts,
    /// <summary>Which actions this particular admin may take, so the console offers only those.</summary>
    IReadOnlyList<string> Allowed,
    /* ---- the rest of the §4 profile ---- */
    bool IsGuestFavoriteHost,
    /// <summary>Hosts this person co-hosts for, with their scopes.</summary>
    IReadOnlyList<string> CoHostOf,
    int ReviewsReceived,
    int OpenDisputes,
    int TotalDisputes,
    int GiftCards,
    decimal GiftCardRemaining,
    IReadOnlyList<AdminBookingRowDto> RecentBookings);

/// <summary>One line of the two-sided booking history docs/08 §4 asks for.</summary>
public record AdminBookingRowDto(
    int Id, string Reference, string Side, string Listing, string Status, string StatusLabel,
    DateOnly CheckIn, DateOnly CheckOut, decimal Total);

/// <summary>
/// docs/08 §2 — the messages of ONE booking, never an inbox. Each view needs a
/// reason and leaves its own audit line.
/// </summary>
public record AdminThreadDto(
    int BookingId, string Reference, string ListingTitle,
    string GuestName, string HostName,
    IReadOnlyList<AdminThreadMessageDto> Messages);

public record AdminThreadMessageDto(
    string Sender, string Body, DateTime SentAt, bool IsSystem);

/// <summary>docs/08 §2 — an admin correcting a field the person cannot fix themselves.</summary>
public record AdminEditProfileRequest(
    string? Reason,
    string? FullName, string? DisplayName, string? Phone,
    string? Location, string? Occupation);

/// <summary>docs/08 §6 and QT-U-07 — the cost of a lock, before it happens.</summary>
public record LockPreviewDto(
    IReadOnlyList<LockLineDto> Lines,
    int GuestsStaying,
    int BookingsCancelled,
    decimal MoneyRefunded,
    decimal PayoutHeld,
    string Warning,
    string? OpenDisputeNotice,
    string SafetyNotice,
    bool IsHost,
    /// <summary>
    /// docs/08 §6 row "Số dư khuyến mãi: đóng băng, không xoá". The freeze is
    /// enforced in <c>WalletController</c>, but the admin about to press the
    /// button could not see it here, so the one question the suspended person
    /// asks first had no answer on the screen that caused it.
    /// </summary>
    string BalanceNotice = "");

public record LockLineDto(
    int BookingId, string Reference, string Action, decimal Money, string Counterparty, string Note);

public record SanctionRequest(
    string Level,
    string? Restriction,
    string? Policy,
    string? Reason,
    string? LiftedWhen,
    int? Days,
    /// <summary>docs/08 §5.6 and §5.4 — which listed ground, when jumping or banning.</summary>
    string? SevereGround,
    /// <summary>docs/08 §6 — the one choice the guest table leaves to the admin.</summary>
    bool RefundInFull = false);

public record RestoreRequest(string? Reason);

/// <summary>docs/08 §8 — a sanction as its subject sees it, with the appeal door.</summary>
public record MySanctionDto(
    int Id, string LevelLabel, string? RestrictionLabel,
    string Policy, string Reason, string? LiftedWhen,
    DateTime CreatedAt, DateTime? ExpiresAt, DateTime? LiftedAt,
    bool OverturnedOnAppeal,
    bool MayAppeal, string? WhyNotAppeal,
    string? AppealStatusLabel, string? AppealOutcome, DateTime? AppealDueBy);

public record FileAppealRequest(string? Argument);

/// <summary>docs/08 §9 — a data request as the person who asked sees it.</summary>
public record MyDataRequestDto(
    int Id, string Kind, string KindLabel, string Status, string StatusLabel,
    DateTime CreatedAt, DateTime DueBy, DateTime? CompletedAt, string? Note,
    string? DownloadUrl, DateTime? LinkExpiresAt);

public record DataRequestRequest(string Kind);

public record AppealByTokenRequest(string? Token, string? Argument);

/// <summary>docs/08 §7 — a live session inside somebody else's account.</summary>
public record ImpersonationDto(
    int Id, int TargetUserId, string TargetName, string AdminName,
    int TicketId, string Reason,
    DateTime ExpiresAt, int SecondsLeft,
    string Banner,
    IReadOnlyList<string> Forbidden,
    bool TargetNotified);

public record ImpersonateRequest(
    int UserId, int TicketId, string? Reason,
    /// <summary>docs/08 §7.4 — the only case where the person is not told. Super only.</summary>
    bool SilentFraudInvestigation = false,
    /// <summary>
    /// docs/08 §7.4 — "có phê duyệt riêng": the OTHER Super who signed off on
    /// staying quiet. Required with the flag; the same person cannot approve
    /// their own silence.
    /// </summary>
    int? SilenceApprovedByUserId = null);

/// <summary>docs/08 §8 — somebody says a decision about them was wrong.</summary>
public record AppealDto(
    int Id, int UserId, string UserName,
    int SanctionId, string SanctionLevel, string SanctionReason,
    string Argument,
    string Status, string StatusLabel,
    DateTime CreatedAt, DateTime DueBy, bool Overdue,
    string? ReviewedBy, DateTime? ReviewedAt, string? Outcome,
    /// <summary>False when the reader is the one who made the original call.</summary>
    bool MayReview);

public record DecideAppealRequest(string Result, string? ReducedTo, string? Outcome);

/// <summary>docs/08 §9 — a request to be exported or erased.</summary>
public record DataRequestDto(
    int Id, int UserId, string UserName, string Email,
    string Kind, string KindLabel,
    string Status, string StatusLabel,
    DateTime CreatedAt, DateTime DueBy, bool Overdue,
    string? Note,
    IReadOnlyList<string> Blockers,
    bool MayErase);

/* ---- docs/08 §10, watching the watchers ---- */

public record ScorecardDto(
    int AdminUserId, string Name,
    int ProfilesViewed, int Decisions, int AppealsAgainst, int AppealsUpheld,
    double OverturnRatePercent, bool LooksUnreliable,
    string Scopes, DateTime? LastActiveAt, bool ScopeLooksUnused, bool AccessReviewDue,
    bool TwoFactorEnabled);

public record OversightFlagDto(string AdminName, string Flag, string Label, string Detail, DateTime At);

public record MoneyApprovalDto(
    int Id, string Action, string Target, decimal Amount, string Reason,
    string RequestedBy, int RequestedByUserId, DateTime RequestedAt,
    bool MayApprove);

public record SampledDecisionDto(
    int Id, string UserName, string Level, string Reason, string DecidedBy, DateTime At);

/// <summary>docs/08 §5.6 — a severe jump waiting for a Super to look at it within 24h.</summary>
public record SevereReviewDto(
    int SanctionId, string UserName, string Level, string Ground, string Reason,
    string DecidedBy, DateTime DecidedAt, DateTime DueBy, bool Overdue);

public record OversightDto(
    IReadOnlyList<ScorecardDto> Admins,
    IReadOnlyList<OversightFlagDto> Flags,
    IReadOnlyList<MoneyApprovalDto> PendingApprovals,
    IReadOnlyList<SampledDecisionDto> RandomSample,
    decimal TwoPersonThreshold,
    int RandomReviewPercent,
    IReadOnlyList<SevereReviewDto> SevereQueue);

public record DecideApprovalRequest(bool Approve, string? Reason);

/// <summary>
/// docs/08 §4 and QT-U-11 — an identity document, shown under a watermark that
/// names whoever asked to see it.
/// </summary>
public record IdentityViewDto(
    string DocumentLabel,
    string? DocumentLast4,
    string FrontImageUrl,
    string? BackImageUrl,
    string SelfieImageUrl,
    /// <summary>Drawn across the images: the admin's name and the moment they looked.</summary>
    string Watermark,
    string Status,
    DateTime SubmittedAt);

/// <summary>
/// docs/08 §3 — granting or withdrawing admin roles. An empty list withdraws
/// everything, which is the leaver case: the account stays so the log still has
/// a name attached to it.
/// </summary>
public record GrantAdminRequest(int UserId, IReadOnlyList<string>? Scopes, string? Reason);

/// <summary>docs/08 QT-U-13 — two accounts, one person.</summary>
public record MergeAccountsRequest(int FromUserId, int IntoUserId, string? Reason);

public record MergeResultDto(int FromUserId, int IntoUserId, IReadOnlyList<string> Moved);
