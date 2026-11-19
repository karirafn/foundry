using System.Text;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Shared.Markdown;

namespace Foundry.Modules.Workers.Features.Reviews;

/// <summary>
/// The one comment a review run posts on its pull request (ADR 0113): an alert chosen by the outcome, the blocking
/// findings and unmet criteria in view, and everything else collapsed.
/// </summary>
internal static class ReviewVerdictComment
{
    // GitHub rejects a comment body over 65,536 characters; the margin leaves room for provider-side expansion.
    private const int MaxLength = 60_000;

    private const int ShortShaLength = 7;

    public static string Compose(RecordedReviewVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        // The approval's own comment already holds its findings; a carried one only says why it still applies.
        if (verdict.CarriedFrom is { } carriedFrom)
        {
            return $"Approval carried to `{ShortSha(verdict.ReviewedCommit)}`: the change is unchanged since the review of `{ShortSha(carriedFrom.ReviewedCommit)}`.";
        }

        ReviewFinding[] blocking = [.. verdict.Findings.Where(finding => finding.Severity == ReviewSeverity.Blocking)];
        ReviewFinding[] advisory = [.. verdict.Findings.Where(finding => finding.Severity == ReviewSeverity.Advisory)];

        // Shed what a reader needs least first: advisory findings, then the collapsed evidence and tests, then blocking
        // findings from the end of the list. The alert and the criteria-not-met list are always kept.
        string comment = Render(verdict, blocking, advisory, blocking.Length, advisory.Length, includeDetails: true);
        for (int shown = advisory.Length - 1; comment.Length > MaxLength && shown >= 0; shown--)
        {
            comment = Render(verdict, blocking, advisory, blocking.Length, shown, includeDetails: true);
        }

        if (comment.Length > MaxLength)
        {
            comment = Render(verdict, blocking, advisory, blocking.Length, 0, includeDetails: false);
        }

        for (int shown = blocking.Length - 1; comment.Length > MaxLength && shown >= 0; shown--)
        {
            comment = Render(verdict, blocking, advisory, shown, 0, includeDetails: false);
        }

        return comment;
    }

    private static string Render(
        RecordedReviewVerdict verdict,
        ReviewFinding[] blocking,
        ReviewFinding[] advisory,
        int blockingShown,
        int advisoryShown,
        bool includeDetails)
    {
        StringBuilder body = new();
        AppendAlert(body, verdict, blocking.Length, advisory.Length);

        if (blocking.Length > 0)
        {
            body.Append('\n').Append("**Blocking**\n");
            AppendFindings(body, blocking, blockingShown);
        }

        ReviewCriterionAnswer[] notMet = [.. verdict.Criteria.Where(answer => answer.Status != ReviewCriterionStatus.Met)];
        if (notMet.Length > 0)
        {
            body.Append('\n').Append("**Criteria not met**\n");
            foreach (ReviewCriterionAnswer answer in notMet)
            {
                body.Append("- ").Append(ModelMarkdown.Inline(answer.Id)).Append(": ").Append(StatusText(answer.Status)).Append('\n');
            }
        }

        if (advisory.Length > 0)
        {
            body.Append('\n').Append("<details><summary>Advisory findings (").Append(advisory.Length).Append(")</summary>\n\n");
            AppendFindings(body, advisory, advisoryShown);
            body.Append("\n</details>\n");
        }

        if (includeDetails)
        {
            AppendDetails(body, verdict);
        }
        else
        {
            body.Append('\n').Append("_Criteria evidence and the tests assessment are omitted for length._\n");
        }

        body.Append('\n').Append("_Reviewed ").Append(ModelMarkdown.CodeSpan(ShortSha(verdict.ReviewedCommit))).Append("._");
        return body.ToString();
    }

    // GitHub recognises an alert's tag only when it stands alone on the blockquote's first line.
    private static void AppendAlert(StringBuilder body, RecordedReviewVerdict verdict, int blockingCount, int advisoryCount)
    {
        (string tag, string headline) = verdict.Outcome switch
        {
            ReviewVerdictOutcome.Approved => ("TIP", "Approved"),
            ReviewVerdictOutcome.ApprovedForPerson => ("WARNING", "Approved, a person should check"),
            ReviewVerdictOutcome.ChangesRequested => ("CAUTION", "Changes requested"),
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict.Outcome, "Unknown review outcome."),
        };
        string counts = blockingCount + advisoryCount == 0
            ? "No findings"
            : $"{blockingCount} blocking, {advisoryCount} advisory";

