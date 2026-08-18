using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialErrorsTests;

public sealed class DuplicateAccountError
{
    [Fact]
    public void HasCorrectCode()
    {
        // Arrange / Act
        Error error = CredentialErrors.DuplicateAccount("karirafn", "karirafn");

        // Assert
        error.Code.ShouldBe(CredentialErrors.DuplicateAccountCode);
    }

    [Fact]
    public void MessageNamesCollidingAccountAndSharedOwner()
    {
        // Arrange / Act
        Error error = CredentialErrors.DuplicateAccount("my-account", "shared-org");

        // Assert
        error.Message.ShouldContain("my-account");
        error.Message.ShouldContain("shared-org");
    }

    [Fact]
    public void NamespaceDerivationUnavailableHasCorrectCode()
    {
        // Arrange / Act
        Error error = CredentialErrors.NamespaceDerivationUnavailable;

        // Assert
        error.Code.ShouldBe(CredentialErrors.NamespaceDerivationUnavailableCode);
    }

    [Fact]
    public void NamespaceDerivationUnavailableMessageSuggestsRetry()
    {
        // Arrange / Act
        Error error = CredentialErrors.NamespaceDerivationUnavailable;

        // Assert
        error.Message.ShouldContain("Try again");
    }
}
