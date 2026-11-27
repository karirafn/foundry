using System.Text.Json;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;

using static Foundry.Modules.Monitoring.Features.Webhooks.Delivery.WebhookRelevance;

namespace Foundry.Modules.Monitoring.Features.Webhooks.Delivery;

/// <summary>
/// Maps a verified GitLab delivery to the <see cref="NudgeKey"/>s it concerns (ADR 0085), applying the same
/// relevance filter as GitHub: a delivery touching nothing Foundry tracks maps to no key and is dropped
/// without a provider call. Only an issue <c>update</c> maps to more than one key. The event is read from the
/// body's <c>object_kind</c>, which GitLab sets for every hook type (confidential variants share the kind of
/// their public counterpart).
/// <list type="table">
/// <item><term><c>issue</c>, <c>work_item</c></term><description>issue listing — plus the dependency pass
/// when the issue closing is a blocker of a tracked issue; an <c>update</c> also nudges the issue's awaiting-input
/// pass (awaiting input on its own thread), its failed-triage edit step (<c>triage_failed</c>) and its spec-rewrite
/// step (spec watched) — GitLab sends <c>update</c> for label changes too, and each step's body read
/// decides</description></item>
/// <item><term><c>merge_request</c>, <c>note</c> (on a merge request)</term><description>review pass for
/// the owning issue — the checks pass when that issue is checks-pending</description></item>
/// <item><term><c>note</c> (on a merge request)</term><description>otherwise, awaiting-input pass for the
/// awaiting-input issue whose question lives on that merge request</description></item>
/// <item><term><c>note</c> (on an issue or work item)</term><description>awaiting-input pass for that issue when
/// it is awaiting input with no merge request (its question lives on the issue thread)</description></item>
/// <item><term><c>pipeline</c> (finished)</term><description>checks pass for the owning issue</description></item>
/// <item><term><c>push</c></term><description>awaiting-input pass for the awaiting-input issue whose resume branch
/// it moved</description></item>
/// </list>
/// GitLab has no event for issue links or protected branches, so neither the dependency pass (beyond a
/// blocker closing) nor the branch-rules step is ever nudged from GitLab.
/// </summary>
internal static partial class GitLabWebhookEventMapper
{
    internal const string EventHeaderName = "X-Gitlab-Event";

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
        "push_events",
    ];

    /// <summary>
    /// Every project-hook trigger flag Foundry sends explicitly. The ones outside
    /// <see cref="SubscribedTriggers"/> are sent as <see langword="false"/> — notably
    /// <c>tag_push_events</c>, since a tag never moves a resume branch.
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
        objectKind is "issue" or "work_item" or "merge_request" or "note" or "pipeline" or "push";

    /// <summary>Whether <paramref name="objectKind"/> is an issue event, whose mapping needs the blockers, failed triages and spec-watched issues.</summary>
    public static bool IsIssueEvent(string? objectKind) => objectKind is "issue" or "work_item";

    public static IReadOnlyList<NudgeKey> Map(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        return GetObjectKind(payload) switch
        {
            "issue" or "work_item" => MapIssue(payload, repositoryId, tracked),
            "merge_request" => AsKeys(MapMergeRequest(payload, repositoryId, tracked)),
            "note" => AsKeys(MapNote(payload, repositoryId, tracked)),
            "pipeline" => AsKeys(MapPipeline(payload, repositoryId, tracked)),
            "push" => AsKeys(MapPush(repositoryId, tracked, payload)),
            _ => [],
        };
    }

    private static IReadOnlyList<NudgeKey> MapIssue(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        if (!TryGetObject(payload, "object_attributes", out JsonElement attributes)
            || GetInt(attributes, "iid") is not { } iid)
        {
            return [];
        }

        string? action = GetString(attributes, "action");

        if (MapBlockerClosed(repositoryId, tracked, iid, action == "close") is { } blockerClosed)
        {
            return [blockerClosed];
        }

        bool hasTriggerLabel = HasTriggerLabel(payload, "labels")
            || HasTriggerLabel(attributes, "labels")
            || HadTriggerLabel(payload);

        return MapIssueChange(repositoryId, tracked, iid, hasTriggerLabel, isEdit: action == "update");
    }

    private static NudgeKey? MapMergeRequest(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        if (!TryGetObject(payload, "object_attributes", out JsonElement attributes))
        {
            return null;
        }

        return MapMergeRequestObject(attributes, repositoryId, tracked, isComment: false);
    }

    private static NudgeKey? MapNote(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        // Comments on a merge request or an issue (work item) matter; a comment on a commit or snippet maps to nothing.
        if (!TryGetObject(payload, "object_attributes", out JsonElement attributes))
        {
            return null;
        }

        return GetString(attributes, "noteable_type") switch
        {
            "MergeRequest" => TryGetObject(payload, "merge_request", out JsonElement mergeRequest)
                ? MapMergeRequestObject(mergeRequest, repositoryId, tracked, isComment: true)
                : null,
            "Issue" or "WorkItem" => GetNotedIssueIid(payload) is { } iid
                ? MapIssueComment(repositoryId, tracked, iid)
                : null,
            _ => null,
        };
    }

    // GitLab stores work-item notes against the issue, but a work-item payload may carry its own object.
    private static int? GetNotedIssueIid(JsonElement payload)
    {
        foreach (string property in (string[])["issue", "work_item"])
        {
            if (TryGetObject(payload, property, out JsonElement issue) && GetInt(issue, "iid") is { } iid)
            {
                return iid;
            }
        }

        return null;
    }

    private static NudgeKey? MapMergeRequestObject(
        JsonElement mergeRequest,
        MonitoredRepositoryId repositoryId,
        WebhookTrackingContext tracked,
        bool isComment)
    {
        int? iid = GetInt(mergeRequest, "iid");
        string? sourceBranch = GetString(mergeRequest, "source_branch");
        IReadOnlyList<int> mergeRequestIids = iid is { } n ? [n] : [];
        IReadOnlyList<string> branches = sourceBranch is { } b ? [b] : [];

        return isComment
            ? MapPullRequestComment(repositoryId, tracked, mergeRequestIids, branches, MergeRequestUrlPattern())
            : MapOwningIssue(repositoryId, tracked, mergeRequestIids, branches, MergeRequestUrlPattern());
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

        return MapChecksDelivery(repositoryId, tracked, mergeRequestIids, branches, MergeRequestUrlPattern());
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
