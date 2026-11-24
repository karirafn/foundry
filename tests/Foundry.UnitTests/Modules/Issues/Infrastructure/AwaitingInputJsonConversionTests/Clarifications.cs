using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.AwaitingInputJsonConversionTests;

public sealed class Clarifications
{
    private static readonly DateTimeOffset AskedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private static string Serialize(IReadOnlyList<Clarification> log) =>
        (string)AwaitingInputJsonConversion.ClarificationsConverter.ConvertToProvider(log)!;

    private static IReadOnlyList<Clarification> Deserialize(string json) =>
        (IReadOnlyList<Clarification>)AwaitingInputJsonConversion.ClarificationsConverter.ConvertFromProvider(json)!;

    [Fact]
    public void AQuestionWithARecommendation_RoundTripsItsRecommendation()
    {
        // Arrange
        IReadOnlyList<Clarification> log =
            [Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", "Postgres.")], AskedAt)];

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Serialize(log));

        // Assert
        read.ShouldHaveSingleItem().ShouldBeOfType<Clarification.Asked>().Questions.ShouldBe([new InputQuestion.Legacy("Which database?", "Two are configured.", "Postgres.")]);
    }

    [Fact]
    public void AStructuredQuestion_RoundTripsEveryField()
    {
        // Arrange
        InputQuestion question = StructuredQuestionBuilder.Build(
            options: ["SQLite", "Postgres", "Both"],
            recommendedOption: "a",
            evidence: ["src/Db.cs:12 opens SQLite", "appsettings.json:4 names Postgres"]);
        IReadOnlyList<Clarification> log = [Clarification.Ask([question], AskedAt)];

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Serialize(log));

