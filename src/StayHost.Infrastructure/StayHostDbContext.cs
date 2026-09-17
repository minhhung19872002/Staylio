using Microsoft.EntityFrameworkCore;
using StayHost.Domain;

namespace StayHost.Infrastructure;

public class StayHostDbContext(DbContextOptions<StayHostDbContext> options) : DbContext(options)
{
    public DbSet<HostProfile> Hosts => Set<HostProfile>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingImage> ListingImages => Set<ListingImage>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<ListingAmenity> ListingAmenities => Set<ListingAmenity>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<WishlistVote> WishlistVotes => Set<WishlistVote>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<FriendMessage> FriendMessages => Set<FriendMessage>();
    public DbSet<TripPlan> TripPlans => Set<TripPlan>();
    public DbSet<TripPlanBooking> TripPlanBookings => Set<TripPlanBooking>();
    public DbSet<TripPlanMember> TripPlanMembers => Set<TripPlanMember>();
    public DbSet<TripItineraryItem> TripItineraryItems => Set<TripItineraryItem>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<MessageThread> MessageThreads => Set<MessageThread>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<TranslationCache> TranslationCaches => Set<TranslationCache>();
    public DbSet<NeighborReport> NeighborReports => Set<NeighborReport>();
    public DbSet<CalendarBlock> CalendarBlocks => Set<CalendarBlock>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AbuseReport> AbuseReports => Set<AbuseReport>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();
    public DbSet<GuidebookPlace> GuidebookPlaces => Set<GuidebookPlace>();
    public DbSet<GuestReview> GuestReviews => Set<GuestReview>();
    public DbSet<ReviewHelpfulVote> ReviewHelpfulVotes => Set<ReviewHelpfulVote>();
    public DbSet<ListingQuestion> ListingQuestions => Set<ListingQuestion>();
    public DbSet<TaxRule> TaxRules => Set<TaxRule>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<BookingEvent> BookingEvents => Set<BookingEvent>();
    public DbSet<ResolutionCase> ResolutionCases => Set<ResolutionCase>();
    public DbSet<ResolutionEvent> ResolutionEvents => Set<ResolutionEvent>();
    public DbSet<AdminAuditEntry> AdminAudit => Set<AdminAuditEntry>();
    public DbSet<QuickReply> QuickReplies => Set<QuickReply>();
    public DbSet<CoHost> CoHosts => Set<CoHost>();
    public DbSet<CoHostPayout> CoHostPayouts => Set<CoHostPayout>();
    public DbSet<CalendarFeed> CalendarFeeds => Set<CalendarFeed>();
    public DbSet<HelpArticle> HelpArticles => Set<HelpArticle>();
    public DbSet<RiskFlag> RiskFlags => Set<RiskFlag>();
    public DbSet<BillSplit> BillSplits => Set<BillSplit>();
    public DbSet<BillShare> BillShares => Set<BillShare>();
    public DbSet<Experience> Experiences => Set<Experience>();
    public DbSet<ExperienceImage> ExperienceImages => Set<ExperienceImage>();
    public DbSet<ExperienceStep> ExperienceSteps => Set<ExperienceStep>();
    public DbSet<ExperienceSlot> ExperienceSlots => Set<ExperienceSlot>();
    public DbSet<ExperienceBooking> ExperienceBookings => Set<ExperienceBooking>();
    public DbSet<ExperienceReview> ExperienceReviews => Set<ExperienceReview>();
    public DbSet<ExperienceHold> ExperienceHolds => Set<ExperienceHold>();
    public DbSet<ServiceOffering> ServiceOfferings => Set<ServiceOffering>();
    public DbSet<ServiceImage> ServiceImages => Set<ServiceImage>();
    public DbSet<ServiceBooking> ServiceBookings => Set<ServiceBooking>();
    public DbSet<ServiceAddOn> ServiceAddOns => Set<ServiceAddOn>();
    public DbSet<ServiceBookingAddOn> ServiceBookingAddOns => Set<ServiceBookingAddOn>();
    public DbSet<ServiceReview> ServiceReviews => Set<ServiceReview>();
    public DbSet<RoomTypeOption> RoomTypes => Set<RoomTypeOption>();
    public DbSet<PriceMatchClaim> PriceMatchClaims => Set<PriceMatchClaim>();
    public DbSet<CreditEntry> CreditEntries => Set<CreditEntry>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();
    public DbSet<SpecialOffer> SpecialOffers => Set<SpecialOffer>();
    public DbSet<ListingCancellationNote> ListingCancellationNotes => Set<ListingCancellationNote>();
    public DbSet<BookingChangeRequest> BookingChangeRequests => Set<BookingChangeRequest>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<PayoutInstallment> PayoutInstallments => Set<PayoutInstallment>();
    public DbSet<GiftCard> GiftCards => Set<GiftCard>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<OneTimeCode> OneTimeCodes => Set<OneTimeCode>();
    public DbSet<IdentityCheck> IdentityChecks => Set<IdentityCheck>();
    public DbSet<ListingView> ListingViews => Set<ListingView>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<BankCredit> BankCredits => Set<BankCredit>();
    public DbSet<SavedCard> SavedCards => Set<SavedCard>();
    public DbSet<Sanction> Sanctions => Set<Sanction>();
    public DbSet<Appeal> Appeals => Set<Appeal>();
    public DbSet<ImpersonationSession> ImpersonationSessions => Set<ImpersonationSession>();
    public DbSet<DataRequest> DataRequests => Set<DataRequest>();
    public DbSet<AdminProfileView> AdminProfileViews => Set<AdminProfileView>();
    public DbSet<MoneyApproval> MoneyApprovals => Set<MoneyApproval>();
    public DbSet<GatewayCharge> GatewayCharges => Set<GatewayCharge>();
    public DbSet<CardAuthentication> CardAuthentications => Set<CardAuthentication>();
    public DbSet<PaymentSession> PaymentSessions => Set<PaymentSession>();
    public DbSet<PayoutBatch> PayoutBatches => Set<PayoutBatch>();
    public DbSet<Chargeback> Chargebacks => Set<Chargeback>();
    public DbSet<ShieldClaim> ShieldClaims => Set<ShieldClaim>();
    public DbSet<ShieldEvidence> ShieldEvidence => Set<ShieldEvidence>();
    public DbSet<ShieldItem> ShieldItems => Set<ShieldItem>();
    public DbSet<ShieldEvent> ShieldEvents => Set<ShieldEvent>();
    public DbSet<ShieldFundMovement> ShieldFundMovements => Set<ShieldFundMovement>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<HostProfile>(e =>
        {
            e.ToTable("hosts");
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Initials).HasMaxLength(4);
            e.Property(x => x.PayoutBankName).HasMaxLength(120);
            e.Property(x => x.PayoutAccountName).HasMaxLength(120);
            e.Property(x => x.PayoutAccountLast4).HasMaxLength(4);
            // docs/07 §14.3 — AES-GCM, nonce and tag packed in, base64. Never a
            // number anyone can read out of a database dump.
            e.Property(x => x.PayoutAccountSealed).HasMaxLength(200);
            e.Property(x => x.OwedToPlatform).HasPrecision(12, 2);
            e.HasOne(x => x.User).WithOne(u => u.HostProfile)
                .HasForeignKey<HostProfile>(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<User>(e =>
        {
            e.ToTable("users");
            // docs/01 TK-01 lets somebody sign up with a phone and no email, so
            // the uniqueness has to skip the blanks rather than collide on them.
            e.HasIndex(x => x.Email).IsUnique().HasFilter("\"Email\" <> ''");
            e.HasIndex(x => x.Phone).IsUnique().HasFilter("\"Phone\" IS NOT NULL AND \"Phone\" <> ''");
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Initials).HasMaxLength(4);
            e.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordSalt).HasMaxLength(100).IsRequired();
            // docs/01 TK-04 — the lengths are the ones Profiles trims to, so a
            // value that got past the service layer still cannot get past here.
            e.Property(x => x.DisplayName).HasMaxLength(Profiles.LineMax);
            e.Property(x => x.Location).HasMaxLength(Profiles.LineMax);
            e.Property(x => x.Occupation).HasMaxLength(Profiles.LineMax);
            e.Property(x => x.Bio).HasMaxLength(Profiles.BioMax);
            e.Property(x => x.SpokenLanguages).HasMaxLength(Profiles.MaxLanguages * (Profiles.TagMax + 1));
            e.Property(x => x.Interests).HasMaxLength(Profiles.MaxInterests * (Profiles.TagMax + 1));
            // docs/01 TK-09 — display preferences. Nullable on purpose: null is
            // "never chosen", and every account from before these columns keeps
            // behaving exactly as it did.
            e.Property(x => x.Language).HasMaxLength(8);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            // docs/01 TK-07 and TK-13.
            e.Property(x => x.WorkEmail).HasMaxLength(200);
            e.Property(x => x.EmergencyContactName).HasMaxLength(Profiles.LineMax);
            e.Property(x => x.EmergencyContactPhone).HasMaxLength(Profiles.LineMax);
            e.Property(x => x.EmergencyContactRelation).HasMaxLength(Profiles.LineMax);
            e.Ignore(x => x.HostProfileId);
        });

