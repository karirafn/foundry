using Foundry.Modules.Monitoring.Infrastructure.Configurations;

using Microsoft.AspNetCore.DataProtection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.WebhookSecretConverterTests;

public sealed class ReadAndWrite
{
    private static readonly IDataProtectionProvider Provider =
        DataProtectionProvider.Create("Foundry.Test");

    private static readonly WebhookSecretConverter Converter = new(Provider);

    [Fact]
    public void WhenSecretIsWritten_ReadingItBackReturnsTheSecret()
    {
        // Arrange
        const string secret = "whsec-secret-value";
        Func<string?, string?> write = Converter.ConvertToProviderExpression.Compile();
        Func<string?, string?> read = Converter.ConvertFromProviderExpression.Compile();

        // Act
        string? result = read(write(secret));

        // Assert
        result.ShouldBe(secret);
    }

    [Fact]
    public void WhenSecretIsNull_WriteReturnsNull()
    {
        // Arrange
        Func<string?, string?> write = Converter.ConvertToProviderExpression.Compile();

        // Act
        string? result = write(null);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenStoredValueIsNull_ReadReturnsNull()
    {
        // Arrange
        Func<string?, string?> read = Converter.ConvertFromProviderExpression.Compile();

        // Act
        string? result = read(null);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenStoredValueWasProtectedUnderAnotherDiscriminator_ReadReturnsNull()
    {
        // Arrange
        IDataProtector otherProtector = DataProtectionProvider
            .Create("Different.Key")
            .CreateProtector("Foundry.Monitoring.WebhookSecret");
        string stored = Convert.ToBase64String(otherProtector.Protect(System.Text.Encoding.UTF8.GetBytes("whsec")));
        Func<string?, string?> read = Converter.ConvertFromProviderExpression.Compile();

        // Act
        string? result = read(stored);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenStoredValueIsNotValidBase64_ReadReturnsNull()
    {
        // Arrange
        Func<string?, string?> read = Converter.ConvertFromProviderExpression.Compile();

        // Act
        string? result = read("this-is-not-base64!!!");

        // Assert
        result.ShouldBeNull();
    }
}
