using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.ClarificationTests;

public sealed class RoundKinds
{
    private static readonly DateTimeOffset At = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ADecidedRound_IsNeverOpen()
    {
        // Act
        Clarification.Decided decided = Clarification.Decide([TriageDecisionBuilder.Build()], At);

        // Assert
        decided.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void ADecidedRound_NeedsAtLeastOneDecision()
    {
        // Act
        ArgumentException thrown = Should.Throw<ArgumentException>(() => Clarification.Decide([], At));

        // Assert
        thrown.ParamName.ShouldBe("decisions");
    }

    [Fact]
    public void ARequestedRound_IsNeverOpenAndCarriesNoDecisions()
    {
        // Act
        Clarification.Requested requested = Clarification.Request([new ReviewComment("Use a date picker.")], 0, At);

        // Assert
        requested.ShouldSatisfyAllConditions(
            () => requested.IsOpen.ShouldBeFalse(),
            () => requested.Decisions.ShouldBeEmpty());
    }

    [Fact]
    public void ARequestedRound_RejectsANegativeOmittedCount()
    {
        // Act
        ArgumentOutOfRangeException thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => Clarification.Request([new ReviewComment("Use a date picker.")], -1, At));

        // Assert
        thrown.ParamName.ShouldBe("omittedCommentCount");
    }

    [Fact]
    public void RequestedRoundsWithDifferentComments_AreNotEqual()
    {
        // Arrange
        Clarification first = Clarification.Request([new ReviewComment("Use a date picker.", "src/Form.cs", 12)], 0, At);
        Clarification second = Clarification.Request([new ReviewComment("Use a date picker.", "src/Form.cs", 13)], 0, At);

        // Act
        bool equal = first.Equals(second);

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void AnsweringAnAskedRound_KeepsItsDecisions()
    {
        // Arrange
        TriageDecision decision = TriageDecisionBuilder.Build();
        Clarification.Asked asked = Clarification.Ask([StructuredQuestionBuilder.Build()], [decision], At);

        // Act
        Clarification.Asked answered = asked.Answer(ClarificationInput.ContinuedWithoutAnswer(), At.AddHours(1));

        // Assert
        answered.ShouldSatisfyAllConditions(
            () => answered.IsOpen.ShouldBeFalse(),
            () => answered.Decisions.ShouldBe([decision]));
    }

    [Fact]
    public void RoundsOfDifferentKindsWithTheSameDecisions_AreNotEqual()
    {
        // Arrange
        TriageDecision decision = TriageDecisionBuilder.Build();
        Clarification asked = Clarification.Ask([StructuredQuestionBuilder.Build()], [decision], At);
        Clarification decided = Clarification.Decide([decision], At);

        // Act
        bool equal = asked.Equals(decided);

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void EachRoundKind_RoundTripsThroughJson()
    {
        // Arrange
        IReadOnlyList<Clarification> log =
        [
            Clarification.Ask([StructuredQuestionBuilder.Build()], [TriageDecisionBuilder.Build()], At)
                .Answer(ClarificationInput.FromComments("ok"), At.AddHours(1)),
            Clarification.Decide([TriageDecisionBuilder.Build(ground: TriageDecisionGround.ImplementationDetail)], At.AddHours(2)),
            Clarification.Request([new ReviewComment("Use a date picker.", "src/Form.cs", 12), new ReviewComment("Thanks.")], 3, At.AddHours(3)),
        ];

        // Act
        IReadOnlyList<Clarification>? read = JsonSerializer.Deserialize<IReadOnlyList<Clarification>>(JsonSerializer.Serialize(log));

        // Assert
        read.ShouldBe(log);
    }

    // The wire shape an integration event carried before rounds had kinds.
    [Fact]
    public void ARoundWrittenBeforeRoundsHadKinds_ReadsAsAnAskedRoundWithNoDecisions()
    {
        // Arrange
        Clarification.Asked expected = Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", "Postgres.")], At);
        const string Json =
            """{"Questions":[{"$type":"legacy","Question":"Which database?","Context":"Two are configured.","Recommendation":"Postgres."}],"Input":null,"AskedAt":"2026-11-11T10:00:00+00:00","AnsweredAt":null}""";

        // Act
        Clarification? read = JsonSerializer.Deserialize<Clarification>(Json);

        // Assert
        read.ShouldBe(expected);
    }
}
