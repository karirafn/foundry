using Foundry.Modules.Settings.Infrastructure;

using Microsoft.AspNetCore.DataProtection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Infrastructure.EncryptedStringConverterTests;

public sealed class RoundTrip
{
    private readonly EncryptedStringConverter _sut;
    private readonly Func<string, string> _encrypt;
    private readonly Func<string, string> _decrypt;

    public RoundTrip()
    {
        IDataProtectionProvider provider = DataProtectionProvider.Create("TestApp");
        _sut = new EncryptedStringConverter(provider);
        _encrypt = _sut.ConvertToProviderExpression.Compile();
        _decrypt = _sut.ConvertFromProviderExpression.Compile();
    }

    [Fact]
    public void WhenValueEncrypted_DecryptingProducesOriginalString()
    {
        // Arrange
        string original = "super-secret-api-key";

        // Act
        string encrypted = _encrypt(original);
        string decrypted = _decrypt(encrypted);

        // Assert
        decrypted.ShouldBe(original);
    }

    [Fact]
    public void WhenValueEncrypted_CiphertextDiffersFromPlaintext()
    {
        // Arrange
        string original = "super-secret-api-key";

        // Act
        string encrypted = _encrypt(original);

        // Assert
        encrypted.ShouldNotBe(original);
    }

    [Fact]
    public void WhenCorruptedCiphertextDecrypted_ReturnsEmptyString()
    {
        // Arrange
        string corrupted = Convert.ToBase64String("not-valid-protected-data"u8.ToArray());

        // Act
        string result = _decrypt(corrupted);

        // Assert
        result.ShouldBe(string.Empty);
    }

    [Fact]
    public void WhenEmptyStringEncrypted_DecryptingProducesEmptyString()
    {
        // Arrange
        string original = string.Empty;

        // Act
        string encrypted = _encrypt(original);
        string decrypted = _decrypt(encrypted);

        // Assert
        decrypted.ShouldBe(original);
    }
}