        body.Append("> [!").Append(tag).Append("]\n");
        body.Append("> **").Append(headline).Append("**: ").Append(counts).Append('\n');
        body.Append("> ").Append(ModelMarkdown.Inline(OneLine(verdict.Summary))).Append('\n');
    }

    private static void AppendFindings(StringBuilder body, ReviewFinding[] findings, int shown)
    {
        foreach (ReviewFinding finding in findings.Take(shown))
        {
            body.Append("- ").Append(FindingLine(finding)).Append('\n');
        }

        if (shown < findings.Length)
        {
            body.Append("- _").Append(findings.Length - shown).Append(" more omitted._\n");
        }
    }

    // GitLab renders Markdown inside <details> only when blank lines surround its body.
    private static void AppendDetails(StringBuilder body, RecordedReviewVerdict verdict)
    {
        if (verdict.Criteria.Count > 0)
        {
            body.Append('\n').Append("<details><summary>Criteria evidence</summary>\n\n");
            foreach (ReviewCriterionAnswer answer in verdict.Criteria)
            {
                body.Append("- ").Append(ModelMarkdown.Inline(answer.Id)).Append(" (").Append(StatusText(answer.Status)).Append(')');
                if (!string.IsNullOrWhiteSpace(answer.Evidence))
                {
                    body.Append(": ").Append(ModelMarkdown.Inline(OneLine(answer.Evidence)));
                }

                body.Append('\n');
            }

            body.Append("\n</details>\n");
        }

        body.Append('\n').Append("<details><summary>Tests: ").Append(AssessmentText(verdict.Tests.Assessment)).Append("</summary>\n\n");
        if (!string.IsNullOrWhiteSpace(verdict.Tests.Reason))
        {
            body.Append(ModelMarkdown.Inline(OneLine(verdict.Tests.Reason))).Append('\n');
        }

        body.Append("\n</details>\n");
    }

    /// <summary>A finding as one line: its location, its body, and its category with the rule it cites.</summary>
    /// <remarks>
    /// A convention citation shows only its file: the verbatim quote is uncapped and may span lines, and stays on the
    /// run's recorded verdict.
    /// </remarks>
    private static string FindingLine(ReviewFinding finding)
    {
        string location = finding.Line is { } line ? $"{finding.Path}:{line}" : finding.Path;
        string category = ReviewVerdict.CategoryTokens.First(pair => pair.Value == finding.Category).Key;
        string? rule = finding.Rule switch
        {
            { Principle: { Length: > 0 } principle } => principle,
            { File: { Length: > 0 } file } => file,
            _ => null,
        };
        string suffix = rule is null ? category : $"{category} · {ModelMarkdown.Inline(OneLine(rule))}";

        return $"{ModelMarkdown.CodeSpan(OneLine(location))}: {ModelMarkdown.Inline(OneLine(finding.Body))} ({suffix})";
    }

    private static string StatusText(ReviewCriterionStatus status) => status switch
    {
        ReviewCriterionStatus.Met => "met",
        ReviewCriterionStatus.Unmet => "unmet",
        ReviewCriterionStatus.NotVerifiable => "not verifiable",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown criterion status."),
    };

    private static string AssessmentText(ReviewTestsAssessment assessment) => assessment switch
    {
        ReviewTestsAssessment.Adequate => "adequate",
        ReviewTestsAssessment.Missing => "missing",
        ReviewTestsAssessment.NotNeeded => "not needed",
        _ => throw new ArgumentOutOfRangeException(nameof(assessment), assessment, "Unknown tests assessment."),
    };

    private static string OneLine(string text) => text.Trim().ReplaceLineEndings(" ");

    private static string ShortSha(string commit) => commit.Length > ShortShaLength ? commit[..ShortShaLength] : commit;
}
