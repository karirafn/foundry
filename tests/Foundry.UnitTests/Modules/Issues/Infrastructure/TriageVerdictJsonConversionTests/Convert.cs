using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Modules.Workers.Contracts;

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
            [new InputQuestion("Which endpoint?", "Two match.", "The v2 one.")],
            DecidedAt);

        // Act
        TriageVerdict read = Deserialize(Serialize(verdict));

        // Assert
        read.ShouldBeOfType<TriageVerdict.NeedsInput>().Questions
            .ShouldBe([new InputQuestion("Which endpoint?", "Two match.", "The v2 one.")]);
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
            .ShouldBe([new InputQuestion("Which endpoint?", "Two match.", null)]);
    }
}
