using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class SetNamespaces
{
    private static GitHubCredential CreateCredential() =>
        GitHubCredential.Create("my-org", "ghp_token", BaseUrl.Create("https://github.com").ValueOrThrow());

    private static Namespace Ns(string value) =>
        Namespace.Create(value).ValueOrThrow();

    // Tests for SetNamespaces(derived, claimedByOthers) overload

    [Fact]
    public void WhenClaimedByOthersIsEmpty_BehavesLikeSingleArgOverload()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();
        HashSet<string> claimedByOthers = [];

        // Act
        credential.SetNamespaces([Ns("org-a"), Ns("org-b")], claimedByOthers);

        // Assert
        credential.Namespaces.Count.ShouldBe(2);
        credential.Namespaces.ShouldContain(n => n.Value == "org-a");
        credential.Namespaces.ShouldContain(n => n.Value == "org-b");
    }

    [Fact]
    public void WhenDerivedContainsClaimedNamespace_ExcludesClaimedFromResult()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();
        HashSet<string> claimedByOthers = ["org-b"];

        // Act
        credential.SetNamespaces([Ns("org-a"), Ns("org-b")], claimedByOthers);

        // Assert
        credential.Namespaces.Count.ShouldBe(1);
        credential.Namespaces.ShouldContain(n => n.Value == "org-a");
        credential.Namespaces.ShouldNotContain(n => n.Value == "org-b");
    }

    [Fact]
    public void WhenAllDerivedAreClaimed_ResultIsEmpty()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();
        HashSet<string> claimedByOthers = ["org-a", "org-b"];

        // Act
        credential.SetNamespaces([Ns("org-a"), Ns("org-b")], claimedByOthers);

        // Assert
        credential.Namespaces.ShouldBeEmpty();
    }

    [Fact]
    public void WhenDuplicatesInDerivedAfterSubtraction_Deduplicates()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();
        HashSet<string> claimedByOthers = ["org-b"];
        Namespace nsA = Ns("org-a");

        // Act
        credential.SetNamespaces([nsA, nsA, Ns("org-b")], claimedByOthers);

        // Assert
        credential.Namespaces.Count.ShouldBe(1);
        credential.Namespaces.ShouldContain(n => n.Value == "org-a");
    }

    [Fact]
    public void WhenEmptyList_NamespacesCollectionIsEmpty()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();

        // Act
        credential.SetNamespaces([]);

        // Assert
        credential.Namespaces.ShouldBeEmpty();
    }

    [Fact]
    public void WhenSingleNamespaceProvided_CredentialHasOneNamespace()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();
        Namespace ns = Ns("my-org");

        // Act
        credential.SetNamespaces([ns]);

        // Assert
        credential.Namespaces.Count.ShouldBe(1);
        credential.Namespaces.ShouldContain(n => n.Value == "my-org");
    }

    [Fact]
    public void WhenDuplicateNamespacesProvided_DeduplicatesOnValue()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();
        Namespace ns = Ns("my-org");

        // Act
        credential.SetNamespaces([ns, ns]);

        // Assert
        credential.Namespaces.Count.ShouldBe(1);
    }

    [Fact]
    public void WhenMultipleDistinctNamespacesProvided_AllAreStored()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();

        // Act
        credential.SetNamespaces([Ns("org-a"), Ns("org-b"), Ns("org-c")]);

        // Assert
        credential.Namespaces.Count.ShouldBe(3);
    }

    [Fact]
    public void WhenCalledTwice_ReplacesExistingNamespaces()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();
        credential.SetNamespaces([Ns("old-org")]);

        // Act
        credential.SetNamespaces([Ns("new-org")]);

        // Assert
        credential.Namespaces.Count.ShouldBe(1);
        credential.Namespaces.ShouldContain(n => n.Value == "new-org");
    }

    [Fact]
    public void WhenNamespaceAdded_NamespaceHasCredentialIdSet()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();
        Namespace ns = Ns("my-org");

        // Act
        credential.SetNamespaces([ns]);

        // Assert
        CredentialNamespace stored = credential.Namespaces.ShouldHaveSingleItem();
        stored.CredentialId.ShouldBe(credential.Id);
    }

    [Fact]
    public void WhenNamespaceAdded_NamespaceHasHostFromCredential()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();
        Namespace ns = Ns("my-org");

        // Act
        credential.SetNamespaces([ns]);

        // Assert
        CredentialNamespace stored = credential.Namespaces.ShouldHaveSingleItem();
        stored.Host.ShouldBe("github.com");
    }
}
