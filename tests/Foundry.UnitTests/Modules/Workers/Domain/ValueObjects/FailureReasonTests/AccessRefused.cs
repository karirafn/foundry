using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class AccessRefused
{
    private const string Detail =
        "fetching the repository failed: fatal: unable to access 'https://oauth2@github.com/o/r.git/': The requested URL returned error: 403";

    [Fact]
    public void CategoryToken_IsAccessRefused()
    {
        // Arrange
        FailureReason reason = new FailureReason.AccessRefused(Detail);

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("access_refused");
    }

    [Fact]
    public void Summary_SaysTheProviderRefusedTheTokenAndCarriesTheDetail()
    {
        // Arrange
        FailureReason reason = new FailureReason.AccessRefused(Detail);

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe($"The provider refused the account token: {Detail}");
    }

    [Fact]
    public void WhenSerialized_ContainsAccessRefusedDiscriminator()
    {
        // Arrange
        FailureReason reason = new FailureReason.AccessRefused(Detail);

        // Act
        string json = JsonSerializer.Serialize(reason);

        // Assert
        json.ShouldContain(@"""$type"":""access_refused""");
    }

    [Fact]
    public void WhenRoundTripped_DeserializesAsAccessRefusedWithItsDetail()
    {
        // Arrange
        FailureReason original = new FailureReason.AccessRefused(Detail);

        // Act
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(JsonSerializer.Serialize(original));

        // Assert
        result.ShouldBe(original);
    }
}
