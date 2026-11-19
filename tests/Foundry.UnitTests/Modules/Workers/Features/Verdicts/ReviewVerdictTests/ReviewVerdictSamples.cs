using Foundry.Shared;
using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictTests;

/// <summary>Builds review verdict JSON and run facts for the review verdict tests.</summary>
internal static class ReviewVerdictSamples
{
    public static readonly HashSet<string> PrincipleIds = ["design.1", "code.5", "testing.1"];

    public static readonly string[] Categories =
        ["correctness", "criteria", "tests", "security", "quality", "design", "conventions"];

    public static JsonObject Verdict(
        JsonArray? findings = null,
        Dictionary<string, string>? dimensionStatuses = null,
        JsonArray? criteria = null,
        string assessment = "adequate",
        JsonArray? coveringTests = null)
    {
        JsonObject dimensions = [];

        foreach (string category in Categories)
        {
            string status = dimensionStatuses is not null && dimensionStatuses.TryGetValue(category, out string? given)
                ? given
                : "clear";
            dimensions[category] = new JsonObject { ["status"] = status, ["note"] = "Checked." };
        }

        return new JsonObject
        {
            ["summary"] = "Adds the thing.",
            ["findings"] = findings ?? [],
            ["dimensions"] = dimensions,
            ["criteria"] = criteria ?? [Criterion("AC1", "met")],
            ["tests"] = new JsonObject
            {
                ["assessment"] = assessment,
                ["reason"] = "Covered.",
                ["coveringTests"] = coveringTests ?? new JsonArray("tests/a_test.go"),
            },
        };
    }

    public static JsonObject Finding(
        string category,
        string severity = "blocking",
        JsonObject? rule = null)
    {
        JsonObject finding = new()
        {
            ["path"] = "src/a.cs",
            ["line"] = 3,
            ["severity"] = severity,
            ["category"] = category,
            ["body"] = "Wrong.",
        };

        if (rule is not null)
        {
            finding["rule"] = rule;
        }

        return finding;
    }

    public static JsonObject Criterion(string id, string status) =>
        new() { ["id"] = id, ["status"] = status, ["evidence"] = "See tests." };

    public static SuppliedReviewFacts Facts(int criteriaCount = 1, bool productionWithoutTests = false)
    {
        List<ChangedPath> paths = [new ChangedPath(ChangeStatus.Modified, "src/a.cs")];

        if (!productionWithoutTests)
        {
            paths.Add(new ChangedPath(ChangeStatus.Modified, "tests/a_test.go"));
        }

        string body = "## Acceptance Criteria\n\n"
            + string.Concat(Enumerable.Range(1, criteriaCount).Select(n => $"- Criterion {n}.\n"));

        return SuppliedReviewFacts.Compute(
            PullRequestUrl.From("https://github.com/acme/app/pull/7"),
            CommitSha.From("0123456789abcdef0123456789abcdef01234567"),
            new ReviewPacketFacts(paths, PatchId.From("89abcdef0123456789abcdef0123456789abcdef")),
            criteriaCount == 0 ? null : body);
    }
}
