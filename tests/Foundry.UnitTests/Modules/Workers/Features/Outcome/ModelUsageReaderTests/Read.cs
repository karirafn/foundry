using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.ModelUsageReaderTests;

public sealed class Read
{
    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    public void WhenModelUsageEmptyAndCostZero_ReturnsZeroTokens(string cost)
    {
        // Arrange — an auth failure before any model call: no model ran, so the true figure is zero
        JsonObject resultEvent = Parse(
            $$$"""{"type":"result","is_error":true,"result":"Failed to authenticate: OAuth session expired and could not be refreshed","total_cost_usd":{{{cost}}},"usage":{"input_tokens":0,"output_tokens":0},"modelUsage":{}}""");

        // Act
        TokenUsage tokens = ModelUsageReader.Read(resultEvent);

        // Assert
        tokens.ShouldBe(new TokenUsage(0, 0, 0, 0));
    }

    [Fact]
    public void WhenModelUsageEmptyAndCostNonZero_ReturnsUnknown()
    {
        // Arrange
        JsonObject resultEvent = Parse("""{"type":"result","total_cost_usd":0.12,"modelUsage":{}}""");

        // Act
        TokenUsage tokens = ModelUsageReader.Read(resultEvent);

        // Assert
        tokens.ShouldBe(TokenUsage.Unknown);
    }

    [Theory]
    [InlineData("""{"type":"result","modelUsage":{}}""")]
    [InlineData("""{"type":"result","total_cost_usd":null,"modelUsage":{}}""")]
    [InlineData("""{"type":"result","total_cost_usd":"0","modelUsage":{}}""")]
    [InlineData("""{"type":"result","total_cost_usd":false,"modelUsage":{}}""")]
    public void WhenModelUsageEmptyAndCostMissingOrNonNumeric_ReturnsUnknown(string json)
    {
        // Arrange
        JsonObject resultEvent = Parse(json);

        // Act
        TokenUsage tokens = ModelUsageReader.Read(resultEvent);

        // Assert
        tokens.ShouldBe(TokenUsage.Unknown);
    }

    [Theory]
    [InlineData("""{"type":"result","total_cost_usd":0}""")]
    [InlineData("""{"type":"result","total_cost_usd":0,"modelUsage":null}""")]
    [InlineData("""{"type":"result","total_cost_usd":0,"modelUsage":[]}""")]
    [InlineData("""{"type":"result","total_cost_usd":0,"modelUsage":"none"}""")]
    public void WhenModelUsageAbsentOrNotObject_ReturnsUnknown(string json)
    {
        // Arrange
        JsonObject resultEvent = Parse(json);

        // Act
        TokenUsage tokens = ModelUsageReader.Read(resultEvent);

        // Assert
        tokens.ShouldBe(TokenUsage.Unknown);
    }

    [Fact]
    public void WhenModelUsageNamesModels_SumsBuckets()
    {
        // Arrange
        JsonObject resultEvent = Parse(
            """{"type":"result","total_cost_usd":0.2,"modelUsage":{"a":{"inputTokens":1,"outputTokens":2,"cacheReadInputTokens":3,"cacheCreationInputTokens":4},"b":{"inputTokens":10,"outputTokens":20,"cacheReadInputTokens":30,"cacheCreationInputTokens":40}}}""");

        // Act
        TokenUsage tokens = ModelUsageReader.Read(resultEvent);

        // Assert
        tokens.ShouldBe(new TokenUsage(11, 33, 44, 22));
    }

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
}
