namespace Foundry.Modules.Monitoring.Features;

internal static class LabelClassifier
{
    private const string FeatureLabel = "feature";
    private const string BugLabel = "bug";
    private const string RefactorLabel = "refactor";
    private const string DocumentationLabel = "documentation";

    private static readonly string[] PriorityOrder =
    [
        BugLabel,
        FeatureLabel,
        RefactorLabel,
        DocumentationLabel,
    ];

    public static string ClassifyKind(IReadOnlyList<string> labels)
    {
        HashSet<string> normalizedLabels = labels
            .Select(l => l.ToLowerInvariant())
            .ToHashSet();

        foreach (string kind in PriorityOrder)
        {
            if (normalizedLabels.Contains(kind))
            {
                return kind;
            }
        }

        return FeatureLabel;
    }
}
