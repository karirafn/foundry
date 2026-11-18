using System.Text.Json.Nodes;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.ReviewVerdictParserTests;

/// <summary>Review verdicts shaped like <c>workers/roles/review/verdict.schema.json</c>, for tests to adjust.</summary>
internal static class ReviewVerdictSamples
{
    public static readonly string[] Categories = ["correctness", "criteria", "tests", "security", "quality", "design", "conventions"];

    /// <summary>A verdict with no findings, every dimension clear, the given criteria met and adequate tests.</summary>
    public static JsonObject Valid(params string[] criterionIds)
    {
        JsonObject dimensions = [];

        foreach (string category in Categories)
        {
            dimensions[category] = new JsonObject { ["status"] = "clear", ["note"] = $"Checked {category}." };
        }

        return new JsonObject
        {
            ["summary"] = "Adds retry to webhook delivery.",
            ["findings"] = new JsonArray(),
            ["dimensions"] = dimensions,
            ["criteria"] = new JsonArray([.. criterionIds.Select(id => (JsonNode)new JsonObject
            {
                ["id"] = id,
                ["status"] = "met",
                ["evidence"] = "Covered by RetryTests.",
            })]),
            ["tests"] = new JsonObject
            {
                ["assessment"] = "adequate",
                ["reason"] = "Retry is tested.",
                ["coveringTests"] = new JsonArray("tests/App.Tests/RetryTests.cs"),
            },
        };
    }

    public static JsonObject Finding(string category, string severity, JsonObject? rule = null)
    {
        JsonObject finding = new()
        {
            ["path"] = "src/App/Retry.cs",
            ["line"] = 12,
            ["severity"] = severity,
            ["category"] = category,
            ["body"] = "The retry never stops.",
        };

        if (rule is not null)
        {
            finding["rule"] = rule;
        }

        return finding;
    }

    /// <summary>Adds <paramref name="finding"/> and marks its category's dimension as having findings.</summary>
    public static JsonObject WithFinding(this JsonObject verdict, JsonObject finding)
    {
        verdict["findings"]!.AsArray().Add(finding);
        verdict["dimensions"]![finding["category"]!.GetValue<string>()]!["status"] = "findings";
        return verdict;
    }

    public static JsonObject WithCriterion(this JsonObject verdict, string id, string status)
    {
        foreach (JsonNode? criterion in verdict["criteria"]!.AsArray())
        {
            if (criterion!["id"]!.GetValue<string>() == id)
            {
                criterion["status"] = status;
            }
        }

        return verdict;
    }

    public static JsonObject WithTests(this JsonObject verdict, string assessment, params string[] coveringTests)
    {
        verdict["tests"]!["assessment"] = assessment;
        verdict["tests"]!["coveringTests"] = new JsonArray([.. coveringTests.Select(path => (JsonNode)JsonValue.Create(path))]);
        return verdict;
    }
}
