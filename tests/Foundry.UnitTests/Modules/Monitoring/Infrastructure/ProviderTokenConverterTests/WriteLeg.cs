using System.Security.Cryptography;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure.Configurations;

using Microsoft.AspNetCore.DataProtection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.ProviderTokenConverterTests;

public sealed class WriteLeg
{
    private static readonly IDataProtectionProvider Provider =
        DataProtectionProvider.Create("Foundry.Test");

    private static readonly ProviderTokenConverter Converter = new(Provider);

    [Fact]
    public void WhenTokenIsNull_WritesNull()
    {
        // Arrange
        Func<ProviderToken?, string?> write = Converter.ConvertToProviderExpression.Compile();

        // Act
        string? result = write(null);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenTokenIsPresent_WritesBase64EncryptedValue()
    {
        // Arrange
        const string token = "glpat-secret-value";
        ProviderToken.Present present = new(token);
        Func<ProviderToken?, string?> write = Converter.ConvertToProviderExpression.Compile();
        Func<string?, ProviderToken?> read = Converter.ConvertFromProviderExpression.Compile();

        // Act
        string? stored = write(present);

        // Assert — round-trip back to Present with the same value
        stored.ShouldNotBeNull();
        ProviderToken.Present roundTripped = read(stored).ShouldBeOfType<ProviderToken.Present>();
        roundTripped.Value.ShouldBe(token);
    }

    [Fact]
    public void WhenTokenIsUnreadable_ThrowsInvalidOperationException()
    {
        // Arrange
        ProviderToken.Unreadable unreadable = new();
        Func<ProviderToken?, string?> write = Converter.ConvertToProviderExpression.Compile();

        // Act
        Action act = () => write(unreadable);

        // Assert
        Should.Throw<InvalidOperationException>(act);
    }
}
