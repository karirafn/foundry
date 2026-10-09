using System.Security.Cryptography;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure.Configurations;

using Microsoft.AspNetCore.DataProtection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.ProviderTokenConverterTests;

public sealed class ReadLeg
{
    private static readonly IDataProtectionProvider Provider =
        DataProtectionProvider.Create("Foundry.Test");

    private static readonly ProviderTokenConverter Converter = new(Provider);

    [Fact]
    public void WhenStoredValueIsNull_ReturnsNull()
    {
        // Arrange
        Func<string?, ProviderToken?> read = Converter.ConvertFromProviderExpression.Compile();

        // Act
        ProviderToken? result = read(null);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenStoredValueIsCryptographicallyCorrupted_ReturnsUnreadable()
    {
        // Arrange
        // Produce a valid base64 value that decrypts with the wrong key by protecting with a different provider.
        IDataProtectionProvider differentProvider = DataProtectionProvider.Create("Different.Key");
        IDataProtector differentProtector = differentProvider.CreateProtector("Foundry.Monitoring.Encryption");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("some-token");
        byte[] protectedBytes = differentProtector.Protect(bytes);
        string corruptedBase64 = Convert.ToBase64String(protectedBytes);

        Func<string?, ProviderToken?> read = Converter.ConvertFromProviderExpression.Compile();

        // Act
        ProviderToken? result = read(corruptedBase64);

        // Assert
        result.ShouldBeOfType<ProviderToken.Unreadable>();
    }

    [Fact]
    public void WhenStoredValueIsNotValidBase64_ReturnsUnreadable()
    {
        // Arrange
        Func<string?, ProviderToken?> read = Converter.ConvertFromProviderExpression.Compile();

        // Act
        ProviderToken? result = read("this-is-not-base64!!!");

        // Assert
        result.ShouldBeOfType<ProviderToken.Unreadable>();
    }

    [Fact]
    public void WhenStoredValueIsValidEncryptedToken_ReturnsPresent()
    {
        // Arrange
        const string token = "glpat-secret-value";
        IDataProtector protector = Provider.CreateProtector("Foundry.Monitoring.Encryption");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(token);
        byte[] protectedBytes = protector.Protect(bytes);
        string storedBase64 = Convert.ToBase64String(protectedBytes);

        Func<string?, ProviderToken?> read = Converter.ConvertFromProviderExpression.Compile();

        // Act
        ProviderToken? result = read(storedBase64);

        // Assert
        ProviderToken.Present present = result.ShouldBeOfType<ProviderToken.Present>();
        present.Value.ShouldBe(token);
    }
}
