using System.Text.Json;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;

using static Foundry.Modules.Monitoring.Features.Webhooks.WebhookRelevance;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Maps a verified GitLab delivery to at most one <see cref="NudgeKey"/> (ADR 0085), applying the same
/// relevance filter as GitHub: a delivery touching nothing Foundry tracks maps to <see langword="null"/>
/// and is dropped without a provider call. The event is read from the body's <c>object_kind</c>, which
/// GitLab sets for every hook type (confidential variants share the kind of their public counterpart).
/// <list type="table">
/// <item><term><c>issue</c>, <c>work_item</c></term><description>issue listing — plus the dependency pass
/// when the issue closing is a blocker of a tracked issue</description></item>
/// <item><term><c>merge_request</c>, <c>note</c> (on a merge request)</term><description>review pass for
/// the owning issue — the checks pass when that issue is checks-pending</description></item>
/// <item><term><c>pipeline</c> (finished)</term><description>checks pass for the owning issue</description></item>
/// <item><term><c>push</c></term><description>no nudge — handled by <see cref="BranchPushPublisher"/></description></item>
/// </list>
/// GitLab has no event for issue links or protected branches, so neither the dependency pass (beyond a
/// blocker closing) nor the branch-rules step is ever nudged from GitLab.
/// </summary>
internal static partial class GitLabWebhookEventMapper
{
    internal const string EventHeaderName = "X-Gitlab-Event";

    /// <summary>
    /// The <c>object_kind</c> of a Push Hook. Not a nudge: <see cref="BranchPushPublisher"/> turns a push to a
    /// running worker branch into <c>ProviderBranchPushed</c> so Workers refreshes the run commit count (issue #660).
    /// </summary>
    internal const string PushObjectKind = "push";

    /// <summary>
    /// The exact triggers the operator ticks on the GitLab webhook form. The confidential variants are
    /// optional: they only matter when tracked issues or their comments are confidential.
    /// </summary>
    internal static readonly IReadOnlyList<string> SubscribedEvents =
    [
        "Issues events (Work item events)",
        "Confidential issues events",
        "Comments",
        "Confidential comments",
        "Merge request events",
        "Pipeline events",
        "Push events",
    ];

    /// <summary>
    /// The project-hook API trigger flags matching <see cref="SubscribedEvents"/>, set to
    /// <see langword="true"/> when Foundry registers the hook itself.
    /// </summary>
    internal static readonly IReadOnlyList<string> SubscribedTriggers =
    [
        "issues_events",
        "confidential_issues_events",
        "note_events",
        "confidential_note_events",
        "merge_requests_events",
        "pipeline_events",
    ];

    /// <summary>
    /// Every project-hook trigger flag Foundry sends explicitly. The ones outside
    /// <see cref="SubscribedTriggers"/> are sent as <see langword="false"/> — notably
    /// <c>push_events</c>, which GitLab enables by default.
    /// </summary>
    internal static readonly IReadOnlyList<string> AllTriggers =
    [
        "push_events",
        "tag_push_events",
        "issues_events",
        "confidential_issues_events",
        "note_events",
        "confidential_note_events",
        "merge_requests_events",
        "job_events",
        "pipeline_events",
        "wiki_page_events",
        "deployment_events",
        "releases_events",
    ];

    /// <summary>
    /// Pipeline statuses that settle a checks verdict (<c>Checks.FromGitLab</c>): green, red or unfinished.
    /// Intermediate statuses (created, pending, running…) leave the issue checks-pending, so they are dropped.
    /// </summary>
    private static readonly HashSet<string> FinishedPipelineStatuses =
        new(["success", "skipped", "failed", "canceled", "canceling", "manual"], StringComparer.Ordinal);

    // GitLab merge request web URLs end in "/-/merge_requests/<iid>".
    [GeneratedRegex(@"/merge_requests/(?<number>\d{1,9})/?$", RegexOptions.CultureInvariant)]
    private static partial Regex MergeRequestUrlPattern();

    /// <summary>The <c>object_kind</c> of a GitLab delivery body, or <see langword="null"/> when absent.</summary>
    public static string? GetObjectKind(JsonElement payload) => GetString(payload, "object_kind");