        // docs/01 TK-06 — one row per attempt at proving who somebody is.
        b.Entity<IdentityCheck>(e =>
        {
            e.ToTable("identity_checks");
            e.HasIndex(x => new { x.UserId, x.SubmittedAt });
            e.HasIndex(x => x.Status);
            e.Property(x => x.DocumentLast4).HasMaxLength(4);
            e.Property(x => x.FrontImageUrl).HasMaxLength(300).IsRequired();
            e.Property(x => x.BackImageUrl).HasMaxLength(300);
            e.Property(x => x.SelfieImageUrl).HasMaxLength(300).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DecidedByUser).WithMany().HasForeignKey(x => x.DecidedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AuthSession>(e =>
        {
            e.ToTable("auth_sessions");
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.Token).HasMaxLength(88).IsRequired();
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.HasOne(x => x.User).WithMany(u => u.Sessions)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.IsActive);
        });

        b.Entity<UserToken>(e =>
        {
            e.ToTable("user_tokens");
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.Token).HasMaxLength(88).IsRequired();
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // docs/07 §7 — the unique key is the whole point: it is what stops the
        // same charge being made twice when a request is retried.
        b.Entity<PaymentAttempt>(e =>
        {
            e.ToTable("payment_attempts");
            e.HasIndex(x => x.Key).IsUnique();
            e.HasIndex(x => new { x.BookingId, x.CreatedAt });
            e.Property(x => x.Key).HasMaxLength(160).IsRequired();
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Method).HasMaxLength(30);
            e.Property(x => x.CardLast4).HasMaxLength(4);
            e.Property(x => x.Message).HasMaxLength(300);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        // docs/07 §2.3 — the bank's side. BankReference is unique for the same
        // reason payment_attempts.Key is: re-importing a statement must not
        // confirm the same booking a second time.
        b.Entity<BankCredit>(e =>
        {
            e.ToTable("bank_credits");
            e.HasIndex(x => x.BankReference).IsUnique();
            e.HasIndex(x => new { x.ResolvedAt, x.ImportedAt });
            e.Property(x => x.BankReference).HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.MatchedReference).HasMaxLength(20);
            e.Property(x => x.ResolutionNote).HasMaxLength(400);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Expected).HasPrecision(12, 2);
            e.Ignore(x => x.NeedsSomebody);
        });

