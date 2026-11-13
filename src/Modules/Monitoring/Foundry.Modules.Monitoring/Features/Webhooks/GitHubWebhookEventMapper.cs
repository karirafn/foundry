using System.Text.Json;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;

using static Foundry.Modules.Monitoring.Features.Webhooks.WebhookRelevance;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Maps a verified GitHub delivery to at most one <see cref="NudgeKey"/> (ADR 0085), applying the
/// relevance filter: a delivery touching nothing Foundry tracks maps to <see langword="null"/> and is
/// dropped without a provider call.
/// <list type="table">
/// <item><term><c>issues</c></term><description>issue listing — plus the dependency pass when the issue
/// closing is a blocker of a tracked issue (GitHub sends no <c>issue_dependencies</c> delivery then)</description></item>
/// <item><term><c>issue_dependencies</c></term><description>issue listing + dependency pass</description></item>
/// <item><term><c>pull_request</c>, <c>pull_request_review</c>, <c>pull_request_review_comment</c>,
/// <c>issue_comment</c> (on a PR)</term><description>review pass for the owning issue — the checks
/// pass when that issue is checks-pending (the review pass only acts on review issues)</description></item>
/// <item><term><c>pull_request_review</c>, <c>pull_request_review_comment</c>, <c>issue_comment</c> (on a PR)
/// </term><description>otherwise, awaiting-input pass for the awaiting-input issue whose question lives on that
/// PR</description></item>
/// <item><term><c>issue_comment</c> (on an issue)</term><description>awaiting-input pass for that issue when it
/// is awaiting input with no PR (its question lives on the issue thread)</description></item>
/// <item><term><c>check_suite</c> (completed), <c>status</c></term><description>checks pass for the owning issue</description></item>
/// <item><term><c>branch_protection_rule</c>, <c>repository_ruleset</c></term><description>branch-rules step</description></item>
/// <item><term><c>push</c></term><description>no nudge — handled by <see cref="BranchPushPublisher"/></description></item>
/// <item><term><c>ping</c></term><description>none (health only)</description></item>
/// </list>
/// </summary>
internal static partial class GitHubWebhookEventMapper
{
    internal const string EventHeaderName = "X-GitHub-Event";

    internal const string PingEvent = "ping";

    /// <summary>
    /// A branch push. Not a nudge: <see cref="BranchPushPublisher"/> turns a push to a running worker branch
    /// into <c>ProviderBranchPushed</c> so Workers refreshes the run commit count (issue #660).
    /// </summary>
    internal const string PushEvent = "push";

    /// <summary>The label that marks an issue for Foundry.</summary>
    internal const string TriggerLabel = WebhookRelevance.TriggerLabel;

    /// <summary>The exact event list the operator subscribes the webhook to.</summary>
    internal static readonly IReadOnlyList<string> SubscribedEvents =
    [
        "issues",
        "issue_dependencies",
        "pull_request",
        "pull_request_review",
        "pull_request_review_comment",
        "issue_comment",
        "check_suite",
        "status",
        "branch_protection_rule",
        "repository_ruleset",
        PushEvent,
    ];

    [GeneratedRegex(@"/pull/(?<number>\d{1,9})/?$", RegexOptions.CultureInvariant)]
    private static partial Regex PullRequestUrlPattern();

    /// <summary>Whether mapping <paramref name="eventName"/> needs the repository's tracked issues.</summary>
    public static bool NeedsTrackingContext(string eventName) =>
        eventName is "issues" or "issue_dependencies" or "pull_request" or "pull_request_review"
            or "pull_request_review_comment" or "issue_comment" or "check_suite" or "status";

    /// <summary>
    /// Whether mapping <paramref name="eventName"/> needs the blockers of tracked issues — only an
    /// <c>issues</c> close can be a blocker closing.
    /// </summary>
    public static bool NeedsBlockers(string? eventName) => eventName is "issues";

    public static NudgeKey? Map(
        string eventName,
        JsonElement payload,
        MonitoredRepositoryId repositoryId,
        WebhookTrackingContext tracked)
    {
        return eventName switch
        {
            "issues" => MapIssues(payload, repositoryId, tracked),
            "issue_dependencies" => MapIssueDependencies(payload, repositoryId, tracked),
            "pull_request" => MapPullRequest(payload, repositoryId, tracked, isComment: false),
            "pull_request_review" or "pull_request_review_comment" => MapPullRequest(payload, repositoryId, tracked, isComment: true),
            "issue_comment" => MapIssueComment(payload, repositoryId, tracked),
            "check_suite" => MapCheckSuite(payload, repositoryId, tracked),
            "status" => MapStatus(payload, repositoryId, tracked),
            "branch_protection_rule" or "repository_ruleset" => new NudgeKey(repositoryId, NudgePass.BranchRules),
            _ => null,
        };
    }

