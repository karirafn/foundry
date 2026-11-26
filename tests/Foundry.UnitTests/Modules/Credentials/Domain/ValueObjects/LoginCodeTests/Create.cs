using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.ValueObjects.LoginCodeTests;

public sealed class Create
{
    [Theory]
    [InlineData("abc#def")]
    [InlineData("a#b#c")]
    [InlineData("abc def#ghi")]
    public void WhenCodeHasTextOnBothSidesOfHash_ReturnsLoginCodeWithThatValue(string value)
    {
        // Act
        Result<LoginCode> result = LoginCode.Create(value);

        // Assert
        result.ValueOrThrow().Value.ShouldBe(value);
    }

    [Fact]
    public void WhenCodeHasSurroundingWhitespace_ReturnsTrimmedValue()
    {
        // Act
        Result<LoginCode> result = LoginCode.Create("  abc#def  ");

        // Assert
        result.ValueOrThrow().Value.ShouldBe("abc#def");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void WhenCodeIsBlank_ReturnsLoginCodeEmpty(string value)
    {
        // Act
        Result<LoginCode> result = LoginCode.Create(value);

        // Assert
        ErrorCode(result).ShouldBe(CredentialsErrors.LoginCodeEmptyCode);
    }

    [Fact]
    public void WhenCodeIs512Characters_ReturnsLoginCode()
    {
        // Arrange
        string value = new string('a', 256) + "#" + new string('b', 255);

        // Act
        Result<LoginCode> result = LoginCode.Create(value);

        // Assert
        result.ValueOrThrow().Value.Length.ShouldBe(512);
    }

    [Fact]
    public void WhenCodeIs513Characters_ReturnsLoginCodeTooLong()
    {
        // Arrange
        string value = new string('a', 256) + "#" + new string('b', 256);

        // Act
        Result<LoginCode> result = LoginCode.Create(value);

        // Assert
        ErrorCode(result).ShouldBe(CredentialsErrors.LoginCodeTooLongCode);
    }

    [Theory]
    [InlineData("abc#def\nxyz")]
    [InlineData("abc#def\r\nxyz")]
    [InlineData("abc\u0000#def")]
    public void WhenCodeContainsControlCharacter_ReturnsLoginCodeHasControlCharacters(string value)
    {
        // Act
        Result<LoginCode> result = LoginCode.Create(value);

        // Assert
        ErrorCode(result).ShouldBe(CredentialsErrors.LoginCodeHasControlCharactersCode);
    }

    [Theory]
    [InlineData("http://localhost:54545/callback?code=x&state=y")]
    [InlineData("https://claude.ai/magic-link#abc")]
    public void WhenCodeIsWebAddress_ReturnsLoginCodeIsWebAddress(string value)
    {
        // Act
        Result<LoginCode> result = LoginCode.Create(value);

        // Assert
        ErrorCode(result).ShouldBe(CredentialsErrors.LoginCodeIsWebAddressCode);
    }

    [Theory]
    [InlineData("INVALID-CODE-FOR-TEST")]
    [InlineData("123456")]
    [InlineData("abc#")]
    [InlineData("#abc")]
    [InlineData("#")]
    public void WhenCodeLacksTextOnBothSidesOfHash_ReturnsLoginCodeIncomplete(string value)
    {
        // Act
        Result<LoginCode> result = LoginCode.Create(value);

        // Assert
        ErrorCode(result).ShouldBe(CredentialsErrors.LoginCodeIncompleteCode);
    }

    [Fact]
    public void WhenCodeIsIncomplete_ErrorMessageTellsOperatorToCopyTheHash()
    {
        // Act
        Result<LoginCode> result = LoginCode.Create("123456");

        // Assert
        ((Result<LoginCode>.Failure)result).Error.Message
            .ShouldBe("That doesn't look like the full code. Copy all of it, including the # in the middle.");
    }

    [Fact]
    public void WhenCodeIsWebAddress_ErrorMessageTellsOperatorToPasteTheCode()
    {
        // Act
        Result<LoginCode> result = LoginCode.Create("https://claude.ai/x");

        // Assert
        ((Result<LoginCode>.Failure)result).Error.Message
            .ShouldBe("That's a web address, not the code. Paste the code the last sign-in page shows.");
    }

    [Fact]
    public void WhenCodeHasControlCharacters_ErrorMessageTellsOperatorToPasteAgain()
    {
        // Act
        Result<LoginCode> result = LoginCode.Create("a#b\nc");

        // Assert
        ((Result<LoginCode>.Failure)result).Error.Message
            .ShouldBe("The code contains line breaks or control characters. Copy just the code and paste it again.");
    }

    private static string ErrorCode(Result<LoginCode> result) =>
        ((Result<LoginCode>.Failure)result).Error.Code;
}