        // docs/07 §7 — the gateway's side of the daily reconciliation. Written
        // by PaymentGateway only, so the two records stay independent.
        b.Entity<GatewayCharge>(e =>
        {
            e.ToTable("gateway_charges");
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => x.ChargedAt);
            e.Property(x => x.Reference).HasMaxLength(160).IsRequired();
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Method).HasMaxLength(30);
        });

        // docs/07 §13 — one trip out to a licensed gateway. OrderRef is unique
        // for the same reason payment_attempts.Key is: a gateway that sends its
        // IPN twice, or sends it while the guest is coming back, must settle the
        // same booking once.
        b.Entity<PaymentSession>(e =>
        {
            e.ToTable("payment_sessions");
            e.HasIndex(x => x.OrderRef).IsUnique();
            e.HasIndex(x => x.AttemptKey);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.OrderRef).HasMaxLength(40).IsRequired();
            e.Property(x => x.AttemptKey).HasMaxLength(160).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(20).IsRequired();
            e.Property(x => x.Method).HasMaxLength(30).IsRequired();
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.PayUrl).HasMaxLength(2000);
            e.Property(x => x.ProviderTxnId).HasMaxLength(80);
            e.Property(x => x.ResponseCode).HasMaxLength(40);
            e.Property(x => x.SettledBy).HasMaxLength(20);
            e.Property(x => x.ProviderPaidAt).HasMaxLength(20);
            e.Property(x => x.RefundedAmount).HasPrecision(12, 2);
            e.Property(x => x.RefundTxnId).HasMaxLength(80);
            e.Property(x => x.RefundCode).HasMaxLength(40);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            // docs/01 TC-08 — a gift card is bought by somebody who is not
            // travelling, so its visit to the gateway hangs on the card instead.
            // Restrict, not Cascade: a paid session is the evidence that money
            // arrived, and deleting a card must never take that record with it.
            e.HasOne(x => x.GiftCard).WithMany()
                .HasForeignKey(x => x.GiftCardId).OnDelete(DeleteBehavior.Restrict);
            // docs/09 — the other two product lines pay through the same gateways.
            // Restrict for the same reason as the gift card: a paid session is proof.
            e.HasOne(x => x.ExperienceBooking).WithMany()
                .HasForeignKey(x => x.ExperienceBookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ServiceBooking).WithMany()
                .HasForeignKey(x => x.ServiceBookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BillShare).WithMany()
                .HasForeignKey(x => x.BillShareId).OnDelete(DeleteBehavior.Restrict);
        });

        // docs/07 §13 — one bank transfer out to a host. Reference is unique for
        // the same reason every other reference here is: an operator who confirms
        // the same file twice must not pay anybody twice.
        b.Entity<PayoutBatch>(e =>
        {
            e.ToTable("payout_batches");
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.Status, x.DueOn });
            e.Property(x => x.Reference).HasMaxLength(40).IsRequired();
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Deducted).HasPrecision(12, 2);
            e.Property(x => x.BankName).HasMaxLength(120);
            e.Property(x => x.AccountName).HasMaxLength(160);
            // The number itself. It is here rather than read back from the host
            // because the transfer must carry the details it was decided with,
            // not the ones the host has today (docs/07 §12.2).
            e.Property(x => x.AccountNumber).HasMaxLength(40);
            e.Property(x => x.SettledBy).HasMaxLength(60);
            e.Property(x => x.Note).HasMaxLength(400);
            e.HasOne(x => x.Host).WithMany()
                .HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Cascade);
        });

        /* ------------------------------------------------------- docs/08 */

        // docs/08 §5 — what was done to an account, and why. Never edited after
        // the fact: an entry that turned out to be wrong is lifted or overturned,
        // both of which are new information rather than a rewrite.
        b.Entity<Sanction>(e =>
        {
            e.ToTable("sanctions");
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.Property(x => x.Policy).HasMaxLength(200);
            e.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            e.Property(x => x.LiftedWhen).HasMaxLength(500);
            e.Property(x => x.LiftedReason).HasMaxLength(1000);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DecidedByUser).WithMany()
                .HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // docs/08 §8 — one appeal per decision, read by somebody else.
        b.Entity<Appeal>(e =>
        {
            e.ToTable("appeals");
            e.HasIndex(x => x.SanctionId).IsUnique();
            e.Property(x => x.Argument).HasMaxLength(4000).IsRequired();
            e.Property(x => x.Outcome).HasMaxLength(4000);
            e.HasOne(x => x.Sanction).WithMany().HasForeignKey(x => x.SanctionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ReviewedByUser).WithMany()
                .HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // docs/08 §7 — time spent inside somebody else's account.
        b.Entity<ImpersonationSession>(e =>
        {
            e.ToTable("impersonation_sessions");
            e.HasIndex(x => new { x.AdminUserId, x.StartedAt });
            e.HasIndex(x => x.TargetUserId);
            e.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            e.HasOne(x => x.AdminUser).WithMany()
                .HasForeignKey(x => x.AdminUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TargetUser).WithMany()
                .HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Cascade);
        });

        // docs/08 §9 — asking for your data out, or gone.
        b.Entity<DataRequest>(e =>
        {
            e.ToTable("data_requests");
            e.HasIndex(x => new { x.UserId, x.Status });
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.HandledByUser).WithMany()
                .HasForeignKey(x => x.HandledByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // docs/08 §10 — a log of decisions alone would miss the admin who only
        // ever looks, which is exactly the pattern §3 wants caught.
        b.Entity<AdminProfileView>(e =>
        {
            e.ToTable("admin_profile_views");
            e.HasIndex(x => new { x.AdminUserId, x.CreatedAt });
            e.HasIndex(x => new { x.TargetUserId, x.CreatedAt });
            e.HasOne(x => x.AdminUser).WithMany()
                .HasForeignKey(x => x.AdminUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TargetUser).WithMany()
                .HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Cascade);
        });

        // docs/08 §10 — the request and the approval are two acts by two people.
        b.Entity<MoneyApproval>(e =>
        {
            e.ToTable("money_approvals");
            e.HasIndex(x => new { x.RequestedByUserId, x.RequestedAt });
            e.Property(x => x.Action).HasMaxLength(60).IsRequired();
            e.Property(x => x.Target).HasMaxLength(120).IsRequired();
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            e.Property(x => x.RejectedReason).HasMaxLength(1000);
            e.HasOne(x => x.RequestedByUser).WithMany()
                .HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ApprovedByUser).WithMany()
                .HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // docs/07 §4 — a card the guest kept. The number is not here.
        b.Entity<SavedCard>(e =>
        {
            e.ToTable("saved_cards");
            e.HasIndex(x => new { x.UserId, x.IsDefault });
            e.Property(x => x.Last4).HasMaxLength(4).IsRequired();
            e.Property(x => x.Nickname).HasMaxLength(60);
            // docs/07 §4 with §14.2 — a card kept at a gateway is a token of
            // theirs, sealed. AES-GCM output plus base64 on a 64-character token.
            e.Property(x => x.GatewayTokenSealed).HasMaxLength(300);
            e.Property(x => x.Provider).HasMaxLength(20);
            e.Ignore(x => x.IsGatewayHeld);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // docs/07 §5 — one trip to the bank's OTP page, kept so a guest who
        // closed the tab can be picked up where they were.
        b.Entity<CardAuthentication>(e =>
        {
            e.ToTable("card_authentications");
            e.HasIndex(x => x.AttemptKey);
            e.HasIndex(x => new { x.BookingId, x.StartedAt });
            e.Property(x => x.AttemptKey).HasMaxLength(160).IsRequired();
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Method).HasMaxLength(30);
            e.Property(x => x.CardLast4).HasMaxLength(4);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        // docs/07 §11 — the bank has taken money back while it decides.
        b.Entity<Chargeback>(e =>
        {
            e.ToTable("chargebacks");
            e.HasIndex(x => x.BookingId);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Reason).HasMaxLength(300);
            e.Property(x => x.Evidence).HasMaxLength(2000);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.HasIndex(x => x.Reference).IsUnique();
            e.Property(x => x.Reference).HasMaxLength(24).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Method).HasMaxLength(20);
            e.Property(x => x.CardLast4).HasMaxLength(4);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.PlatformFee).HasPrecision(12, 2);
            e.Property(x => x.HostPayout).HasPrecision(12, 2);
            e.Property(x => x.PayoutDeducted).HasPrecision(12, 2);
            e.Property(x => x.CoHostShare).HasPrecision(12, 2);
            e.Property(x => x.PayoutReference).HasMaxLength(40);
            e.HasOne(x => x.Booking).WithOne(bk => bk.Payment)
                .HasForeignKey<Payment>(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MessageThread>(e =>
        {
            e.ToTable("message_threads");
            e.HasIndex(x => new { x.ListingId, x.GuestUserId }).IsUnique();
            e.HasOne(x => x.Listing).WithMany().HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.GuestUser).WithMany().HasForeignKey(x => x.GuestUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.HostUser).WithMany().HasForeignKey(x => x.HostUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
        });

        // docs/01 TM-23 — saved searches.
        b.Entity<SavedSearch>(e =>
        {
            e.ToTable("saved_searches");
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Label).HasMaxLength(120).IsRequired();
            e.Property(x => x.Q).HasMaxLength(200);
            e.Property(x => x.Category).HasMaxLength(40);
            e.Property(x => x.RoomType).HasMaxLength(20);
            e.Property(x => x.AmenitiesCsv).HasMaxLength(400);
            e.Property(x => x.HostLanguagesCsv).HasMaxLength(200);
            e.Property(x => x.MinPrice).HasPrecision(12, 2);
            e.Property(x => x.MaxPrice).HasPrecision(12, 2);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // docs/01 AT-03 — neighbour reports, filed without an account.
        b.Entity<NeighborReport>(e =>
        {
            e.ToTable("neighbor_reports");
            e.HasIndex(x => x.Status);
            e.Property(x => x.Location).HasMaxLength(300).IsRequired();
            e.Property(x => x.Detail).HasMaxLength(StayHost.Domain.NeighborReports.DetailMax).IsRequired();
            e.Property(x => x.Contact).HasMaxLength(200);
            e.Property(x => x.Resolution).HasMaxLength(1000);
        });

        // docs/01 TĐ-03 — cached machine translations, one per (source, target).
        b.Entity<TranslationCache>(e =>
        {
            e.ToTable("translation_caches");
            e.HasIndex(x => new { x.SourceHash, x.TargetLang }).IsUnique();
            e.Property(x => x.SourceHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.TargetLang).HasMaxLength(8).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(20);
        });

        // docs/01 QT-08 — feature flags with percentage rollout.
        b.Entity<FeatureFlag>(e =>
        {
            e.ToTable("feature_flags");
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(80).IsRequired();
            e.Property(x => x.Description).HasMaxLength(300);
        });

        // docs/01 AT-10 — the block list. One row per (blocker, blocked) pair.
        b.Entity<UserBlock>(e =>
        {
            e.ToTable("user_blocks");
            e.HasIndex(x => new { x.BlockerUserId, x.BlockedUserId }).IsUnique();
            e.HasIndex(x => x.BlockedUserId);
            e.HasOne(x => x.Blocker).WithMany()
                .HasForeignKey(x => x.BlockerUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Blocked).WithMany()
                .HasForeignKey(x => x.BlockedUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Message>(e =>
        {
            e.ToTable("messages");
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.HasOne(x => x.Thread).WithMany(t => t.Messages)
                .HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Attachments).HasMaxLength(2000);
            e.HasOne(x => x.SenderUser).WithMany()
                .HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<QuickReply>(e =>
        {
            e.ToTable("quick_replies");
            e.HasIndex(x => new { x.HostUserId, x.SortOrder });
            e.Property(x => x.Title).HasMaxLength(80).IsRequired();
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            e.HasOne(x => x.HostUser).WithMany()
                .HasForeignKey(x => x.HostUserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CoHost>(e =>
        {
            e.ToTable("co_hosts");
            e.HasIndex(x => new { x.OwnerUserId, x.Email });
            e.HasIndex(x => x.InviteToken).IsUnique();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.InviteToken).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.OwnerUser).WithMany()
                .HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CoHostUser).WithMany()
                .HasForeignKey(x => x.CoHostUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Listing).WithMany()
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);

            // docs/02 G8 — the optional cut of the owner's earnings.
            e.Property(x => x.PayoutPercent).HasPrecision(5, 2);
            e.Property(x => x.PayoutFixed).HasPrecision(12, 2);
            e.HasOne(x => x.PayeeHost).WithMany()
                .HasForeignKey(x => x.PayeeHostId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CoHostPayout>(e =>
        {
            e.ToTable("co_host_payouts");
            e.HasIndex(x => new { x.PayeeHostId, x.Status });
            e.HasIndex(x => x.PayoutReference);
            // One share per co-host per booking: the sweep runs every minute and
            // a booking that came back round must not pay the same person twice.
            e.HasIndex(x => new { x.CoHostId, x.BookingId }).IsUnique();
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Earnings).HasPrecision(12, 2);
            e.Property(x => x.ClawedBack).HasPrecision(12, 2);
            e.Property(x => x.Deducted).HasPrecision(12, 2);
            e.Property(x => x.Percent).HasPrecision(5, 2);
            e.Property(x => x.Fixed).HasPrecision(12, 2);
            e.Property(x => x.Percent).HasPrecision(5, 2);
            e.Property(x => x.Fixed).HasPrecision(12, 2);
            e.Property(x => x.Basis).HasMaxLength(120).IsRequired();
            e.Property(x => x.PayoutReference).HasMaxLength(64);
            e.HasOne(x => x.CoHost).WithMany()
                .HasForeignKey(x => x.CoHostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PayeeHost).WithMany()
                .HasForeignKey(x => x.PayeeHostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CalendarFeed>(e =>
        {
            e.ToTable("calendar_feeds");
            e.HasIndex(x => x.ListingId);
            e.Property(x => x.Label).HasMaxLength(80).IsRequired();
            e.Property(x => x.Url).HasMaxLength(600).IsRequired();
            e.Property(x => x.LastError).HasMaxLength(400);
            e.HasOne(x => x.Listing).WithMany(l => l.CalendarFeeds)
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Experience>(e =>
        {
            e.ToTable("experiences");
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(140).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.City).HasMaxLength(120).IsRequired();
            e.Property(x => x.Country).HasMaxLength(120).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(400);
            e.Property(x => x.MeetingPoint).HasMaxLength(300);
            e.Property(x => x.Languages).HasMaxLength(120);
            e.Property(x => x.SearchText).HasMaxLength(4000);
            e.Property(x => x.PricePerPerson).HasColumnType("numeric(14,2)");
            e.Property(x => x.PrivateGroupPrice).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.Host).WithMany()
                .HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExperienceImage>(e =>
        {
            e.ToTable("experience_images");
            e.Property(x => x.Url).HasMaxLength(600).IsRequired();
            e.Property(x => x.Caption).HasMaxLength(200);
            e.HasOne(x => x.Experience).WithMany(x => x.Images)
                .HasForeignKey(x => x.ExperienceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExperienceStep>(e =>
        {
            e.ToTable("experience_steps");
            // Read in order every time the page is drawn, so the order is an index
            // rather than a sort the query has to do on its own.
            e.HasIndex(x => new { x.ExperienceId, x.SortOrder });
            e.Property(x => x.Title).HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasMaxLength(400).IsRequired();
            e.Property(x => x.ImageUrl).HasMaxLength(600);
            e.HasOne(x => x.Experience).WithMany(x => x.Itinerary)
                .HasForeignKey(x => x.ExperienceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExperienceSlot>(e =>
        {
            e.ToTable("experience_slots");
            e.HasIndex(x => new { x.ExperienceId, x.StartsAt });
            e.Property(x => x.CancelReason).HasMaxLength(300);
            e.HasOne(x => x.Experience).WithMany(x => x.Slots)
                .HasForeignKey(x => x.ExperienceId).OnDelete(DeleteBehavior.Cascade);
            // docs/09 §2.7 (scenario 2) — the seat count is claimed with a single
            // conditional UPDATE in ExperienceService, so two guests racing for the
            // last seats cannot both win. Nothing to configure here; the guard is
            // the WHERE clause, which the database evaluates atomically.
        });

        b.Entity<ExperienceBooking>(e =>
        {
            e.ToTable("experience_bookings");
            e.HasIndex(x => x.Reference).IsUnique();
            e.Property(x => x.Reference).HasMaxLength(20).IsRequired();
            e.Property(x => x.CancelReason).HasMaxLength(300);
            foreach (var money in new[] { "Subtotal", "ServiceFee", "Tax", "Total",
                                          "HostServiceFee", "HostPayout", "RefundedAmount" })
                e.Property(money).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.Slot).WithMany()
                .HasForeignKey(x => x.SlotId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.GuestUser).WithMany()
                .HasForeignKey(x => x.GuestUserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExperienceHold>(e =>
        {
            e.ToTable("experience_holds");
            // The sweeper looks these up by expiry, the booking path by user.
            e.HasIndex(x => x.ExpiresAt);
            e.HasIndex(x => new { x.SlotId, x.UserId });
            e.HasOne(x => x.Slot).WithMany()
                .HasForeignKey(x => x.SlotId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExperienceReview>(e =>
        {
            e.ToTable("experience_reviews");
            // docs/09 §2.10 — one review per ticket, so a seat cannot be scored twice.
            e.HasIndex(x => x.BookingId).IsUnique();
            e.HasIndex(x => x.ExperienceId);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Experience).WithMany()
                .HasForeignKey(x => x.ExperienceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AuthorUser).WithMany()
                .HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.Average);
        });

        b.Entity<ExternalLogin>(e =>
        {
            e.ToTable("external_logins");
            // One account per provider identity, and one link per provider per user.
            e.HasIndex(x => new { x.Provider, x.ProviderUserId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Provider }).IsUnique();
            e.Property(x => x.ProviderUserId).HasMaxLength(200).IsRequired();
            e.Property(x => x.ProviderEmail).HasMaxLength(200);
            e.HasOne(x => x.User).WithMany(u => u.ExternalLogins)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OneTimeCode>(e =>
        {
            e.ToTable("one_time_codes");
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.Property(x => x.SentTo).HasMaxLength(200).IsRequired();
            e.Property(x => x.CodeHash).HasMaxLength(200).IsRequired();
            e.Property(x => x.CodeSalt).HasMaxLength(100).IsRequired();
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShieldClaim>(e =>
        {
            e.ToTable("shield_claims");
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.BookingId, x.Status });
            e.Property(x => x.Reference).HasMaxLength(20).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Decision).HasMaxLength(1000);
            e.Property(x => x.ThirdPartyName).HasMaxLength(200);
            e.Property(x => x.ThirdPartyContact).HasMaxLength(200);
            e.Property(x => x.ThirdPartyKind).HasMaxLength(20);
            foreach (var money in new[]
                     {
                         "Claimed", "ExpensesClaimed", "RehousingDifference", "Approved", "Deductible",
                         "CreditGranted", "PaidFromFund", "RecoveredFromCounterparty", "RecoveredLater"
                     })
                e.Property(money).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.OpenedByUser).WithMany()
                .HasForeignKey(x => x.OpenedByUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DecidedByUser).WithMany()
                .HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ShieldEvidence>(e =>
        {
            e.ToTable("shield_evidence");
            e.Property(x => x.Url).HasMaxLength(600).IsRequired();
            e.Property(x => x.Caption).HasMaxLength(300);
            e.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            e.HasOne(x => x.Claim).WithMany(c => c.Evidence)
                .HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShieldItem>(e =>
        {
            e.ToTable("shield_items");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Value).HasColumnType("numeric(14,2)");
            e.Property(x => x.Allowed).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.Claim).WithMany(c => c.Items)
                .HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShieldEvent>(e =>
        {
            e.ToTable("shield_events");
            e.HasIndex(x => new { x.ClaimId, x.CreatedAt });
            e.Property(x => x.Actor).HasMaxLength(40).IsRequired();
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.Claim).WithMany(c => c.Events)
                .HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShieldFundMovement>(e =>
        {
            e.ToTable("shield_fund_movements");
            e.HasIndex(x => new { x.Period, x.Kind });
            e.Property(x => x.Amount).HasColumnType("numeric(14,2)");
            e.Property(x => x.Memo).HasMaxLength(300);
            e.HasOne(x => x.Claim).WithMany()
                .HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CreditEntry>(e =>
        {
            e.ToTable("credit_entries");
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.Property(x => x.Amount).HasColumnType("numeric(14,2)");
            e.Property(x => x.Memo).HasMaxLength(200);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Coupon>(e =>
        {
            e.ToTable("coupons");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32).IsRequired();
            e.Property(x => x.Campaign).HasMaxLength(120);
            e.Property(x => x.Value).HasColumnType("numeric(14,2)");
            e.Property(x => x.MaxDiscount).HasColumnType("numeric(14,2)");
            e.Property(x => x.MinBookingTotal).HasColumnType("numeric(14,2)");
        });

        b.Entity<CouponRedemption>(e =>
        {
            e.ToTable("coupon_redemptions");
            e.HasIndex(x => new { x.CouponId, x.Voided });
            e.HasIndex(x => new { x.CouponId, x.UserId, x.Voided });
            e.Property(x => x.Amount).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.Coupon).WithMany()
                .HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PayoutInstallment>(e =>
        {
            e.ToTable("payout_installments");
            e.HasIndex(x => new { x.Paid, x.DueOn });
            e.Property(x => x.Amount).HasColumnType("numeric(12,2)");
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SupportTicket>(e =>
        {
            e.ToTable("support_tickets");
            e.HasIndex(x => new { x.Status, x.Priority, x.CreatedAt });
            e.Property(x => x.Subject).HasMaxLength(StayHost.Domain.SupportTickets.SubjectMax);
            e.Property(x => x.Message).HasMaxLength(StayHost.Domain.SupportTickets.MessageMax);
            e.Property(x => x.AdminReply).HasMaxLength(StayHost.Domain.SupportTickets.MessageMax);
            e.Property(x => x.SessionId).HasMaxLength(64);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<BookingChangeRequest>(e =>
        {
            e.ToTable("booking_change_requests");
            e.HasIndex(x => new { x.BookingId, x.Status });
            e.Property(x => x.NewTotal).HasColumnType("numeric(12,2)");
            e.Property(x => x.Difference).HasColumnType("numeric(12,2)");
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ListingCancellationNote>(e =>
        {
            e.ToTable("listing_cancellation_notes");
            e.HasIndex(x => new { x.ListingId, x.CreatedAt });
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasOne(x => x.Listing).WithMany()
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SpecialOffer>(e =>
        {
            e.ToTable("special_offers");
            e.HasIndex(x => x.ThreadId);
            e.HasIndex(x => new { x.Status, x.ExpiresAt });
            e.Property(x => x.NightlyRate).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.Thread).WithMany()
                .HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Listing).WithMany()
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<GiftCard>(e =>
        {
            e.ToTable("gift_cards");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(24).IsRequired();
            e.Property(x => x.RecipientEmail).HasMaxLength(200).IsRequired();
            e.Property(x => x.RecipientName).HasMaxLength(120);
            e.Property(x => x.Message).HasMaxLength(400);
            e.Property(x => x.Amount).HasColumnType("numeric(14,2)");
            e.Property(x => x.Remaining).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.PurchasedByUser).WithMany()
                .HasForeignKey(x => x.PurchasedByUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.RedeemedByUser).WithMany()
                .HasForeignKey(x => x.RedeemedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Referral>(e =>
        {
            e.ToTable("referrals");
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.ReferrerUserId, x.InviteeEmail });
            e.Property(x => x.Code).HasMaxLength(24).IsRequired();
            e.Property(x => x.InviteeEmail).HasMaxLength(200).IsRequired();
            e.Property(x => x.ReferrerReward).HasColumnType("numeric(14,2)");
            e.Property(x => x.InviteeReward).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.ReferrerUser).WithMany()
                .HasForeignKey(x => x.ReferrerUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.InviteeUser).WithMany()
                .HasForeignKey(x => x.InviteeUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<RoomTypeOption>(e =>
        {
            e.ToTable("room_types");
            e.HasIndex(x => new { x.ListingId, x.SortOrder });
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(400);
            e.Property(x => x.ImageUrl).HasMaxLength(600);
            e.Property(x => x.PricePerNight).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.Listing).WithMany(l => l.RoomTypes)
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PriceMatchClaim>(e =>
        {
            e.ToTable("price_match_claims");
            e.HasIndex(x => x.BookingId);
            e.Property(x => x.CompetitorUrl).HasMaxLength(600).IsRequired();
            e.Property(x => x.Decision).HasMaxLength(400);
            foreach (var money in new[] { "CompetitorNightlyRate", "OurNightlyRate", "Difference" })
                e.Property(money).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.GuestUser).WithMany()
                .HasForeignKey(x => x.GuestUserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ServiceOffering>(e =>
        {
            e.ToTable("service_offerings");
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(140).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Category).HasMaxLength(40).IsRequired();
            e.Property(x => x.City).HasMaxLength(120).IsRequired();
            e.Property(x => x.Country).HasMaxLength(120).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(400);
            e.Property(x => x.PartnerName).HasMaxLength(160);
            e.Property(x => x.SearchText).HasMaxLength(4000);
            e.Property(x => x.BasePrice).HasColumnType("numeric(14,2)");
            e.Property(x => x.CommissionRate).HasColumnType("numeric(6,4)");
            e.Property(x => x.TravelFeePerKm).HasColumnType("numeric(14,2)");
            e.Property(x => x.OnSiteRequirements).HasMaxLength(2000);
            e.Property(x => x.CertificateName).HasMaxLength(200);
            e.Ignore(x => x.RequirementList);
            e.HasOne(x => x.Host).WithMany()
                .HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ServiceAddOn>(e =>
        {
            e.ToTable("service_add_ons");
            e.HasIndex(x => x.OfferingId);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Price).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.Offering).WithMany(x => x.AddOns)
                .HasForeignKey(x => x.OfferingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ServiceBookingAddOn>(e =>
        {
            e.ToTable("service_booking_add_ons");
            e.HasIndex(x => x.BookingId);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Price).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.Booking).WithMany(x => x.AddOns)
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            // The extra itself may later be retired; the booking keeps its own copy
            // of the name and price, so the receipt never changes underneath it.
            e.HasOne(x => x.AddOn).WithMany()
                .HasForeignKey(x => x.AddOnId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ServiceReview>(e =>
        {
            e.ToTable("service_reviews");
            // docs/09 §5 — one review per job, so a booking cannot be scored twice.
            e.HasIndex(x => x.BookingId).IsUnique();
            e.HasIndex(x => x.OfferingId);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Offering).WithMany()
                .HasForeignKey(x => x.OfferingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AuthorUser).WithMany()
                .HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.Average);
        });

        b.Entity<ServiceImage>(e =>
        {
            e.ToTable("service_images");
            e.Property(x => x.Url).HasMaxLength(600).IsRequired();
            e.HasOne(x => x.Offering).WithMany(x => x.Images)
                .HasForeignKey(x => x.OfferingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ServiceBooking>(e =>
        {
            e.ToTable("service_bookings");
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.OfferingId, x.StartsAt });
            e.Property(x => x.Reference).HasMaxLength(20).IsRequired();
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.Note).HasMaxLength(400);
            e.Property(x => x.CancelReason).HasMaxLength(300);
            e.Ignore(x => x.EndsAt);
            foreach (var money in new[] { "Subtotal", "ServiceFee", "Tax", "Total",
                                          "PlatformCut", "ProviderPayout", "RefundedAmount",
                                          "AddOnsTotal", "TravelFee" })
                e.Property(money).HasColumnType("numeric(14,2)");
            e.HasOne(x => x.Offering).WithMany()
                .HasForeignKey(x => x.OfferingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.GuestUser).WithMany()
                .HasForeignKey(x => x.GuestUserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BillSplit>(e =>
        {
            e.ToTable("bill_splits");
            e.HasIndex(x => x.BookingId).IsUnique();
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.OrganiserUser).WithMany()
                .HasForeignKey(x => x.OrganiserUserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BillShare>(e =>
        {
            e.ToTable("bill_shares");
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Token).HasMaxLength(64).IsRequired();
            e.Property(x => x.CardLast4).HasMaxLength(4);
            e.HasOne(x => x.Split).WithMany(s => s.Shares)
                .HasForeignKey(x => x.SplitId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<HelpArticle>(e =>
        {
            e.ToTable("help_articles");
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Category).HasMaxLength(80).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(400);
            e.Property(x => x.SearchText).HasMaxLength(6000);
        });

        b.Entity<RiskFlag>(e =>
        {
            e.ToTable("risk_flags");
            e.HasIndex(x => new { x.UserId, x.Kind, x.Status });
            e.Property(x => x.Summary).HasMaxLength(200).IsRequired();
            e.Property(x => x.Detail).HasMaxLength(400);
            e.Property(x => x.Resolution).HasMaxLength(400);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PriceRule>(e =>
        {
            e.ToTable("price_rules");
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.NightlyRate).HasPrecision(12, 2);
            e.HasIndex(x => new { x.ListingId, x.From });
            e.HasOne(x => x.Listing).WithMany(l => l.PriceRules)
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ListingQuestion>(e =>
        {
            e.ToTable("listing_questions");
            e.HasIndex(x => new { x.ListingId, x.AnsweredAt });
            e.Property(x => x.Question).HasMaxLength(Domain.ListingQuestions.MaxLength);
            e.Property(x => x.Answer).HasMaxLength(Domain.ListingQuestions.MaxAnswerLength);
            e.HasOne(x => x.Listing).WithMany()
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AskerUser).WithMany()
                .HasForeignKey(x => x.AskerUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ReviewHelpfulVote>(e =>
        {
            e.ToTable("review_helpful_votes");
            e.HasIndex(x => new { x.ReviewId, x.VoterKey }).IsUnique();
            e.Property(x => x.VoterKey).HasMaxLength(80);
            e.HasOne(x => x.Review).WithMany()
                .HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<GuestReview>(e =>
        {
            e.ToTable("guest_reviews");
            e.HasIndex(x => x.BookingId).IsUnique();
            e.Property(x => x.Text).HasMaxLength(2000);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.HostUser).WithMany()
                .HasForeignKey(x => x.HostUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.GuestUser).WithMany()
                .HasForeignKey(x => x.GuestUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasIndex(x => new { x.UserId, x.ReadAt });
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Link).HasMaxLength(300);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AbuseReport>(e =>
        {
            e.ToTable("abuse_reports");
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Target);
            e.Property(x => x.Reason).HasMaxLength(Reports.ReasonMax).IsRequired();
            e.Property(x => x.Detail).HasMaxLength(Reports.DetailMax);
            e.Property(x => x.Resolution).HasMaxLength(500);
            e.Property(x => x.SessionId).HasMaxLength(64);
            e.Ignore(x => x.SubjectId);

            // Every subject is optional at the database level because a row holds
            // exactly one of them; which one is required comes from Target and is
            // enforced in Reports.Validate, where it can be tested without a
            // database. Deleting the subject takes its reports with it — a report
            // about a listing nobody can open is not something a moderator can act on.
            e.HasOne(x => x.Listing).WithMany()
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ReportedUser).WithMany()
                .HasForeignKey(x => x.ReportedUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Message).WithMany()
                .HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Review).WithMany()
                .HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);

            // The reporter going away must not erase the report: the thing reported
            // is still there, and the queue is about the subject, not the sender.
            e.HasOne(x => x.ReporterUser).WithMany()
                .HasForeignKey(x => x.ReporterUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<EmailMessage>(e =>
        {
            e.ToTable("email_messages");
            e.Property(x => x.ToEmail).HasMaxLength(200).IsRequired();
            e.Property(x => x.ToName).HasMaxLength(150);
            e.Property(x => x.Subject).HasMaxLength(250).IsRequired();
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.Property(x => x.Error).HasMaxLength(500);
            // docs/01 TK-09 — the reader's language and the untranslated parts,
            // so the dispatcher can translate content while the frame stays
            // hand-made. All nullable: a row from before these columns is a
            // Vietnamese mail and is sent exactly as it always was.
            e.Property(x => x.Language).HasMaxLength(8);
            e.Property(x => x.RawTitle).HasMaxLength(250);
            e.Property(x => x.RawBody).HasMaxLength(4000);
            e.Property(x => x.CtaUrl).HasMaxLength(500);
        });

        b.Entity<CalendarBlock>(e =>
        {
            e.ToTable("calendar_blocks");
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.ExternalUid).HasMaxLength(300);
            e.HasIndex(x => new { x.FeedId, x.ExternalUid });
            e.HasOne(x => x.Listing).WithMany(l => l.Blocks)
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Feed).WithMany()
                .HasForeignKey(x => x.FeedId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Amenity>(e =>
        {
            e.ToTable("amenities");
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(40).IsRequired();
            e.Property(x => x.Label).HasMaxLength(80).IsRequired();
        });

        b.Entity<Listing>(e =>
        {
            e.ToTable("listings");
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.City);
            e.HasIndex(x => x.PricePerNight);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.City).HasMaxLength(100).IsRequired();
            e.Property(x => x.PricePerNight).HasPrecision(12, 2);
            e.Property(x => x.CleaningFee).HasPrecision(12, 2);
            e.Property(x => x.WeekendSurchargeRate).HasPrecision(5, 4);
            e.Property(x => x.ExtraGuestFee).HasPrecision(12, 2);
            e.Property(x => x.PetFee).HasPrecision(12, 2);
            e.Property(x => x.TimeZoneId).HasMaxLength(60);
            // Diacritic-free haystack for docs/03 §6; indexed for the LIKE the search runs.
            e.Property(x => x.SearchText).HasMaxLength(400);
            e.HasIndex(x => x.SearchText);
            e.Property(x => x.BedLayoutJson).HasColumnType("jsonb");
            e.Property(x => x.LicenseNumber).HasMaxLength(80);
            e.Property(x => x.SecurityCameraNote).HasMaxLength(500);
            // docs/01 AT-01 — review stance and the reason a rejection carries.
            e.Property(x => x.ReviewNote).HasMaxLength(500);
            e.HasIndex(x => x.ReviewStatus);
            // docs/01 CĐ-03 and CĐ-04 — the arrival guide.
            e.Property(x => x.AddressLine).HasMaxLength(CheckInGuide.LineMax * 2);
            e.Property(x => x.WifiName).HasMaxLength(CheckInGuide.LineMax);
            e.Property(x => x.WifiPassword).HasMaxLength(CheckInGuide.LineMax);
            e.Property(x => x.DoorCode).HasMaxLength(40);
            e.Property(x => x.HostPhone).HasMaxLength(30);
            e.Property(x => x.Directions).HasMaxLength(CheckInGuide.NoteMax);
            e.Property(x => x.ApplianceNotes).HasMaxLength(CheckInGuide.NoteMax);
            e.HasOne(x => x.Host).WithMany(h => h.Listings)
                .HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Cascade);
        });

        // docs/03 §6 — one row per listing per day, so "recent" can mean recent.
        b.Entity<ListingView>(e =>
        {
            e.ToTable("listing_views");
            e.HasIndex(x => new { x.ListingId, x.Day }).IsUnique();
            e.HasIndex(x => x.Day);
            e.HasOne(x => x.Listing).WithMany()
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ListingImage>(e =>
        {
            e.ToTable("listing_images");
            e.Property(x => x.Url).HasMaxLength(500).IsRequired();
            e.HasOne(x => x.Listing).WithMany(l => l.Images)
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ListingAmenity>(e =>
        {
            e.ToTable("listing_amenities");
            e.HasKey(x => new { x.ListingId, x.AmenityId });
            e.HasOne(x => x.Listing).WithMany(l => l.Amenities)
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Amenity).WithMany(a => a.Listings)
                .HasForeignKey(x => x.AmenityId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Review>(e =>
        {
            e.ToTable("reviews");
            e.Property(x => x.AuthorName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Text).HasMaxLength(2000);
            e.Property(x => x.HostReply).HasMaxLength(2000);
            e.Property(x => x.PrivateNote).HasMaxLength(2000);
            // docs/01 TĐ-11. Nullable with no default on purpose: a default here
            // would state a language for every review ever written, most of them
            // wrongly. Null means "not recorded" and is answered from the text
            // (`ReviewInsights.LanguageOf`) rather than asserted.
            e.Property(x => x.Language).HasMaxLength(8);
            // Only published reviews are ever queried for display.
            e.HasIndex(x => new { x.ListingId, x.PublishedAt });
            e.HasOne(x => x.Listing).WithMany(l => l.Reviews)
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AuthorUser).WithMany()
                .HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Wishlist>(e =>
        {
            e.ToTable("wishlists");
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.SessionId).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.SessionId);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Favorite>(e =>
        {
            e.ToTable("favorites");
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasOne(x => x.Wishlist).WithMany(w => w.Items)
                .HasForeignKey(x => x.WishlistId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SessionId, x.ListingId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.SessionId).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.Listing).WithMany()
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // docs/01 XH-01 — friend connections. One row per pair (normalised on insert).
        b.Entity<Friendship>(e =>
        {
            e.ToTable("friendships");
            e.HasIndex(x => new { x.RequesterId, x.AddresseeId }).IsUnique();
            e.HasIndex(x => x.AddresseeId);
            e.HasOne(x => x.Requester).WithMany()
                .HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Addressee).WithMany()
                .HasForeignKey(x => x.AddresseeId).OnDelete(DeleteBehavior.Restrict);
        });

        // docs/01 CĐ-10, CĐ-11 — trip plans, their bookings, companions and itinerary.
        b.Entity<TripPlan>(e =>
        {
            e.ToTable("trip_plans");
            e.HasIndex(x => x.OwnerId);
            e.Property(x => x.Name).HasMaxLength(StayHost.Domain.TripPlans.NameMax).IsRequired();
            e.HasOne(x => x.Owner).WithMany()
                .HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TripPlanBooking>(e =>
        {
            e.ToTable("trip_plan_bookings");
            e.HasIndex(x => new { x.TripPlanId, x.BookingId }).IsUnique();
            e.HasOne(x => x.TripPlan).WithMany(t => t.Bookings)
                .HasForeignKey(x => x.TripPlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TripPlanMember>(e =>
        {
            e.ToTable("trip_plan_members");
            e.HasIndex(x => new { x.TripPlanId, x.UserId }).IsUnique();
            e.HasOne(x => x.TripPlan).WithMany(t => t.Members)
                .HasForeignKey(x => x.TripPlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TripItineraryItem>(e =>
        {
            e.ToTable("trip_itinerary_items");
            e.HasIndex(x => new { x.TripPlanId, x.Day, x.SortOrder });
            e.Property(x => x.Title).HasMaxLength(StayHost.Domain.TripPlans.TitleMax).IsRequired();
            e.Property(x => x.Note).HasMaxLength(StayHost.Domain.TripPlans.NoteMax);
            e.HasOne(x => x.TripPlan).WithMany(t => t.Items)
                .HasForeignKey(x => x.TripPlanId).OnDelete(DeleteBehavior.Cascade);
        });

        // docs/01 TĐ-22 — the host's own local guidebook, one shortlist per listing.
        b.Entity<GuidebookPlace>(e =>
        {
            e.ToTable("guidebook_places");
            e.HasIndex(x => new { x.ListingId, x.SortOrder });
            e.Property(x => x.Name).HasMaxLength(Guidebooks.NameMax).IsRequired();
            e.Property(x => x.Note).HasMaxLength(Guidebooks.NoteMax);
            e.Property(x => x.Address).HasMaxLength(Guidebooks.AddressMax);
            e.HasOne(x => x.Listing).WithMany(l => l.Guidebook)
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
        });

        // docs/01 XH-03 — peer messages between friends.
        b.Entity<FriendMessage>(e =>
        {
            e.ToTable("friend_messages");
            e.HasIndex(x => new { x.FromUserId, x.ToUserId, x.SentAt });
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            e.HasOne(x => x.FromUser).WithMany()
                .HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToUser).WithMany()
                .HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Listing).WithMany()
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.SetNull);
        });

        // docs/01 YT-06 — group votes on a shared wishlist. One vote per voter per place.
        b.Entity<WishlistVote>(e =>
        {
            e.ToTable("wishlist_votes");
            e.HasIndex(x => new { x.WishlistId, x.ListingId, x.VoterKey }).IsUnique();
            e.HasIndex(x => new { x.WishlistId, x.ListingId });
            e.Property(x => x.VoterKey).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.Wishlist).WithMany()
                .HasForeignKey(x => x.WishlistId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Booking>(e =>
        {
            e.ToTable("bookings");
            e.HasIndex(x => x.Reference).IsUnique();
            e.Property(x => x.Reference).HasMaxLength(20).IsRequired();
            e.Property(x => x.SessionId).HasMaxLength(64).IsRequired();
            foreach (var money in new[]
                     {
                         nameof(Booking.RoomBeforeDiscount), nameof(Booking.RoomDiscount),
                         nameof(Booking.ExtraGuestFee), nameof(Booking.PetFee), nameof(Booking.CleaningFee),
                         nameof(Booking.Subtotal), nameof(Booking.ServiceFee), nameof(Booking.Tax),
                         nameof(Booking.Promotion), nameof(Booking.Total),
                         nameof(Booking.HostServiceFee), nameof(Booking.HostPayout),
                         nameof(Booking.RefundedAmount), nameof(Booking.GoodwillCredit),
                         nameof(Booking.CreditUsed), nameof(Booking.CouponDiscount),
                         nameof(Booking.NightlyOverride)
                     })
            {
                e.Property(money).HasPrecision(12, 2);
            }

            // A coupon deleted by an admin must not take the bookings that used
            // it with it: the discount already happened and the record stands.
            e.HasOne(x => x.Coupon).WithMany()
                .HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.SetNull);

            e.Property(x => x.PriceLinesJson).HasColumnType("jsonb");
            e.Property(x => x.GuestNote).HasMaxLength(1000);
            e.Property(x => x.CancellationReason).HasMaxLength(300);
            e.HasIndex(x => x.GuestUserId);
            e.HasOne(x => x.Listing).WithMany()
                .HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.GuestUser).WithMany()
                .HasForeignKey(x => x.GuestUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.RoomType).WithMany()
                .HasForeignKey(x => x.RoomTypeId).OnDelete(DeleteBehavior.Restrict);

            // docs/03 §2 calls double-booking "yêu cầu bắt buộc, không phải tối
            // ưu hoá", so the guarantee lives in the database, not in a check
            // that two concurrent requests could both pass. The migration turns
            // this into a GiST exclusion constraint over listing + date range.
            e.HasIndex(x => new { x.ListingId, x.CheckIn, x.CheckOut })
                .HasDatabaseName("ix_bookings_listing_range");
        });

        b.Entity<BookingEvent>(e =>
        {
            e.ToTable("booking_events");
            e.HasIndex(x => new { x.BookingId, x.CreatedAt });
            e.Property(x => x.Actor).HasMaxLength(40).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasOne(x => x.Booking).WithMany(bk => bk.Events)
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ResolutionCase>(e =>
        {
            e.ToTable("resolution_cases");
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.Reference).HasMaxLength(20).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            e.Property(x => x.EvidenceUrls).HasMaxLength(2000);
            e.Property(x => x.Response).HasMaxLength(4000);
            e.Property(x => x.Decision).HasMaxLength(2000);
            e.Property(x => x.AmountClaimed).HasPrecision(12, 2);
            e.Property(x => x.AmountAwarded).HasPrecision(12, 2);
            e.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.OpenedByUser).WithMany().HasForeignKey(x => x.OpenedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DecidedByUser).WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ResolutionEvent>(e =>
        {
            e.ToTable("resolution_events");
            e.HasIndex(x => new { x.CaseId, x.CreatedAt });
            e.Property(x => x.Actor).HasMaxLength(40).IsRequired();
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.Case).WithMany(c => c.Events).HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AdminAuditEntry>(e =>
        {
            e.ToTable("admin_audit");
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Target);
            e.Property(x => x.Action).HasMaxLength(60).IsRequired();
            e.Property(x => x.Target).HasMaxLength(60).IsRequired();
            e.Property(x => x.Before).HasMaxLength(2000);
            e.Property(x => x.After).HasMaxLength(2000);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TaxRule>(e =>
        {
            e.ToTable("tax_rules");
            e.HasIndex(x => new { x.Country, x.City });
            e.Property(x => x.Country).HasMaxLength(100).IsRequired();
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Value).HasPrecision(12, 4);
        });

        // docs/02 §J, docs/01 QT-06/TC-12 — display exchange rates, next to the
        // other operator-tunable money knob. Precision (20,12), not TaxRule's
        // (12,4): one đồng in dollars is 0.0000392, which (12,4) rounds to zero.
        b.Entity<ExchangeRate>(e =>
        {
            e.ToTable("exchange_rates");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(3).IsRequired();
            e.Property(x => x.Label).HasMaxLength(60).IsRequired();
            e.Property(x => x.Symbol).HasMaxLength(8).IsRequired();
            e.Property(x => x.RateFromVnd).HasPrecision(20, 12);
            e.Property(x => x.FeedRate).HasPrecision(20, 12);

            // Seeded IN THE MIGRATION, not in DbSeeder: the seeder only runs on
            // a blank database, which is exactly how prod kept admin@stayhost.vn
            // through the domain rename (CLAUDE.md §8.0). Seeding here means an
            // existing production database gets its eight rows the moment it
            // migrates, instead of a migrated-but-empty table and a currency
            // picker with nothing in it. Values verbatim from the constants
            // CatalogService compiled in until 05/09/2026.
            var seededAt = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
            e.HasData(
                new ExchangeRate { Id = 1, Code = "VND", Label = "Việt Nam Đồng", Symbol = "₫", RateFromVnd = 1m, SortOrder = 0, UpdatedAt = seededAt },
                new ExchangeRate { Id = 2, Code = "USD", Label = "US Dollar", Symbol = "$", RateFromVnd = 0.0000392m, SortOrder = 1, UpdatedAt = seededAt },
                new ExchangeRate { Id = 3, Code = "EUR", Label = "Euro", Symbol = "€", RateFromVnd = 0.0000362m, SortOrder = 2, UpdatedAt = seededAt },
                new ExchangeRate { Id = 4, Code = "JPY", Label = "Japanese Yen", Symbol = "¥", RateFromVnd = 0.00596m, SortOrder = 3, UpdatedAt = seededAt },
                new ExchangeRate { Id = 5, Code = "KRW", Label = "South Korean Won", Symbol = "₩", RateFromVnd = 0.0535m, SortOrder = 4, UpdatedAt = seededAt },
                new ExchangeRate { Id = 6, Code = "SGD", Label = "Singapore Dollar", Symbol = "S$", RateFromVnd = 0.0000508m, SortOrder = 5, UpdatedAt = seededAt },
                new ExchangeRate { Id = 7, Code = "AUD", Label = "Australian Dollar", Symbol = "A$", RateFromVnd = 0.0000602m, SortOrder = 6, UpdatedAt = seededAt },
                new ExchangeRate { Id = 8, Code = "GBP", Label = "British Pound", Symbol = "£", RateFromVnd = 0.0000309m, SortOrder = 7, UpdatedAt = seededAt });
        });

        b.Entity<LedgerEntry>(e =>
        {
            e.ToTable("ledger_entries");
            e.HasIndex(x => x.TransactionId);
            e.HasIndex(x => new { x.Account, x.CreatedAt });
            e.Property(x => x.TransactionKind).HasMaxLength(40).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Memo).HasMaxLength(200);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Ignore(x => x.Signed);
            e.HasOne(x => x.Booking).WithMany(bk => bk.LedgerEntries)
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ExperienceBooking).WithMany()
                .HasForeignKey(x => x.ExperienceBookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ServiceBooking).WithMany()
                .HasForeignKey(x => x.ServiceBookingId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>
    /// docs/00 §6.1 and §6.2: ledger rows are append-only. Anything that tries to
    /// update or delete one is a bug, so it fails loudly rather than silently
    /// rewriting history.
    /// </summary>
    public override int SaveChanges()
    {
        GuardLedger();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardLedger();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void GuardLedger()
    {
        var tampered = ChangeTracker.Entries<LedgerEntry>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);

        if (tampered)
        {
            throw new InvalidOperationException(
                "Sổ ghi tiền là bất biến: chỉ được thêm bút toán mới, không sửa hay xoá bút toán cũ.");
        }

        var rewrittenHistory =
            ChangeTracker.Entries<BookingEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<ResolutionEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<AdminAuditEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<ShieldEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<ShieldFundMovement>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<CreditEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted);

        if (rewrittenHistory)
        {
            throw new InvalidOperationException(
                "Lịch sử đơn, hồ sơ và nhật ký quản trị chỉ được thêm, không sửa hay xoá (docs/00 §6.2).");
        }
    }
}
