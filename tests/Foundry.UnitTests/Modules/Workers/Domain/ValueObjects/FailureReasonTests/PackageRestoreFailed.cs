using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class PackageRestoreFailed
{
    private const string Detail = "App.sln: error NU1301: Unable to load the service index";

    [Fact]
    public void CategoryToken_IsPackageRestoreFailed()
    {
        // Arrange
        FailureReason reason = new FailureReason.PackageRestoreFailed(Detail);

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("package_restore_failed");
    }

    [Fact]
    public void Summary_SaysPackagesCouldNotBeRestoredAndCarriesTheDetail()
    {
        // Arrange
        FailureReason reason = new FailureReason.PackageRestoreFailed(Detail);

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe($"Could not restore packages: {Detail}");
    }

    [Fact]
    public void WhenSerialized_ContainsPackageRestoreFailedDiscriminator()
    {
        // Arrange
        FailureReason reason = new FailureReason.PackageRestoreFailed(Detail);

        // Act
        string json = JsonSerializer.Serialize(reason);

        // Assert
        json.ShouldContain(@"""$type"":""package_restore_failed""");
    }

    [Fact]
    public void WhenRoundTripped_DeserializesAsPackageRestoreFailedWithItsDetail()
    {
        // Arrange
        FailureReason original = new FailureReason.PackageRestoreFailed(Detail);

        // Act
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(JsonSerializer.Serialize(original));

        // Assert
        result.ShouldBe(original);
    }
}
