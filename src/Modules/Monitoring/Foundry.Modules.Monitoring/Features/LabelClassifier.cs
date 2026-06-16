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
        foreach (string kind in PriorityOrder)
        {
            if (labels.Any(l => string.Equals(l, kind, StringComparison.OrdinalIgnoreCase)))
            {
                return kind;
            }
        }

        return FeatureLabel;
    }
}
