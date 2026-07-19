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
