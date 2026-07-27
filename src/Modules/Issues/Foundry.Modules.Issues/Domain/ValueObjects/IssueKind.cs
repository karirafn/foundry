namespace Foundry.Modules.Issues.Domain.ValueObjects;

public sealed record IssueKind
{
    public static readonly IssueKind Feature = new("feature", "feat");
    public static readonly IssueKind Bug = new("bug", "fix");
    public static readonly IssueKind Refactor = new("refactor", "refactor");
    public static readonly IssueKind Documentation = new("documentation", "docs");

    public string Value { get; }
    public string BranchPrefix { get; }

    private IssueKind(string value, string branchPrefix)
    {
        Value = value;
        BranchPrefix = branchPrefix;
    }

    public static IssueKind FromLabel(string label)
    {
        return label.ToLowerInvariant() switch
        {
            "feature" => Feature,
            "bug" => Bug,
            "refactor" => Refactor,
            "documentation" => Documentation,
            _ => Feature,
        };
    }

    public override string ToString() => Value;
}
