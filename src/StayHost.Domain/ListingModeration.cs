namespace StayHost.Domain;

/// <summary>
/// docs/01 AT-01 — where a listing stands with the platform's reviewers.
///
/// <see cref="Approved"/> is 0 on purpose: every listing that existed before this
/// gate did, and every seeded demo place, reads as approved with no back-fill, so
/// turning the column on changes nothing that was already live. A listing only
/// enters <see cref="Pending"/> when the switch below is on and a host publishes a
/// place that has never been approved; an admin's decision moves it to
/// <see cref="Approved"/> or <see cref="Rejected"/> from there.
/// </summary>
public enum ListingReviewStatus
{
    Approved = 0,
    Pending = 1,
    Rejected = 2
}

/// <summary>
/// docs/01 AT-01 — whether a brand-new listing has to be looked at before the
/// public can see it.
///
/// Off by default, which is how the platform shipped: a host publishes and the
/// place is live at once. The customer decides operationally whether to switch
/// pre-publish review on; the queue, the approve/reject path and the search gate
/// are all built and only wait on this flag. Turning it on reviews <em>new</em>
/// listings only — once a place is approved, later edits stay live, because
/// AT-01 is about new listings appearing, not about freezing every edit.
/// </summary>
public sealed record ModerationSettings
{
    public bool NewListingsRequireApproval { get; init; }

    public static ModerationSettings Current { get; set; } = new();
}

/// <summary>docs/01 AT-01 — the pure rules for the review gate.</summary>
public static class ListingModeration
{
    /// <summary>
    /// A place reaches the public only when it is both published and approved.
    /// With the gate off every listing is already approved, so this is exactly
    /// "is it published?" — the behaviour before AT-01.
    /// </summary>
    public static bool IsPubliclyVisible(bool isPublished, ListingReviewStatus status) =>
        isPublished && status == ListingReviewStatus.Approved;

    /// <summary>
    /// The status a freshly created listing takes. Under an active gate every new
    /// place is unreviewed, draft or not; only with the gate off is it approved
    /// outright.
    ///
    /// A draft used to be approved on creation, and since an approved place stays
    /// approved when it is saved (<see cref="StatusOnSave"/>), saving the draft
    /// again with "publish" on put it in front of the public without an admin
    /// ever seeing it. The admin queue shows only published ones.
    /// </summary>
    public static ListingReviewStatus StatusForNew(bool isPublished, bool requireApproval) =>
        requireApproval ? ListingReviewStatus.Pending : ListingReviewStatus.Approved;

    /// <summary>
    /// Where an existing listing lands when its host saves it. An approved place
    /// stays approved — editing it does not send it back to the queue. A rejected
    /// or pending place that the host publishes again is a resubmission, so it
    /// returns to the queue. With the gate off nothing is ever held.
    /// </summary>
    public static ListingReviewStatus StatusOnSave(
        ListingReviewStatus current, bool isPublished, bool requireApproval)
    {
        if (!requireApproval) return ListingReviewStatus.Approved;
        if (current == ListingReviewStatus.Approved) return ListingReviewStatus.Approved;
        return isPublished ? ListingReviewStatus.Pending : current;
    }

    public static string Label(ListingReviewStatus status) => status switch
    {
        ListingReviewStatus.Pending => "Đang chờ duyệt",
        ListingReviewStatus.Rejected => "Bị từ chối",
        _ => "Đã duyệt"
    };
}
