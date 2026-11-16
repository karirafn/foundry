using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.AccountNameTests;

public sealed class Create
{
    [Fact]
    public void WhenValueIsAProviderLogin_ReturnsAccountNameWithThatValue()
    {
        // Act
        Result<AccountName> result = AccountName.Create("octocat");

        // Assert
        result.ValueOrThrow().Value.ShouldBe("octocat");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t ")]
    public void WhenValueIsBlank_ReturnsUnresolvedIdentity(string value)
    {
        // Act
        Result<AccountName> result = AccountName.Create(value);

        // Assert
        ((Result<AccountName>.Failure)result).Error.ShouldBe(CredentialErrors.UnresolvedIdentity);
    }

    [Fact]
    public void WhenValueIs200Characters_ReturnsAccountName()
    {
        // Arrange
        string value = new('a', 200);

        // Act
        Result<AccountName> result = AccountName.Create(value);

        // Assert
        result.ValueOrThrow().Value.ShouldBe(value);
    }

    [Fact]
    public void WhenValueIs201Characters_ReturnsUnresolvedIdentity()
    {
        // Act
        Result<AccountName> result = AccountName.Create(new string('a', 201));

        // Assert
        ((Result<AccountName>.Failure)result).Error.ShouldBe(CredentialErrors.UnresolvedIdentity);
    }

    [Theory]
    [InlineData("octo\ncat")]
    [InlineData("octo\u0000cat")]
    [InlineData("octocat\u007f")]
    public void WhenValueContainsAControlCharacter_ReturnsUnresolvedIdentity(string value)
    {
        // Act
        Result<AccountName> result = AccountName.Create(value);

        // Assert
        ((Result<AccountName>.Failure)result).Error.ShouldBe(CredentialErrors.UnresolvedIdentity);
    }
}
