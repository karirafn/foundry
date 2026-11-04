using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// What Foundry currently tracks for one repository — the input to the relevance filter.
/// </summary>
internal sealed record WebhookTrackingContext(
    IReadOnlySet<int> KnownIssueNumbers,
    IReadOnlyList<ReviewIssueInfo> ReviewIssues,
    IReadOnlyList<ChecksPendingIssueInfo> ChecksPendingIssues)
{
    internal static readonly WebhookTrackingContext Empty = new(new HashSet<int>(), [], []);
}

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

    /// <summary>
    /// The label that marks an issue for Foundry — the GitHub issue listing filters on it
    /// (<c>GitHubHttpClient</c> <c>IssueListQuery</c>).
    /// </summary>
    internal const string TriggerLabel = "foundry";

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

    // Worker branches are "<prefix>/<issue-number>-<slug>" (or "<prefix>/<issue-number>").
    [GeneratedRegex(@"^[a-z][a-z0-9-]*/(?<number>\d{1,9})(?:-|$)", RegexOptions.CultureInvariant)]
    private static partial Regex WorkerBranchPattern();

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

        return MapOwningIssue(repositoryId, tracked, pullNumber is { } n ? [n] : [], headBranch is { } b ? [b] : []);
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

        return MapOwningIssue(repositoryId, tracked, [pullNumber], []);
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

        return MapChecksPendingIssue(repositoryId, tracked, pullNumbers, branches);
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

        return MapChecksPendingIssue(repositoryId, tracked, [], branches);
    }

    private static NudgeKey? MapOwningIssue(
        MonitoredRepositoryId repositoryId,
        WebhookTrackingContext tracked,
        IReadOnlyList<int> pullNumbers,
        IReadOnlyList<string> branches)
    {
        HashSet<int> candidates = CandidateIssueNumbers(branches);

        foreach (ReviewIssueInfo review in tracked.ReviewIssues)
        {
            if (candidates.Contains(review.IssueNumber) || MatchesPull(review.PullRequestUrl, pullNumbers))
            {
                return new NudgeKey(repositoryId, NudgePass.Review, review.IssueNumber);
            }
        }

        return MapChecksPendingIssue(repositoryId, tracked, pullNumbers, branches);
    }

    private static NudgeKey? MapChecksPendingIssue(
        MonitoredRepositoryId repositoryId,
        WebhookTrackingContext tracked,
        IReadOnlyList<int> pullNumbers,
        IReadOnlyList<string> branches)
    {
        HashSet<int> candidates = CandidateIssueNumbers(branches);

        foreach (ChecksPendingIssueInfo checksPending in tracked.ChecksPendingIssues)
        {
            if (candidates.Contains(checksPending.IssueNumber) || MatchesPull(checksPending.PullRequestUrl, pullNumbers))
            {
                return new NudgeKey(repositoryId, NudgePass.Checks, checksPending.IssueNumber);
            }
        }

        return null;
    }

    private static HashSet<int> CandidateIssueNumbers(IReadOnlyList<string> branches)
    {
        HashSet<int> numbers = [];
        foreach (string branch in branches)
        {
            Match match = WorkerBranchPattern().Match(branch);
            if (match.Success && int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }

    private static bool MatchesPull(string pullRequestUrl, IReadOnlyList<int> pullNumbers)
    {
        if (pullNumbers.Count == 0)
        {
            return false;
        }

        Match match = PullRequestUrlPattern().Match(pullRequestUrl);
        return match.Success
            && int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            && pullNumbers.Contains(number);
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

    private static bool TryGetObject(JsonElement element, string property, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static int? GetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int number)
            ? number
            : null;

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
