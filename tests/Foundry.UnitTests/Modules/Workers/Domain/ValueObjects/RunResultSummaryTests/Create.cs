using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.RunResultSummaryTests;

public sealed class Create
{
    [Fact]
    public void WhenIsErrorTrueAndSubtypeIsSuccess_SubtypeIsNull()
    {
        // Arrange

        // Act
        RunResultSummary summary = RunResultSummary.Create(
            resultText: "Claude encountered an error.",
            subtype: "success",
            isError: true,
            durationMs: 1000,
            numTurns: 3,
            totalCostUsd: null,
            inputTokens: null,
            outputTokens: null);

        // Assert
        summary.Subtype.ShouldBeNull();
    }

    [Fact]
    public void WhenIsErrorFalseAndSubtypeIsSuccess_SubtypeIsPreserved()
    {
        // Arrange

        // Act
        RunResultSummary summary = RunResultSummary.Create(
            resultText: "All done.",
            subtype: "success",
            isError: false,
            durationMs: 5000,
            numTurns: 10,
            totalCostUsd: 0.01m,
            inputTokens: null,
            outputTokens: null);

        // Assert
        summary.Subtype.ShouldBe("success");
    }

    [Fact]
    public void WhenIsErrorTrueAndSubtypeIsGenuineErrorSubtype_SubtypeIsPreserved()
    {
        // Arrange

        // Act
        RunResultSummary summary = RunResultSummary.Create(
            resultText: "Max turns reached.",
            subtype: "error_max_turns",
            isError: true,
            durationMs: 2000,
            numTurns: 20,
            totalCostUsd: null,
            inputTokens: null,
            outputTokens: null);

        // Assert
        summary.Subtype.ShouldBe("error_max_turns");
    }
}
