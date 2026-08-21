using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialErrorsTests;

public sealed class NamespaceFullyClaimedElsewhereError
{
    [Fact]
    public void HasCorrectCode()
    {
        // Arrange / Act
        Error error = CredentialErrors.NamespaceFullyClaimedElsewhere("other-account", "shared-org");

        // Assert
        error.Code.ShouldBe(CredentialErrors.NamespaceFullyClaimedElsewhereCode);
    }

    [Fact]
    public void MessageNamesHolderAndSharedOwner()
    {
        // Arrange / Act
        Error error = CredentialErrors.NamespaceFullyClaimedElsewhere("other-account", "shared-org");

        // Assert
        error.Message.ShouldContain("other-account");
        error.Message.ShouldContain("shared-org");
    }
}
