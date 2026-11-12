using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.TriageVerdictTests;

public sealed class TryParse
{
    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    [Theory]
    [InlineData("feature", nameof(TriageIssueKind.Feature))]
    [InlineData("bug", nameof(TriageIssueKind.Bug))]
    [InlineData("refactor", nameof(TriageIssueKind.Refactor))]
    [InlineData("documentation", nameof(TriageIssueKind.Documentation))]
    public void WhenOutcomeIsReady_ParsesAReadyVerdictWithItsKind(string kindToken, string expected)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json($$"""{"outcome":"ready","kind":"{{kindToken}}"}"""));

        // Assert
        verdict.ShouldBe(new TriageVerdict.Ready(Enum.Parse<TriageIssueKind>(expected)));
    }

    [Fact]
    public void WhenOutcomeIsNeedsInputWithQuestions_ParsesTheKindAndQuestions()
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(
            """{"outcome":"needs_input","kind":"bug","questions":[{"question":"Which endpoint?","context":"Two match.","recommendation":"The v2 one."}]}"""));

        // Assert
        TriageVerdict.NeedsInput needsInput = verdict.ShouldBeOfType<TriageVerdict.NeedsInput>();
        needsInput.ShouldSatisfyAllConditions(
            () => needsInput.Kind.ShouldBe(TriageIssueKind.Bug),
            () => needsInput.Questions.ShouldBe([new RunVerdictQuestion("Which endpoint?", "Two match.", "The v2 one.")]));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"outcome":"ready"}""")]
    [InlineData("""{"kind":"feature"}""")]
    [InlineData("""{"outcome":"done","kind":"feature"}""")]
    [InlineData("""{"outcome":"too_big","kind":"feature"}""")]
    [InlineData("""{"outcome":"ready","kind":"chore"}""")]
    [InlineData("""{"outcome":"ready","kind":"Feature"}""")]
    [InlineData("""{"outcome":"ready","kind":1}""")]
    [InlineData("""{"outcome":1,"kind":"feature"}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature"}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[]}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[{"question":"","context":"x"}]}""")]
    [InlineData("""{"outcome":"needs_input","kind":"feature","questions":[{"question":"Which?"}]}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","questions":[]}""")]
    [InlineData("""{"outcome":"ready","kind":"feature","summary":"extra property"}""")]
    public void WhenTheOutputBreaksTheSchema_ReturnsNull(string json)
    {
        // Act
        TriageVerdict? verdict = TriageVerdict.TryParse(Json(json));

        // Assert
        verdict.ShouldBeNull();
    }
}
