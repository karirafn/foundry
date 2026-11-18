using System.Globalization;
using System.Text;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.ContainerSpec;

internal static partial class SystemPromptBuilder
{
    // Unique heading that opens the review-facts section (ADR 0114). Tests anchor on this constant.
    internal const string ReviewFactsSectionMarker = "Review facts computed by Foundry:";

    internal const string NoAcceptanceCriteriaNote =
        "No acceptance criteria were found in the issue. Send `criteria` as an empty list.";

    internal const string ProductionWithoutTestsLabel = "Production code changed with no test added or modified:";

    // The table is a reading aid; the flag and the verdict checks always use every path.
    internal const int MaxReviewPathRows = 500;

    private static string ReviewFactsSection(ReviewFacts? facts)
    {
        if (facts is null)
        {
            return string.Empty;
        }

        StringBuilder sb = new();
        sb.Append("\n\n").AppendLine(ReviewFactsSectionMarker);
        sb.AppendLine("Foundry computed these from the pull request and the issue, and checks your verdict against them.");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Reviewed commit: {facts.ReviewedCommit.Value}");
        sb.AppendLine();
        sb.AppendLine("Acceptance criteria (answer every one by its id, and no other id):");

        if (facts.Criteria.Items.Count == 0)
        {
            sb.AppendLine(NoAcceptanceCriteriaNote);
        }

        foreach (AcceptanceCriterion criterion in facts.Criteria.Items)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{criterion.Id}: {EncodeForXmlData(criterion.Text)}");
        }

        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"{ProductionWithoutTestsLabel} {(facts.ProductionChangedWithoutTests ? "yes" : "no")}");

        if (facts.ProductionChangedWithoutTests)
        {
            sb.AppendLine(
                "An `adequate` tests assessment must then list in `coveringTests` the test paths that cover the change; each must be a path Foundry classes as test.");
        }

        sb.AppendLine();
        sb.AppendLine("Changed paths:");
        sb.AppendLine("| Status | Class | Path |");
        sb.AppendLine("| --- | --- | --- |");

        foreach (ClassifiedPath path in facts.ClassifiedPaths.Take(MaxReviewPathRows))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {StatusLetter(path.Status)} | {ClassName(path.Class)} | {TableCell(path.Path)} |");
        }

        int omitted = facts.ClassifiedPaths.Count - MaxReviewPathRows;

        if (omitted > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{omitted} more changed paths are not listed. The diff holds every path.");
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static string TableCell(string value) => EncodeForXmlData(value).Replace("|", "\\|", StringComparison.Ordinal);

    private static string StatusLetter(ChangeStatus status) => status switch
    {
        ChangeStatus.Added => "A",
        ChangeStatus.Modified => "M",
        ChangeStatus.Deleted => "D",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown change status."),
    };

    private static string ClassName(ChangedFileClass changedFileClass) => changedFileClass switch
    {
        ChangedFileClass.Production => "production",
        ChangedFileClass.Test => "test",
        ChangedFileClass.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(changedFileClass), changedFileClass, "Unknown changed-file class."),
    };
}
