using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// What Foundry currently tracks for one repository — the input to the relevance filter.
/// <see cref="BlockerIssueNumbers"/> holds the same-repository issues that currently block a tracked
/// issue; it is loaded only for providers whose issue events must nudge the dependency pass.
/// </summary>
internal sealed record WebhookTrackingContext(
    IReadOnlySet<int> KnownIssueNumbers,
    IReadOnlyList<ReviewIssueInfo> ReviewIssues,
    IReadOnlyList<ChecksPendingIssueInfo> ChecksPendingIssues,
    IReadOnlySet<int>? BlockerIssueNumbers = null)
{
    internal static readonly WebhookTrackingContext Empty = new(new HashSet<int>(), [], []);

    internal IReadOnlySet<int> Blockers => BlockerIssueNumbers ?? EmptySet;

    private static readonly IReadOnlySet<int> EmptySet = new HashSet<int>();
}

/// <summary>
/// Provider-neutral relevance rules shared by the webhook event mappers (ADR 0085): a pull/merge
/// request delivery nudges the review pass of the issue that owns it, or the checks pass when that
/// issue is checks-pending; anything else maps to <see langword="null"/>.
/// </summary>
internal static partial class WebhookRelevance
{
    /// <summary>
    /// The label that marks an issue for Foundry — both provider listings filter on it
    /// (<c>GitHubHttpClient</c> <c>IssueListQuery</c>, <c>GitLabHttpClient</c> <c>labels=foundry</c>).
    /// </summary>
    internal const string TriggerLabel = "foundry";

    // Worker branches are "<prefix>/<issue-number>-<slug>" (or "<prefix>/<issue-number>").
    [GeneratedRegex(@"^[a-z][a-z0-9-]*/(?<number>\d{1,9})(?:-|$)", RegexOptions.CultureInvariant)]
    private static partial Regex WorkerBranchPattern();

    /// <summary>
    /// Maps a pull/merge request delivery to the review pass of its owning issue, falling back to the
    /// checks pass when the owner is checks-pending (the review pass only acts on review issues).
    /// </summary>
    internal static NudgeKey? MapOwningIssue(
        MonitoredRepositoryId repositoryId,
        WebhookTrackingContext tracked,
        IReadOnlyList<int> pullNumbers,
        IReadOnlyList<string> branches,
        Regex pullRequestUrlPattern)
    {
        HashSet<int> candidates = CandidateIssueNumbers(branches);

        foreach (ReviewIssueInfo review in tracked.ReviewIssues)
        {
            if (candidates.Contains(review.IssueNumber) || MatchesPull(review.PullRequestUrl, pullNumbers, pullRequestUrlPattern))
            {
                return new NudgeKey(repositoryId, NudgePass.Review, review.IssueNumber);
            }
        }

        return MapChecksPendingIssue(repositoryId, tracked, pullNumbers, branches, pullRequestUrlPattern);
    }

    /// <summary>Maps a checks delivery to the checks pass of the checks-pending issue that owns it.</summary>
    internal static NudgeKey? MapChecksPendingIssue(
        MonitoredRepositoryId repositoryId,
        WebhookTrackingContext tracked,
        IReadOnlyList<int> pullNumbers,
        IReadOnlyList<string> branches,
        Regex pullRequestUrlPattern)
    {
        HashSet<int> candidates = CandidateIssueNumbers(branches);

        foreach (ChecksPendingIssueInfo checksPending in tracked.ChecksPendingIssues)
        {
            if (candidates.Contains(checksPending.IssueNumber)
                || MatchesPull(checksPending.PullRequestUrl, pullNumbers, pullRequestUrlPattern))
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

    private static bool MatchesPull(string pullRequestUrl, IReadOnlyList<int> pullNumbers, Regex pullRequestUrlPattern)
    {
        if (pullNumbers.Count == 0)
        {
            return false;
        }

        Match match = pullRequestUrlPattern.Match(pullRequestUrl);
        return match.Success
            && int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            && pullNumbers.Contains(number);
    }

    internal static bool TryGetObject(JsonElement element, string property, out JsonElement value)
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

    internal static int? GetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int number)
            ? number
            : null;

    internal static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