    private static NudgeKey? MapIssues(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        if (!TryGetObject(payload, "issue", out JsonElement issue) || GetInt(issue, "number") is not { } number)
        {
            return null;
        }

        if (MapBlockerClosed(repositoryId, tracked, number, GetString(payload, "action") == "closed") is { } blockerClosed)
        {
            return blockerClosed;
        }

        bool isTracked = tracked.KnownIssueNumbers.Contains(number);
        bool hasTriggerLabel = HasLabel(issue, TriggerLabel)
            || (TryGetObject(payload, "label", out JsonElement label) && IsTriggerLabel(label));

        return isTracked || hasTriggerLabel
            ? new NudgeKey(repositoryId, NudgePass.IssueListing)
            : null;
    }

    private static NudgeKey? MapIssueDependencies(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        foreach (string property in (string[])["blocked_issue", "blocking_issue", "issue"])
        {
            if (TryGetObject(payload, property, out JsonElement issue)
                && GetInt(issue, "number") is { } number
                && tracked.KnownIssueNumbers.Contains(number))
            {
                return new NudgeKey(repositoryId, NudgePass.IssueListingAndDependencies);
            }
        }

        return null;
    }

    private static NudgeKey? MapPullRequest(
        JsonElement payload,
        MonitoredRepositoryId repositoryId,
        WebhookTrackingContext tracked,
        bool isComment)
    {
        if (!TryGetObject(payload, "pull_request", out JsonElement pullRequest))
        {
            return null;
        }

        int? pullNumber = GetInt(pullRequest, "number");
        string? headBranch = TryGetObject(pullRequest, "head", out JsonElement head) ? GetString(head, "ref") : null;
        IReadOnlyList<int> pullNumbers = pullNumber is { } n ? [n] : [];
        IReadOnlyList<string> branches = headBranch is { } b ? [b] : [];

        return isComment
            ? WebhookRelevance.MapPullRequestComment(repositoryId, tracked, pullNumbers, branches, PullRequestUrlPattern())
            : WebhookRelevance.MapOwningIssue(repositoryId, tracked, pullNumbers, branches, PullRequestUrlPattern());
    }

    private static NudgeKey? MapIssueComment(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        if (!TryGetObject(payload, "issue", out JsonElement issue) || GetInt(issue, "number") is not { } number)
        {
            return null;
        }

        // GitHub numbers pull requests and issues in one sequence; "issue.pull_request" marks a PR thread.
        return TryGetObject(issue, "pull_request", out _)
            ? WebhookRelevance.MapPullRequestComment(repositoryId, tracked, [number], [], PullRequestUrlPattern())
            : WebhookRelevance.MapIssueComment(repositoryId, tracked, number);
    }

    private static NudgeKey? MapCheckSuite(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        if (GetString(payload, "action") != "completed" || !TryGetObject(payload, "check_suite", out JsonElement suite))
        {
            return null;
        }

        List<int> pullNumbers = [];
        if (suite.TryGetProperty("pull_requests", out JsonElement pulls) && pulls.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement pull in pulls.EnumerateArray())
            {
                if (GetInt(pull, "number") is { } n)
                {
                    pullNumbers.Add(n);
                }
            }
        }

        List<string> branches = GetString(suite, "head_branch") is { } branch ? [branch] : [];

        return WebhookRelevance.MapChecksPendingIssue(repositoryId, tracked, pullNumbers, branches, PullRequestUrlPattern());
    }

    private static NudgeKey? MapStatus(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        List<string> branches = [];
        if (payload.TryGetProperty("branches", out JsonElement branchArray) && branchArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement branch in branchArray.EnumerateArray())
            {
                if (GetString(branch, "name") is { } name)
                {
                    branches.Add(name);
                }
            }
        }

        return WebhookRelevance.MapChecksPendingIssue(repositoryId, tracked, [], branches, PullRequestUrlPattern());
    }

    private static bool HasLabel(JsonElement issue, string labelName)
    {
        if (!issue.TryGetProperty("labels", out JsonElement labels) || labels.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement label in labels.EnumerateArray())
        {
            if (string.Equals(GetString(label, "name"), labelName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTriggerLabel(JsonElement label) =>
        string.Equals(GetString(label, "name"), TriggerLabel, StringComparison.OrdinalIgnoreCase);
}
