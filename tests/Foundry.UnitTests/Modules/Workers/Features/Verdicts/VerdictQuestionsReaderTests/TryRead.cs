using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Verdicts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Verdicts.VerdictQuestionsReaderTests;

public sealed class TryRead
{
    private const string ValidQuestion =
        """
        {"question":"Which database?","problem":"Both are named.","impact":"The schema differs.",
         "options":["SQLite","Postgres"],"recommendation":{"option":"b","reason":"Production runs it."},
         "evidence":["src/Db.cs:12 opens SQLite"]}
        """;

    [Fact]
    public void WhenTheQuestionIsStructured_ReadsEveryField()
    {
        // Act
        IReadOnlyList<InputQuestion.Structured>? questions = VerdictQuestionsReader.TryRead(JsonNode.Parse($"[{ValidQuestion}]"));

        // Assert
        InputQuestion.Structured question = questions.ShouldHaveSingleItem();
        question.Question.ShouldBe("Which database?");
        question.Problem.ShouldBe("Both are named.");
        question.Impact.ShouldBe("The schema differs.");
        question.Options.ShouldBe(["SQLite", "Postgres"]);
        question.RecommendedOption.ShouldBe("b");
        question.RecommendationReason.ShouldBe("Production runs it.");
        question.Evidence.ShouldBe(["src/Db.cs:12 opens SQLite"]);
    }

    [Fact]
    public void WhenEvidenceIsEmpty_ReadsTheQuestion()
    {
        // Act
        IReadOnlyList<InputQuestion.Structured>? questions = VerdictQuestionsReader.TryRead(JsonNode.Parse(
            """[{"question":"Q?","problem":"P","impact":"I","options":["x","y"],"recommendation":{"option":"a","reason":"R"},"evidence":[]}]"""));

        // Assert
        questions.ShouldHaveSingleItem().Evidence.ShouldBeEmpty();
    }

    [Fact]
    public void WhenTheRecommendedLetterIsBeyondTheOptions_RejectsTheQuestions()
    {
        // Act
        IReadOnlyList<InputQuestion.Structured>? questions = VerdictQuestionsReader.TryRead(JsonNode.Parse(
            """[{"question":"Q?","problem":"P","impact":"I","options":["x","y"],"recommendation":{"option":"c","reason":"R"},"evidence":[]}]"""));

        // Assert
        questions.ShouldBeNull();
    }

    [Theory]
    [InlineData("""[{"question":"Which database?","context":"Both are named.","recommendation":"Postgres."}]""")]
    [InlineData("""[{"question":"Q?","problem":"P","impact":"I","options":["x"],"recommendation":{"option":"a","reason":"R"},"evidence":[]}]""")]
    [InlineData("""[{"question":"Q?","problem":"P","impact":"I","options":["x","y"],"recommendation":"a","evidence":[]}]""")]
    [InlineData("""[{"question":"Q?","problem":"P","impact":"I","options":["x","y"],"recommendation":{"option":"a"},"evidence":[]}]""")]
    [InlineData("""[{"question":"Q?","problem":"P","impact":"I","options":["x","y"],"recommendation":{"option":"a","reason":"R"}}]""")]
    [InlineData("""[{"question":"Q?","problem":"P","impact":"I","options":["x",1],"recommendation":{"option":"a","reason":"R"},"evidence":[]}]""")]
    [InlineData("""[{"question":"","problem":"P","impact":"I","options":["x","y"],"recommendation":{"option":"a","reason":"R"},"evidence":[]}]""")]
    [InlineData("""[]""")]
    public void WhenAQuestionBreaksTheDefinition_RejectsTheQuestions(string json)
    {
        // Act
        IReadOnlyList<InputQuestion.Structured>? questions = VerdictQuestionsReader.TryRead(JsonNode.Parse(json));

        // Assert
        questions.ShouldBeNull();
    }

    [Theory]
    [InlineData("""[{"question":"Q?","problem":"P","impact":"I","options":["x","y"],"recommendation":{"option":"a","reason":"R"},"evidence":[],"extra":1}]""")]
    [InlineData("""[{"question":"Q?","problem":"P","impact":"I","options":["x","y"],"recommendation":{"option":"a","reason":"R","extra":1},"evidence":[]}]""")]
    public void WhenAQuestionCarriesAnExtraProperty_RejectsTheQuestions(string json)
    {
        // Act
        IReadOnlyList<InputQuestion.Structured>? questions = VerdictQuestionsReader.TryRead(JsonNode.Parse(json));

        // Assert
        questions.ShouldBeNull();
    }
}
