using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialErrorsTests;

public sealed class NamespaceClaimedElsewhereError
{
    [Fact]
    public void WhenSingleConflict_HasCorrectCode()
    {
        // Arrange
        IReadOnlyList<NamespaceConflict> conflicts =
        [
            new NamespaceConflict("first-user", Guid.NewGuid(), "First Account"),
        ];

        // Act
        Error error = CredentialErrors.NamespaceClaimedElsewhere(conflicts);

        // Assert
        error.Code.ShouldBe(CredentialErrors.NamespaceClaimedElsewhereCode);
    }

    [Fact]
    public void WhenSingleConflict_MessageContainsNamespaceAndHolder()
    {
        // Arrange
        IReadOnlyList<NamespaceConflict> conflicts =
        [
            new NamespaceConflict("first-user", Guid.NewGuid(), "First Account"),
        ];

        // Act
        Error error = CredentialErrors.NamespaceClaimedElsewhere(conflicts);

        // Assert
        error.Message.ShouldContain("first-user");
        error.Message.ShouldContain("First Account");
    }

    [Fact]
    public void WhenMultipleConflicts_MessageContainsAllNamespacesAndHoldersInOrdinalOrder()
    {
        // Arrange — reverse alphabetical order to prove ordinal sort is applied by caller
        IReadOnlyList<NamespaceConflict> conflicts =
        [
            new NamespaceConflict("zeta-org", Guid.NewGuid(), "Zeta Holder"),
            new NamespaceConflict("alpha-org", Guid.NewGuid(), "Alpha Holder"),
        ];

        // Act
        Error error = CredentialErrors.NamespaceClaimedElsewhere(conflicts);

        // Assert
        error.Message.ShouldContain("zeta-org");
        error.Message.ShouldContain("Zeta Holder");
        error.Message.ShouldContain("alpha-org");
        error.Message.ShouldContain("Alpha Holder");

        // Order must preserve the input list order (ordinal sort is the caller's responsibility)
        int zetaIndex = error.Message.IndexOf("zeta-org", StringComparison.Ordinal);
        int alphaIndex = error.Message.IndexOf("alpha-org", StringComparison.Ordinal);
        zetaIndex.ShouldBeLessThan(alphaIndex);
    }
}