        // Assert
        read.ShouldHaveSingleItem().ShouldBeOfType<Clarification.Asked>().Questions.ShouldBe([question]);
    }

    // The shape the clarification log held before questions were structured, as the converter wrote it.
    [Fact]
    public void AQuestionStoredBeforeQuestionsWereStructured_ReadsAsLegacy()
    {
        // Arrange
        const string Stored =
            """[{"questions":[{"question":"Which database?","context":"Two are configured.","recommendation":"Postgres."}],"input":null,"askedAt":"2026-11-11T10:00:00+00:00","answeredAt":null}]""";

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Stored);

        // Assert
        read.ShouldHaveSingleItem().ShouldBeOfType<Clarification.Asked>().Questions.ShouldBe([new InputQuestion.Legacy("Which database?", "Two are configured.", "Postgres.")]);
    }

    [Fact]
    public void AQuestionStoredWithoutARecommendation_ReadsANullRecommendation()
    {
        // Arrange
        const string Stored =
            """[{"questions":[{"question":"Which database?","context":"Two are configured."}],"input":null,"askedAt":"2026-11-11T10:00:00+00:00","answeredAt":null}]""";

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Stored);

        // Assert
        read.ShouldHaveSingleItem().ShouldBeOfType<Clarification.Asked>().Questions.ShouldBe([new InputQuestion.Legacy("Which database?", "Two are configured.", null)]);
    }

    [Fact]
    public void AnAnsweredRound_WritesItsInputAsAnArrayOfKindedParts()
    {
        // Arrange
        IReadOnlyList<Clarification> log =
        [
            Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)
                .Answer(ClarificationInput.FromComments("Use Postgres."), AskedAt.AddHours(1)),
        ];

        // Act
        string json = Serialize(log);

        // Assert
        json.ShouldContain("""
            "input":[{"kind":"comments","text":"Use Postgres."}]
            """.Trim());
    }

    [Fact]
    public void AnAnsweredRound_RoundTripsItsInput()
    {
        // Arrange
        IReadOnlyList<Clarification> log =
        [
            Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)
                .Answer(ClarificationInput.FromComments("Use Postgres."), AskedAt.AddHours(1)),
        ];

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Serialize(log));

        // Assert
        read.ShouldBe(log);
    }

    [Fact]
    public void ARoundAnsweredByCommentsAndAnEdit_WritesTheEditAsItsKindAlone()
    {
        // Arrange
        IReadOnlyList<Clarification> log =
        [
            Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)
                .Answer(
                    new ClarificationInput([new InputPart.Comments("See the description."), new InputPart.DescriptionEdited()]),
                    AskedAt.AddHours(1)),
        ];

        // Act
        string json = Serialize(log);

        // Assert
        json.ShouldContain("""
            "input":[{"kind":"comments","text":"See the description."},{"kind":"descriptionEdited"}]
            """.Trim());
    }

    [Fact]
    public void ARoundAnsweredByAnEdit_RoundTripsItsInput()
    {
        // Arrange
        IReadOnlyList<Clarification> log =
        [
            Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)
                .Answer(new ClarificationInput([new InputPart.DescriptionEdited()]), AskedAt.AddHours(1)),
        ];

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Serialize(log));

        // Assert
        read.ShouldBe(log);
    }

    [Fact]
    public void ARoundContinuedWithoutAnswer_WritesItsPartAsItsKindAlone()
    {
        // Arrange
        IReadOnlyList<Clarification> log =
        [
            Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)
                .Answer(new ClarificationInput([new InputPart.ContinuedWithoutAnswer()]), AskedAt.AddDays(3)),
        ];

        // Act
        string json = Serialize(log);

        // Assert
        json.ShouldContain("""
            "input":[{"kind":"continuedWithoutAnswer"}]
            """.Trim());
    }

    [Fact]
    public void ARoundContinuedWithoutAnswer_RoundTripsItsInput()
    {
        // Arrange
        IReadOnlyList<Clarification> log =
        [
            Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)
                .Answer(new ClarificationInput([new InputPart.ContinuedWithoutAnswer()]), AskedAt.AddDays(3)),
        ];

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Serialize(log));

        // Assert
        read.ShouldBe(log);
    }

    [Fact]
    public void ARoundAnsweredByAPush_WritesItsShas()
    {
        // Arrange
        IReadOnlyList<Clarification> log =
        [
            Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)
                .Answer(new ClarificationInput([new InputPart.BranchUpdated("aaa111", "bbb222")]), AskedAt.AddHours(1)),
        ];

        // Act
        string json = Serialize(log);

        // Assert
        json.ShouldContain("""
            "input":[{"kind":"branchUpdated","fromSha":"aaa111","toSha":"bbb222"}]
            """.Trim());
    }

    [Theory]
    [InlineData("aaa111")]
    [InlineData(null)]
    public void ARoundAnsweredByAPush_RoundTripsItsInput(string? fromSha)
    {
        // Arrange
        IReadOnlyList<Clarification> log =
        [
            Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)
                .Answer(
                    new ClarificationInput([new InputPart.Comments("Pushed it."), new InputPart.BranchUpdated(fromSha, "bbb222")]),
                    AskedAt.AddHours(1)),
        ];

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Serialize(log));

        // Assert
        read.ShouldBe(log);
    }

    [Fact]
    public void AnOpenRound_WritesANullInput()
    {
        // Arrange
        IReadOnlyList<Clarification> log =
            [Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)];

        // Act
        string json = Serialize(log);

        // Assert
        json.ShouldContain("\"input\":null");
    }

    [Fact]
    public void ARoundStoredWithAPlainStringInput_ReadsItAsOneCommentsPart()
    {
        // Arrange
        const string Stored =
            """[{"questions":[{"question":"Which database?","context":"Two are configured."}],"input":"Use Postgres.","askedAt":"2026-11-11T10:00:00+00:00","answeredAt":"2026-11-11T11:00:00+00:00"}]""";

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Stored);

        // Assert
        read.ShouldHaveSingleItem().ShouldBeOfType<Clarification.Asked>().Input.ShouldBe(ClarificationInput.FromComments("Use Postgres."));
    }

    [Fact]
    public void ARoundStoredWithANullInput_ReadsAsOpen()
    {
        // Arrange
        const string Stored =
            """[{"questions":[{"question":"Which database?","context":"Two are configured."}],"input":null,"askedAt":"2026-11-11T10:00:00+00:00","answeredAt":null}]""";

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Stored);

        // Assert
        read.ShouldHaveSingleItem().ShouldBeOfType<Clarification.Asked>().ShouldSatisfyAllConditions(
            round => round.IsOpen.ShouldBeTrue(),
            round => round.Input.ShouldBeNull());
    }
}
