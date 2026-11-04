using Foundry.Shared.Infrastructure.DataProtection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.DataProtection.LegacyDataProtectionTests;

public sealed class ResolveDiscriminator
{
    private const string CurrentName = "Foundry";

    [Fact]
    public void WhenConfiguredIsNonBlank_ReturnsTrimmedConfigured()
    {
        // Arrange
        const string configured = "  C:\\dev\\foundry\\src\\Foundry.WebApi\\ ";

        // Act
        string? discriminator = LegacyDataProtection.ResolveDiscriminator(configured, "/app/", CurrentName);

        // Assert
        discriminator.ShouldBe("C:\\dev\\foundry\\src\\Foundry.WebApi\\");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenConfiguredIsBlank_ReturnsHostDefault(string? configured)
    {
        // Arrange
        const string hostDefault = "/app/";

        // Act
        string? discriminator = LegacyDataProtection.ResolveDiscriminator(configured, hostDefault, CurrentName);

        // Assert
        discriminator.ShouldBe(hostDefault);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "  ")]
    public void WhenConfiguredAndHostDefaultAreBlank_ReturnsNull(string? configured, string? hostDefault)
    {
        // Act
        string? discriminator = LegacyDataProtection.ResolveDiscriminator(configured, hostDefault, CurrentName);

        // Assert
        discriminator.ShouldBeNull();
    }

    [Theory]
    [InlineData(" Foundry ", "/app/")]
    [InlineData(null, "Foundry")]
    public void WhenResolvedEqualsCurrentName_ReturnsNull(string? configured, string? hostDefault)
    {
        // Arrange

        // Act
        string? discriminator = LegacyDataProtection.ResolveDiscriminator(configured, hostDefault, CurrentName);

        // Assert
        discriminator.ShouldBeNull();
    }
}
