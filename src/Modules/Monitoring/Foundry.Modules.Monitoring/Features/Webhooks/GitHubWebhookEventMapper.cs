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
/// <item><term><c>issues</c></term><description>issue listing</description></item>
/// <item><term><c>issue_dependencies</c></term><description>issue listing + dependency pass</description></item>
/// <item><term><c>pull_request</c>, <c>pull_request_review</c>, <c>pull_request_review_comment</c>,
/// <c>issue_comment</c> (on a PR)</term><description>review pass for the owning issue — the checks
/// pass when that issue is checks-pending (the review pass only acts on review issues)</description></item>
/// <item><term><c>check_suite</c> (completed), <c>status</c></term><description>checks pass for the owning issue</description></item>
/// <item><term><c>branch_protection_rule</c>, <c>repository_ruleset</c></term><description>branch-rules step</description></item>
/// <item><term><c>ping</c></term><description>none (health only)</description></item>
/// </list>
/// </summary>
internal static partial class GitHubWebhookEventMapper
{
    internal const string EventHeaderName = "X-GitHub-Event";

    internal const string PingEvent = "ping";

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
    ];

    [GeneratedRegex(@"/pull/(?<number>\d{1,9})/?$", RegexOptions.CultureInvariant)]
    private static partial Regex PullRequestUrlPattern();

    /// <summary>Whether mapping <paramref name="eventName"/> needs the repository's tracked issues.</summary>
    public static bool NeedsTrackingContext(string eventName) =>
        eventName is "issues" or "issue_dependencies" or "pull_request" or "pull_request_review"
            or "pull_request_review_comment" or "issue_comment" or "check_suite" or "status";

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
            "pull_request" or "pull_request_review" or "pull_request_review_comment"
                => MapPullRequest(payload, repositoryId, tracked),
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

    private static NudgeKey? MapPullRequest(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        if (!TryGetObject(payload, "pull_request", out JsonElement pullRequest))
        {
            return null;
        }

        int? pullNumber = GetInt(pullRequest, "number");
        string? headBranch = TryGetObject(pullRequest, "head", out JsonElement head) ? GetString(head, "ref") : null;

        return WebhookRelevance.MapOwningIssue(repositoryId, tracked, pullNumber is { } n ? [n] : [], headBranch is { } b ? [b] : [], PullRequestUrlPattern());
    }

    private static NudgeKey? MapIssueComment(JsonElement payload, MonitoredRepositoryId repositoryId, WebhookTrackingContext tracked)
    {
        // Only comments on a pull request matter; a comment on a plain issue maps to nothing.
        if (!TryGetObject(payload, "issue", out JsonElement issue)
            || !TryGetObject(issue, "pull_request", out _)
            || GetInt(issue, "number") is not { } pullNumber)
        {
            return null;
        }

        return WebhookRelevance.MapOwningIssue(repositoryId, tracked, [pullNumber], [], PullRequestUrlPattern());
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
