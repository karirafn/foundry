using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class FetchFailed
{
    [Fact]
    public void CategoryToken_IsFetchFailed()
    {
        // Arrange
        FailureReason reason = new FailureReason.FetchFailed("fatal: fetch-pack: invalid index-pack output");

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("fetch_failed");
    }

    [Fact]
    public void Summary_SaysFetchingFailedAndCarriesTheDetail()
    {
        // Arrange
        FailureReason reason = new FailureReason.FetchFailed("fatal: fetch-pack: invalid index-pack output");

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe("Fetching the repository failed: fatal: fetch-pack: invalid index-pack output");
    }

    [Fact]
    public void WhenSerialized_ContainsFetchFailedDiscriminator()
    {
        // Arrange
        FailureReason reason = new FailureReason.FetchFailed("fatal: fetch-pack: invalid index-pack output");

        // Act
        string json = JsonSerializer.Serialize(reason);

        // Assert
        json.ShouldContain(@"""$type"":""fetch_failed""");
    }

    [Fact]
    public void WhenRoundTripped_DeserializesAsFetchFailedWithItsDetail()
    {
        // Arrange
        FailureReason original = new FailureReason.FetchFailed("fatal: fetch-pack: invalid index-pack output");

        // Act
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(JsonSerializer.Serialize(original));

        // Assert
        result.ShouldBe(original);
    }
}