    /// <summary>Whether mapping <paramref name="objectKind"/> needs the repository's tracked issues.</summary>
    public static bool NeedsTrackingContext(string? objectKind) =>
        objectKind is "issue" or "work_item" or "merge_request" or "note" or "pipeline";

    /// <summary>Whether mapping <paramref name="objectKind"/> needs the blockers of tracked issues.</summary>
    public static bool NeedsBlockers(string? objectKind) => objectKind is "issue" or "work_item";

    public static NudgeKey? Map(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        return GetObjectKind(payload) switch
        {
            "issue" or "work_item" => MapIssue(payload, repositoryId, tracked),
            "merge_request" => MapMergeRequest(payload, repositoryId, tracked),
            "note" => MapNote(payload, repositoryId, tracked),
            "pipeline" => MapPipeline(payload, repositoryId, tracked),
            _ => null,
        };
    }

    private static NudgeKey? MapIssue(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        if (!TryGetObject(payload, "object_attributes", out JsonElement attributes)
            || GetInt(attributes, "iid") is not { } iid)
        {
            return null;
        }

        if (MapBlockerClosed(repositoryId, tracked, iid, GetString(attributes, "action") == "close") is { } blockerClosed)
        {
            return blockerClosed;
        }

        bool hasTriggerLabel = HasTriggerLabel(payload, "labels")
            || HasTriggerLabel(attributes, "labels")
            || HadTriggerLabel(payload);

        return tracked.KnownIssueNumbers.Contains(iid) || hasTriggerLabel
            ? new NudgeKey(repositoryId, NudgePass.IssueListing)
            : null;
    }

    private static NudgeKey? MapMergeRequest(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        if (!TryGetObject(payload, "object_attributes", out JsonElement attributes))
        {
            return null;
        }

        return MapMergeRequestObject(attributes, repositoryId, tracked);
    }

    private static NudgeKey? MapNote(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        // Only comments on a merge request matter; a comment on an issue, commit or snippet maps to nothing.
        if (!TryGetObject(payload, "object_attributes", out JsonElement attributes)
            || GetString(attributes, "noteable_type") != "MergeRequest"
            || !TryGetObject(payload, "merge_request", out JsonElement mergeRequest))
        {
            return null;
        }

        return MapMergeRequestObject(mergeRequest, repositoryId, tracked);
    }

    private static NudgeKey? MapMergeRequestObject(
        JsonElement mergeRequest,
        MonitoredRepositoryId repositoryId,
        WebhookTrackingContext tracked)
    {
        int? iid = GetInt(mergeRequest, "iid");
        string? sourceBranch = GetString(mergeRequest, "source_branch");

        return MapOwningIssue(
            repositoryId,
            tracked,
            iid is { } n ? [n] : [],
            sourceBranch is { } b ? [b] : [],
            MergeRequestUrlPattern());
    }

    private static NudgeKey? MapPipeline(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        if (!TryGetObject(payload, "object_attributes", out JsonElement attributes)
            || GetString(attributes, "status") is not { } status
            || !FinishedPipelineStatuses.Contains(status))
        {
            return null;
        }

        List<int> mergeRequestIids = [];
        List<string> branches = [];

        if (GetString(attributes, "ref") is { } pipelineRef)
        {
            branches.Add(pipelineRef);
        }

        if (TryGetObject(payload, "merge_request", out JsonElement mergeRequest))
        {
            if (GetInt(mergeRequest, "iid") is { } iid)
            {
                mergeRequestIids.Add(iid);
            }

            if (GetString(mergeRequest, "source_branch") is { } sourceBranch)
            {
                branches.Add(sourceBranch);
            }
        }

        return MapChecksPendingIssue(repositoryId, tracked, mergeRequestIids, branches, MergeRequestUrlPattern());
    }

    private static bool HadTriggerLabel(JsonElement payload) =>
        TryGetObject(payload, "changes", out JsonElement changes)
        && TryGetObject(changes, "labels", out JsonElement labelChange)
        && (HasTriggerLabel(labelChange, "previous") || HasTriggerLabel(labelChange, "current"));

    private static bool HasTriggerLabel(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out JsonElement labels)
            || labels.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement label in labels.EnumerateArray())
        {
            if (string.Equals(GetString(label, "title"), TriggerLabel, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
