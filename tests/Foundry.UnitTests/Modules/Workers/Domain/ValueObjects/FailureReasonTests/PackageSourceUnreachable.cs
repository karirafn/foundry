using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class PackageSourceUnreachable
{
    private const string Detail = "the package proxy at host.docker.internal:5287 did not answer";

    [Fact]
    public void CategoryToken_IsPackageSourceUnreachable()
    {
        // Arrange
        FailureReason reason = new FailureReason.PackageSourceUnreachable(Detail);

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("package_source_unreachable");
    }

    [Fact]
    public void Summary_NamesThePackageProxyAndCarriesTheDetail()
    {
        // Arrange
        FailureReason reason = new FailureReason.PackageSourceUnreachable(Detail);

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe($"Could not reach the package proxy: {Detail}");
    }

    [Fact]
    public void WhenSerialized_ContainsPackageSourceUnreachableDiscriminator()
    {
        // Arrange
        FailureReason reason = new FailureReason.PackageSourceUnreachable(Detail);

        // Act
        string json = JsonSerializer.Serialize(reason);

        // Assert
        json.ShouldContain(@"""$type"":""package_source_unreachable""");
    }

    [Fact]
    public void WhenRoundTripped_DeserializesAsPackageSourceUnreachableWithItsDetail()
    {
        // Arrange
        FailureReason original = new FailureReason.PackageSourceUnreachable(Detail);

        // Act
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(JsonSerializer.Serialize(original));

        // Assert
        result.ShouldBe(original);
    }
}
