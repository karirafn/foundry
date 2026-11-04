using System.Security.Cryptography;
using System.Text;

using Foundry.Shared.Infrastructure.DataProtection;

using Microsoft.AspNetCore.DataProtection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.DataProtection;

public sealed class DataProtectedTextTests
{
    private const string Purpose = "Foundry.Test.Purpose";

    private static readonly IDataProtector Protector =
        DataProtectionProvider.Create("Foundry.Test").CreateProtector(Purpose);

    [Fact]
    public void Protect_ReturnsBase64OfProtectedUtf8Bytes()
    {
        // Arrange
        const string plaintext = "sécret-värde";

        // Act
        string text = DataProtectedText.Protect(Protector, plaintext);

        // Assert
        byte[] decoded = Protector.Unprotect(Convert.FromBase64String(text));
        Encoding.UTF8.GetString(decoded).ShouldBe(plaintext);
    }

    [Fact]
    public void Unprotect_ReversesProtect()
    {
        // Arrange
        const string plaintext = "round-trip";
        string text = DataProtectedText.Protect(Protector, plaintext);

        // Act
        string result = DataProtectedText.Unprotect(Protector, text);

        // Assert
        result.ShouldBe(plaintext);
    }

    [Fact]
    public void Unprotect_WhenProtectedUnderAnotherPurpose_ThrowsCryptographicException()
    {
        // Arrange
        IDataProtector other = DataProtectionProvider.Create("Foundry.Test").CreateProtector("Other.Purpose");
        string text = DataProtectedText.Protect(other, "value");

        // Act
        Action act = () => DataProtectedText.Unprotect(Protector, text);

        // Assert
        Should.Throw<CryptographicException>(act);
    }

    [Fact]
    public void Unprotect_WhenProtectedUnderAnotherDiscriminator_ThrowsCryptographicException()
    {
        // Arrange
        IDataProtector other = DataProtectionProvider.Create("Other.Discriminator").CreateProtector(Purpose);
        string text = DataProtectedText.Protect(other, "value");

        // Act
        Action act = () => DataProtectedText.Unprotect(Protector, text);

        // Assert
        Should.Throw<CryptographicException>(act);
    }

    [Fact]
    public void Unprotect_WhenInputIsNotBase64_ThrowsFormatException()
    {
        // Arrange
        const string text = "this-is-not-base64!!!";

        // Act
        Action act = () => DataProtectedText.Unprotect(Protector, text);

        // Assert
        Should.Throw<FormatException>(act);
    }
}
