namespace Foundry.Modules.Monitoring.Features.Polling;

/// <summary>
/// The categories of provider change one scheduled poll found (ADR 0085). Compared against the
/// provider's webhook coverage (<see cref="Webhooks.WebhookProviders.MissedChangeCategories"/>) to decide
/// whether the poll found a <em>missed change</em>. Dependency refreshes are deliberately not a category:
/// a blocker closing outside the tracked set is never announced by a relevant delivery on either provider,
/// and GitLab has no event for issue links at all.
/// </summary>
[Flags]
internal enum PollChanges
{
    None = 0,

    /// <summary>Issue detection, detail change or untrack in the listing pass.</summary>
    IssueListing = 1 << 0,

    /// <summary>A closed issue, a closed pull request or new review feedback in the review pass.</summary>
    Review = 1 << 1,

    /// <summary>A provider-driven checks verdict (never a time-based exit).</summary>
    Checks = 1 << 2,

    /// <summary>The default branch's protection rules changed what makes the repository (in)eligible.</summary>
    BranchRules = 1 << 3,
}
