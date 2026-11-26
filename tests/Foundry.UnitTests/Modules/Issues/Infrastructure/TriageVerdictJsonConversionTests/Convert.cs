using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.TriageVerdictJsonConversionTests;

public sealed class Convert
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private static string Serialize(TriageVerdict verdict) =>
        (string)TriageVerdictJsonConversion.Converter.ConvertToProvider(verdict)!;

    private static TriageVerdict Deserialize(string json) =>
        (TriageVerdict)TriageVerdictJsonConversion.Converter.ConvertFromProvider(json)!;

    [Fact]
    public void ANeedsInputVerdictWithARecommendation_RoundTripsItsRecommendation()
    {
        // Arrange
        TriageVerdict verdict = new TriageVerdict.NeedsInput(
            IssueKind.Bug,
            [new InputQuestion.Legacy("Which endpoint?", "Two match.", "The v2 one.")],
            DecidedAt);

        // Act
        TriageVerdict read = Deserialize(Serialize(verdict));

        // Assert
        read.ShouldBeOfType<TriageVerdict.NeedsInput>().Questions
            .ShouldBe([new InputQuestion.Legacy("Which endpoint?", "Two match.", "The v2 one.")]);
    }

    [Fact]
    public void ABlockedVerdict_RoundTripsItsBlockers()
    {
        // Arrange
        TriageVerdict verdict = new TriageVerdict.Blocked(
            IssueKind.Feature,
            [new TriageBlocker(15, "Needs the retry queue."), new TriageBlocker(31, "Needs the schema.")],
            DecidedAt);

        // Act
        TriageVerdict read = Deserialize(Serialize(verdict));

        // Assert
        read.ShouldBeOfType<TriageVerdict.Blocked>().BlockedBy
            .ShouldBe([new TriageBlocker(15, "Needs the retry queue."), new TriageBlocker(31, "Needs the schema.")]);
    }

    [Fact]
    public void ABlockedRowNamingExactlyTheBlockerCap_ReadsABlockedVerdict()
    {
        // Arrange
        string blockedBy = string.Join(
            ',',
            Enumerable.Range(1, TriageVerdict.Blocked.MaxBlockers).Select(number => $$"""{"issue":{{number}},"reason":"Needs it."}"""));
        string stored = $$"""{"outcome":"blocked","kind":"bug","blockedBy":[{{blockedBy}}],"decidedAt":"2026-11-11T10:00:00+00:00"}""";

        // Act
        TriageVerdict read = Deserialize(stored);

        // Assert
        read.ShouldBeOfType<TriageVerdict.Blocked>().BlockedBy.Count.ShouldBe(TriageVerdict.Blocked.MaxBlockers);
    }

    [Fact]
    public void ABlockedRowWithAReasonExactlyAtTheCap_ReadsABlockedVerdict()
    {
        // Arrange
        string reason = new('a', TriageVerdict.Blocked.ReasonMaxLength);
        string stored = $$"""{"outcome":"blocked","kind":"bug","blockedBy":[{"issue":15,"reason":"{{reason}}"}],"decidedAt":"2026-11-11T10:00:00+00:00"}""";

        // Act
        TriageVerdict read = Deserialize(stored);

        // Assert
        read.ShouldBeOfType<TriageVerdict.Blocked>().ReasonFor(15).ShouldBe(reason);
    }

    [Fact]
    public void ABlockedVerdict_StoresTheDocumentedShape()
    {
        // Arrange
        TriageVerdict verdict = new TriageVerdict.Blocked(
            IssueKind.Feature,
            [new TriageBlocker(15, "Needs the retry queue.")],
            DecidedAt);

        // Act
        string stored = Serialize(verdict);

        // Assert
        stored.ShouldBe(
            """{"outcome":"blocked","kind":"feature","blockedBy":[{"issue":15,"reason":"Needs the retry queue."}],"decidedAt":"2026-11-11T10:00:00+00:00"}""");
    }

    [Fact]
    public void AReadyVerdictStoredBeforeBlockedExisted_StillReads()
    {
        // Arrange
        const string Stored = """{"outcome":"ready","kind":"bug","decidedAt":"2026-11-11T10:00:00+00:00"}""";

        // Act
        TriageVerdict read = Deserialize(Stored);

        // Assert
        read.ShouldBeOfType<TriageVerdict.Ready>().Kind.ShouldBe(IssueKind.Bug);
    }

    [Theory]
    [InlineData(ImplementDemand.Routine)]
    [InlineData(ImplementDemand.Demanding)]
    public void AReadyVerdict_RoundTripsItsDemandAndReason(ImplementDemand demand)
    {
        // Arrange
        TriageVerdict verdict = new TriageVerdict.Ready(IssueKind.Feature, DecidedAt, demand, "A schema migration.");

        // Act
        TriageVerdict read = Deserialize(Serialize(verdict));

        // Assert
        TriageVerdict.Ready ready = read.ShouldBeOfType<TriageVerdict.Ready>();
        ready.ShouldSatisfyAllConditions(
            () => ready.Demand.ShouldBe(demand),
            () => ready.DemandReason.ShouldBe("A schema migration."));
    }

    [Fact]
    public void ARoutineReadyVerdict_StoresTheDocumentedShape()
    {
        // Arrange
        TriageVerdict verdict = new TriageVerdict.Ready(IssueKind.Bug, DecidedAt, ImplementDemand.Routine, "none");

        // Act
        string stored = Serialize(verdict);

        // Assert
        stored.ShouldBe(
            """{"outcome":"ready","kind":"bug","decidedAt":"2026-11-11T10:00:00+00:00","demand":"routine","demandReason":"none"}""");
    }

    [Fact]
    public void AReadyVerdictWithNoReason_StoresNoReason()
    {
        // Arrange
        TriageVerdict verdict = new TriageVerdict.Ready(IssueKind.Bug, DecidedAt);

        // Act
        string stored = Serialize(verdict);

        // Assert
        stored.ShouldBe("""{"outcome":"ready","kind":"bug","decidedAt":"2026-11-11T10:00:00+00:00","demand":"demanding"}""");
    }

    [Fact]
    public void AReadyVerdictStoredBeforeDemandExisted_ReadsDemandingWithNoReason()
    {
        // Arrange
        const string Stored = """{"outcome":"ready","kind":"bug","decidedAt":"2026-11-11T10:00:00+00:00"}""";

        // Act
        TriageVerdict read = Deserialize(Stored);

        // Assert
        TriageVerdict.Ready ready = read.ShouldBeOfType<TriageVerdict.Ready>();
        ready.ShouldSatisfyAllConditions(
            () => ready.Demand.ShouldBe(ImplementDemand.Demanding),
            () => ready.DemandReason.ShouldBeNull());
    }

    [Theory]
    [InlineData("\"trivial\"")]
    [InlineData("\"Routine\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("{}")]
    public void AReadyVerdictWithADemandItDoesNotRecognise_ReadsDemanding(string demand)
    {
        // Arrange
        string stored = $$"""{"outcome":"ready","kind":"bug","decidedAt":"2026-11-11T10:00:00+00:00","demand":{{demand}}}""";

        // Act
        TriageVerdict read = Deserialize(stored);

        // Assert
        read.ShouldBeOfType<TriageVerdict.Ready>().Demand.ShouldBe(ImplementDemand.Demanding);
    }

    [Theory]
    [InlineData("""{"outcome":"deferred","kind":"bug","decidedAt":"2026-11-11T10:00:00+00:00"}""")]
    [InlineData("""{"outcome":"ready","kind":"chore","decidedAt":"2026-11-11T10:00:00+00:00"}""")]
    [InlineData("""{"outcome":"blocked","kind":"bug","decidedAt":"2026-11-11T10:00:00+00:00"}""")]
    [InlineData("""{"outcome":"blocked","kind":"bug","blockedBy":[],"decidedAt":"2026-11-11T10:00:00+00:00"}""")]
    [InlineData("""{"outcome":"blocked","kind":"bug","blockedBy":[{"issue":15,"reason":"  "}],"decidedAt":"2026-11-11T10:00:00+00:00"}""")]
    [InlineData("""{"outcome":"needs_input","kind":"bug","questions":[],"decidedAt":"2026-11-11T10:00:00+00:00"}""")]
    [InlineData("{not json")]
    [InlineData("null")]
    public void AStoredRowThatCannotFormAVerdict_ReadsNoVerdict(string stored)
    {
        // Arrange
        // (stored row supplied by the theory data)

        // Act
        object? read = TriageVerdictJsonConversion.Converter.ConvertFromProvider(stored);

        // Assert
        read.ShouldBeNull();
    }

    [Fact]
    public void ABlockedRowWithAReasonOverTheCap_ReadsNoVerdict()
    {
        // Arrange
        string stored =
            $$"""{"outcome":"blocked","kind":"bug","blockedBy":[{"issue":15,"reason":"{{new string('a', 501)}}"}],"decidedAt":"2026-11-11T10:00:00+00:00"}""";

        // Act
        object? read = TriageVerdictJsonConversion.Converter.ConvertFromProvider(stored);

        // Assert
        read.ShouldBeNull();
    }

    [Fact]
    public void ABlockedRowNamingMoreBlockersThanAnIssueRecords_ReadsNoVerdict()
    {
        // Arrange
        string blockedBy = string.Join(',', Enumerable.Range(1, 51).Select(number => $$"""{"issue":{{number}},"reason":"Needs it."}"""));
        string stored = $$"""{"outcome":"blocked","kind":"bug","blockedBy":[{{blockedBy}}],"decidedAt":"2026-11-11T10:00:00+00:00"}""";

        // Act
        object? read = TriageVerdictJsonConversion.Converter.ConvertFromProvider(stored);

        // Assert
        read.ShouldBeNull();
    }

    [Fact]
    public void ANeedsInputVerdictWithAStructuredQuestion_RoundTripsEveryField()
    {
        // Arrange
        InputQuestion question = StructuredQuestionBuilder.Build(evidence: []);
        TriageVerdict verdict = new TriageVerdict.NeedsInput(IssueKind.Bug, [question], DecidedAt);

        // Act
        TriageVerdict read = Deserialize(Serialize(verdict));

        // Assert
        read.ShouldBeOfType<TriageVerdict.NeedsInput>().Questions.ShouldBe([question]);
    }

    // The shape triage_verdict held before questions were structured, as the converter wrote it.
    [Fact]
    public void AQuestionStoredBeforeQuestionsWereStructured_ReadsAsLegacy()
    {
        // Arrange
        const string Stored =
            """{"outcome":"needs_input","kind":"bug","questions":[{"question":"Which endpoint?","context":"Two match.","recommendation":"The v2 one."}],"decidedAt":"2026-11-11T10:00:00+00:00"}""";

        // Act
        TriageVerdict read = Deserialize(Stored);

        // Assert
        read.ShouldBeOfType<TriageVerdict.NeedsInput>().Questions
            .ShouldBe([new InputQuestion.Legacy("Which endpoint?", "Two match.", "The v2 one.")]);
    }

    [Fact]
    public void AStructuredQuestionBreakingItsRules_ReadsAsNoVerdict()
    {
        // Arrange
        const string Stored =
            """{"outcome":"needs_input","kind":"bug","questions":[{"$type":"structured","question":"Q?","problem":"P","impact":"I","options":["x","y"],"recommendedOption":"c","recommendationReason":"R","evidence":[]}],"decidedAt":"2026-11-11T10:00:00+00:00"}""";

        // Act
        TriageVerdict? read = (TriageVerdict?)TriageVerdictJsonConversion.Converter.ConvertFromProvider(Stored);

        // Assert
        read.ShouldBeNull();
    }

    [Fact]
    public void AQuestionStoredWithoutARecommendation_ReadsANullRecommendation()
    {
        // Arrange
        const string Stored =
            """{"outcome":"needs_input","kind":"bug","questions":[{"question":"Which endpoint?","context":"Two match."}],"decidedAt":"2026-11-11T10:00:00+00:00"}""";

        // Act
        TriageVerdict read = Deserialize(Stored);

        // Assert
        read.ShouldBeOfType<TriageVerdict.NeedsInput>().Questions
            .ShouldBe([new InputQuestion.Legacy("Which endpoint?", "Two match.", null)]);
    }
}
