using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Modules.Workers.Contracts;

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
            [Clarification.Ask([new InputQuestion("Which database?", "Two are configured.", "Postgres.")], AskedAt)];

        // Act
        IReadOnlyList<Clarification> read = Deserialize(Serialize(log));

        // Assert
        read.ShouldHaveSingleItem().Questions.ShouldBe([new InputQuestion("Which database?", "Two are configured.", "Postgres.")]);
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
        read.ShouldHaveSingleItem().Questions.ShouldBe([new InputQuestion("Which database?", "Two are configured.", null)]);
    }
}
