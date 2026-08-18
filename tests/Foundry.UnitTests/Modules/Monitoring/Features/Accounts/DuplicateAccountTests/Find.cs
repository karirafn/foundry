using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Accounts.DuplicateAccountTests;

public sealed class Find
{
    [Fact]
    public void WhenGitLabNestedGroupNamespace_OverlapIsExactValueNotPrefix()
    {
        // Arrange — "efla/gis" (derived) vs "efla" (claimed): distinct values, not a prefix match
        Namespace derivedNs = Namespace.Create("efla/gis").ValueOrThrow();
        Dictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers = new()
        {
            ["efla"] = (Guid.NewGuid(), "karirafn"),
        };

        // Act
        (string HolderName, string SharedOwner)? result = DuplicateAccount.Find(
            resolvedName: "karirafn",
            derivedNamespaces: [derivedNs],
            claimedByOthers: claimedByOthers);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenMultipleClaimsButOnlyOneIntersects_ReturnsIntersectingOne()
    {
        // Arrange
        Namespace derivedNs = Namespace.Create("karirafn").ValueOrThrow();
        Guid intersectingHolder = Guid.NewGuid();
        Dictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers = new()
        {
            ["other-org"] = (Guid.NewGuid(), "karirafn"),
            ["karirafn"] = (intersectingHolder, "karirafn"),
        };

        // Act
        (string HolderName, string SharedOwner)? result = DuplicateAccount.Find(
            resolvedName: "karirafn",
            derivedNamespaces: [derivedNs],
            claimedByOthers: claimedByOthers);

        // Assert
        result.ShouldNotBeNull();
        result.Value.ShouldSatisfyAllConditions(
            () => result.Value.HolderName.ShouldBe("karirafn"),
            () => result.Value.SharedOwner.ShouldBe("karirafn"));
    }

    [Fact]
    public void WhenClaimedByOthersIsEmpty_ReturnsNull()
    {
        // Arrange
        Namespace derivedNs = Namespace.Create("karirafn").ValueOrThrow();

        // Act
        (string HolderName, string SharedOwner)? result = DuplicateAccount.Find(
            resolvedName: "karirafn",
            derivedNamespaces: [derivedNs],
            claimedByOthers: new Dictionary<string, (Guid HolderCredentialId, string HolderName)>());

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenDifferentNameButIntersectingNamespace_ReturnsNull()
    {
        // Arrange — a genuine namespace conflict (different login, same owner): handled by the takeover flow, not the duplicate guard
        Namespace derivedNs = Namespace.Create("efla").ValueOrThrow();
        Dictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers = new()
        {
            ["efla"] = (Guid.NewGuid(), "colleague"),
        };

        // Act
        (string HolderName, string SharedOwner)? result = DuplicateAccount.Find(
            resolvedName: "karirafn",
            derivedNamespaces: [derivedNs],
            claimedByOthers: claimedByOthers);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenSameNameButDisjointNamespaces_ReturnsNull()
    {
        // Arrange — same login, different owner namespaces: the legitimate two-owner case this bug exists to unblock
        Namespace derivedNs = Namespace.Create("Kraftlyftingasamband-Islands").ValueOrThrow();
        Dictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers = new()
        {
            ["karirafn"] = (Guid.NewGuid(), "karirafn"),
        };

        // Act
        (string HolderName, string SharedOwner)? result = DuplicateAccount.Find(
            resolvedName: "karirafn",
            derivedNamespaces: [derivedNs],
            claimedByOthers: claimedByOthers);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenSameNameAndIntersectingNamespace_ReturnsHolderAndSharedOwner()
    {
        // Arrange
        Namespace derivedNs = Namespace.Create("karirafn").ValueOrThrow();
        Dictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers = new()
        {
            ["karirafn"] = (Guid.NewGuid(), "karirafn"),
        };

        // Act
        (string HolderName, string SharedOwner)? result = DuplicateAccount.Find(
            resolvedName: "karirafn",
            derivedNamespaces: [derivedNs],
            claimedByOthers: claimedByOthers);

        // Assert
        result.ShouldNotBeNull();
        result.Value.ShouldSatisfyAllConditions(
            () => result.Value.HolderName.ShouldBe("karirafn"),
            () => result.Value.SharedOwner.ShouldBe("karirafn"));
    }
}
