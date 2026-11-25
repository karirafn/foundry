using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.AwaitingInputJsonConversionTests;

public sealed class ClarificationRoundKinds
{
    private static readonly DateTimeOffset AskedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private static string Serialize(IReadOnlyList<Clarification> log) =>
        (string)AwaitingInputJsonConversion.ClarificationsConverter.ConvertToProvider(log)!;

    private static IReadOnlyList<Clarification> Deserialize(string json) =>
        (IReadOnlyList<Clarification>)AwaitingInputJsonConversion.ClarificationsConverter.ConvertFromProvider(json)!;

    // The shape of a round stored before rounds had kinds: a pre-structured question and a plain-string input.
    [Fact]
    public void ARoundStoredBeforeRoundsHadKinds_RoundTripsAsAnAskedRoundWithNoDecisions()
    {
        // Arrange
        const string Stored =
            """[{"questions":[{"question":"Which database?","context":"Two are configured.","recommendation":"Postgres."}],"input":"Use Postgres.","askedAt":"2026-11-11T10:00:00+00:00","answeredAt":"2026-11-11T11:00:00+00:00"}]""";
        Clarification expected = new Clarification.Asked(
            [new InputQuestion.Legacy("Which database?", "Two are configured.", "Postgres.")],
            ClarificationInput.FromComments("Use Postgres."),
            AskedAt,
            AskedAt.AddHours(1),
            []);

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Stored);
        IReadOnlyList<Clarification> roundTripped = Deserialize(Serialize(read));

        // Assert
        read.ShouldSatisfyAllConditions(
            () => read.ShouldBe([expected]),
            () => roundTripped.ShouldBe([expected]));
    }

    [Fact]
    public void AnAskedRoundWithDecisions_RoundTripsItsDecisions()
    {
        // Arrange
        TriageDecision decision = TriageDecisionBuilder.Build(
            ground: TriageDecisionGround.ImplementationDetail,
            evidence: ["src/Retry.cs:8 caps at three", "docs/retry.md:2 says so"]);
        IReadOnlyList<Clarification> log = [Clarification.Ask([StructuredQuestionBuilder.Build()], [decision], AskedAt)];

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Serialize(log));

        // Assert
        read.ShouldHaveSingleItem().ShouldBeOfType<Clarification.Asked>().Decisions.ShouldBe([decision]);
    }

    [Fact]
    public void ADecidedRound_RoundTripsItsDecisionsAndTime()
    {
        // Arrange
        Clarification decided = Clarification.Decide([TriageDecisionBuilder.Build()], AskedAt);

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Serialize([decided]));

        // Assert
        read.ShouldBe([decided]);
    }

    [Fact]
    public void ADecidedRound_IsWrittenWithItsKindAndGroundToken()
    {
        // Act
        string json = Serialize([Clarification.Decide([TriageDecisionBuilder.Build()], AskedAt)]);

        // Assert
        json.ShouldSatisfyAllConditions(
            () => json.ShouldContain("\"kind\":\"decided\""),
            () => json.ShouldContain("\"ground\":\"settled_business\""),
            () => json.ShouldNotContain("\"questions\""));
    }

    [Fact]
    public void ARequestedRound_RoundTripsItsCommentsOmittedCountAndTime()
    {
        // Arrange
        Clarification requested = Clarification.Request(
            [new ReviewComment("Date should be a date picker.", "src/Form.cs", 12), new ReviewComment("Looks good otherwise.")],
            4,
            AskedAt);

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Serialize([requested]));

        // Assert
        read.ShouldBe([requested]);
    }

    [Fact]
    public void ARequestedRound_IsWrittenWithItsKindAndNoQuestions()
    {
        // Act
        string json = Serialize([Clarification.Request([new ReviewComment("Use a date picker.")], 0, AskedAt)]);

        // Assert
        json.ShouldSatisfyAllConditions(
            () => json.ShouldContain("\"kind\":\"requested\""),
            () => json.ShouldContain("\"receivedAt\""),
            () => json.ShouldNotContain("\"questions\""));
    }

    // Rounds stored before requested rounds existed keep their meaning when a requested round is written beside them.
    [Fact]
    public void StoredLegacyAskedAndDecidedRounds_RoundTripUnchanged()
    {
        // Arrange
        const string Stored =
            """[{"questions":[{"question":"Which database?","context":"Two are configured.","recommendation":"Postgres."}],"input":"Use Postgres.","askedAt":"2026-11-11T10:00:00+00:00","answeredAt":"2026-11-11T11:00:00+00:00"},{"kind":"asked","questions":[{"question":"Which cap?","context":"None is set.","recommendation":"Three."}],"input":[{"kind":"comments","text":"ok"}],"askedAt":"2026-11-11T12:00:00+00:00","answeredAt":"2026-11-11T13:00:00+00:00"},{"kind":"decided","input":null,"answeredAt":null,"decisions":[{"question":"Which cap?","decision":"Three.","ground":"settled_business","evidence":["src/Retry.cs:8"]}],"decidedAt":"2026-11-11T14:00:00+00:00"}]""";
        IReadOnlyList<Clarification> expected =
        [
            new Clarification.Asked(
                [new InputQuestion.Legacy("Which database?", "Two are configured.", "Postgres.")],
                ClarificationInput.FromComments("Use Postgres."),
                AskedAt,
                AskedAt.AddHours(1),
                []),
            new Clarification.Asked(
                [new InputQuestion.Legacy("Which cap?", "None is set.", "Three.")],
                ClarificationInput.FromComments("ok"),
                AskedAt.AddHours(2),
                AskedAt.AddHours(3),
                []),
            Clarification.Decide(
                [TriageDecisionBuilder.Build(question: "Which cap?", decision: "Three.", evidence: ["src/Retry.cs:8"])],
                AskedAt.AddHours(4)),
        ];

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Stored);
        IReadOnlyList<Clarification> roundTripped = Deserialize(Serialize(
            [.. read, Clarification.Request([new ReviewComment("Use a date picker.")], 0, AskedAt.AddHours(5))]));

        // Assert
        read.ShouldSatisfyAllConditions(
            () => read.ShouldBe(expected),
            () => roundTripped.Take(3).ShouldBe(expected));
    }

    [Fact]
    public void AnAskedRoundWithNoDecisions_WritesNoDecisions()
    {
        // Act
        string json = Serialize([Clarification.Ask([StructuredQuestionBuilder.Build()], AskedAt)]);

        // Assert
        json.ShouldNotContain("\"decisions\"");
    }
}
