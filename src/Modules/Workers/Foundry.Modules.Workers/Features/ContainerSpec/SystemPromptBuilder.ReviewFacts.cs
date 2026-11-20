using System.Globalization;
using System.Text;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.ContainerSpec;

internal static partial class SystemPromptBuilder
{
    /// <summary>The most changed paths the review-facts table lists; the flag and the verdict checks use every path.</summary>
    internal const int MaxReviewPathRows = 500;

    internal const string NoAcceptanceCriteriaText = "No acceptance criteria were found in the issue. Send an empty `criteria` list.";

    internal const string CoveredByChecksAvailableText =
        "You may answer a criterion `covered_by_checks` when its whole claim is that a build, test or CI run passes.";

    internal const string CoveredByChecksUnavailableText =
        "`covered_by_checks` is not available: answer every criterion from the diff.";

    /// <summary>
    /// Renders the facts code computed for a review run (ADR 0114): each acceptance criterion as <c>AC&lt;n&gt;: text</c>,
    /// the checks gate's result, the production-without-tests flag, and a table of the classified changed paths capped
    /// at <see cref="MaxReviewPathRows"/> rows behind an omission note. Criterion text and paths come from the issue and
    /// the repository, so they are XML-encoded like the issue content.
    /// </summary>
    internal static string RenderReviewFacts(SuppliedReviewFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        StringBuilder sb = new();
        sb.Append("## Review facts\n\n");
        sb.Append("Foundry computed these facts from the pull request and the issue. Your verdict is checked against them.\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"Pull request: {EncodeForXmlData(facts.PullRequestUrl.Value)}\n");
        sb.Append(CultureInfo.InvariantCulture, $"Reviewed commit: {facts.ReviewedCommit.Value}\n\n");

        sb.Append("### Acceptance criteria\n\n");

        if (facts.Criteria.Count == 0)
        {
            sb.Append(NoAcceptanceCriteriaText).Append("\n\n");
        }
        else
        {
            sb.Append("Answer every criterion in `criteria` by its id, and no other id.\n\n<acceptance-criteria>\n");

            foreach (AcceptanceCriterion criterion in facts.Criteria)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{criterion.Id}: {EncodeForXmlData(criterion.Text)}\n");
            }

            sb.Append("</acceptance-criteria>\n\n");
        }

        sb.Append("### Checks gate\n\n");
        sb.Append(facts.ChecksPassed
            ? "Passed: every check on the reviewed commit passed. " + CoveredByChecksAvailableText + "\n\n"
            : "No checks ran on this pull request. " + CoveredByChecksUnavailableText + "\n\n");

        sb.Append("### Tests\n\n");
        sb.Append(facts.ProductionChangedWithoutTests
            ? "Production changed, no test changed: yes. The change adds or modifies production code and adds or modifies no test. "
                + "If you judge the tests `adequate`, name in `coveringTests` the test files that already cover the change; "
                + "each must be a test path.\n\n"
            : "Production changed, no test changed: no.\n\n");

        sb.Append("### Changed paths\n\n");

        if (facts.ClassifiedPaths.Count == 0)
        {
            sb.Append("The change has no changed paths.");
            return sb.ToString();
        }

        sb.Append("| Status | Class | Path |\n| --- | --- | --- |\n");

        foreach (ClassifiedPath path in facts.ClassifiedPaths.Take(MaxReviewPathRows))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"| {StatusLetter(path.Path.Status)} | {ClassToken(path.Class)} | {EncodeForXmlData(path.Path.Path).Replace("|", "\\|", StringComparison.Ordinal)} |\n");
        }

        int omitted = facts.ClassifiedPaths.Count - MaxReviewPathRows;

        if (omitted > 0)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"\nNote: {omitted} more changed path(s) were omitted from this table. The flag above counts every changed path.\n");
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static string StatusLetter(ChangeStatus status) => status switch
    {
        ChangeStatus.Added => "A",
        ChangeStatus.Modified => "M",
        ChangeStatus.Deleted => "D",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown change status."),
    };

    private static string ClassToken(ChangedFileClass fileClass) => fileClass switch
    {
        ChangedFileClass.Production => "production",
        ChangedFileClass.Test => "test",
        ChangedFileClass.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(fileClass), fileClass, "Unknown file class."),
    };
}
