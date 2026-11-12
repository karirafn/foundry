using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.VerdictQuestionsReaderTests;

public sealed class TryRead
{
    [Fact]
    public void WhenEveryQuestionCarriesARecommendation_ReadsTheRecommendations()
    {
        // Act
        IReadOnlyList<RunVerdictQuestion>? questions = VerdictQuestionsReader.TryRead(JsonNode.Parse(
            """[{"question":"Which database?","context":"Both are named.","recommendation":"Postgres, since production runs it."}]"""));

        // Assert
        questions.ShouldBe([new RunVerdictQuestion("Which database?", "Both are named.", "Postgres, since production runs it.")]);
    }

    [Theory]
    [InlineData("""[{"question":"Which database?","context":"Both are named."}]""")]
    [InlineData("""[{"question":"Which database?","context":"Both are named.","recommendation":""}]""")]
    [InlineData("""[{"question":"Which database?","context":"Both are named.","recommendation":null}]""")]
    [InlineData("""[{"question":"Which database?","context":"Both are named.","recommendation":1}]""")]
    public void WhenAQuestionHasNoRecommendationOrABlankOne_RejectsTheQuestions(string json)
    {
        // Act
        IReadOnlyList<RunVerdictQuestion>? questions = VerdictQuestionsReader.TryRead(JsonNode.Parse(json));

        // Assert
        questions.ShouldBeNull();
    }

    [Fact]
    public void WhenAQuestionCarriesAnExtraProperty_RejectsTheQuestions()
    {
        // Act
        IReadOnlyList<RunVerdictQuestion>? questions = VerdictQuestionsReader.TryRead(JsonNode.Parse(
            """[{"question":"Which?","context":"x","recommendation":"This one.","extra":1}]"""));

        // Assert
        questions.ShouldBeNull();
    }
}
