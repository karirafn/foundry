using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class ProviderUnreachable
{
    [Fact]
    public void CategoryToken_IsProviderUnreachable()
    {
        // Arrange
        FailureReason reason = new FailureReason.ProviderUnreachable("Could not resolve host: gitlab.com");

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("provider_unreachable");
    }

    [Fact]
    public void Summary_NamesTheGitProviderAndCarriesTheDetail()
    {
        // Arrange
        FailureReason reason = new FailureReason.ProviderUnreachable("Could not resolve host: gitlab.com");

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe("Could not reach the git provider: Could not resolve host: gitlab.com");
    }

    [Fact]
    public void WhenSerialized_ContainsProviderUnreachableDiscriminator()
    {
        // Arrange
        FailureReason reason = new FailureReason.ProviderUnreachable("Could not resolve host: gitlab.com");

        // Act
        string json = JsonSerializer.Serialize(reason);

        // Assert
        json.ShouldContain(@"""$type"":""provider_unreachable""");
    }

    [Fact]
    public void WhenRoundTripped_DeserializesAsProviderUnreachableWithItsDetail()
    {
        // Arrange
        FailureReason original = new FailureReason.ProviderUnreachable("Could not resolve host: gitlab.com");

        // Act
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(JsonSerializer.Serialize(original));

        // Assert
        result.ShouldBe(original);
    }
}
