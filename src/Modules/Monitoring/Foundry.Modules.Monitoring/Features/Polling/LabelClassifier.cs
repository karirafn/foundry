namespace Foundry.Modules.Monitoring.Features.Polling;

internal static class LabelClassifier
{
    private const string FeatureLabel = "feature";
    private const string BugLabel = "bug";
    private const string RefactorLabel = "refactor";
    private const string DocumentationLabel = "documentation";
    private const string ScopedLabelPrefix = "type::";

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
            if (labels.Any(l => MatchesKind(l, kind)))
            {
                return kind;
            }
        }

        return FeatureLabel;
    }

    private static bool MatchesKind(string label, string kind)
    {
        if (string.Equals(label, kind, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (label.StartsWith(ScopedLabelPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string stripped = label[ScopedLabelPrefix.Length..];
            return string.Equals(stripped, kind, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
