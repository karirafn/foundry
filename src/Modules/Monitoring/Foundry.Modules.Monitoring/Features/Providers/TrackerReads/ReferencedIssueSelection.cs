using System.Globalization;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Providers.TrackerReads;

/// <summary>
/// Selects the Referenced Issues of a dispatched issue: its same-project links by relation, then the
/// same-project <c>#N</c> mentions in its body and comments in first-mention order.
/// </summary>
internal static partial class ReferencedIssueSelection
{
    public const int MaxReferencedIssues = 10;

    public static IReadOnlyList<IssueReference> Select(
        int issueNumber,
        IssueContent content,
        IReadOnlyList<IssueReference> links)
    {
        IEnumerable<IssueReference> mentions = MentionedNumbers(content)
            .Select(number => new IssueReference(number, IssueRelation.Mention));

        // IssueRelation declares the strongest relation first and Mention last, and OrderBy is stable, so one
        // ordering puts links ahead of mentions, keeps provider and first-mention order within a relation, and
        // lets DistinctBy keep each number at its strongest relation.
        return
        [
            .. links
                .Concat(mentions)
                .Where(reference => reference.Number != issueNumber)
                .OrderBy(reference => reference.Relation)
                .DistinctBy(reference => reference.Number)
                .Take(MaxReferencedIssues),
        ];
    }

    private static IEnumerable<int> MentionedNumbers(IssueContent content) =>
        new[] { content.Body }
            .Concat(content.Comments.OrderBy(comment => comment.CreatedAt).Select(comment => comment.Body))
            .SelectMany(text => MentionPattern().Matches(text))
            .Select(match => int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture))
            .Distinct();

    // A mention is #N with N in 1..999,999,999. The lookbehind rejects a cross-project reference (owner/repo#N),
    // an HTML entity (&#N;), a merge-request or heading marker, and a fragment glued to a word or slug; the
    // lookahead rejects a number that runs into a word (#12abc).
    [GeneratedRegex(
        @"(?<![\w&/.#!-])#(?<number>[1-9][0-9]{0,8})(?!\w)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex MentionPattern();
}
