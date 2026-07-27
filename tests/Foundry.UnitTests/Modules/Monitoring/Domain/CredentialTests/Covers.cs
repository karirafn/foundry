using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class Covers
{
    private static GitHubCredential CreateCredential(params string[] namespaceValues)
    {
        GitHubCredential credential = GitHubCredential.Create(
            "my-org",
            "ghp_token",
            BaseUrl.Create("https://github.com").ValueOrThrow());

        credential.SetNamespaces(namespaceValues.Select(v => Namespace.Create(v).ValueOrThrow()));
        return credential;
    }

    private static RepositorySlug Slug(string value) =>
        RepositorySlug.Create(value).ValueOrThrow();

    [Fact]
    public void WhenNoNamespaces_ReturnsFalse()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();

        // Act
        bool result = credential.Covers(Slug("owner/repo"));

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenNamespaceMatchesOwner_ReturnsTrue()
    {
        // Arrange
        GitHubCredential credential = CreateCredential("my-org");

        // Act
        bool result = credential.Covers(Slug("my-org/repo"));

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenNamespaceDoesNotMatchOwner_ReturnsFalse()
    {
        // Arrange
        GitHubCredential credential = CreateCredential("other-org");

        // Act
        bool result = credential.Covers(Slug("my-org/repo"));

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenNamespaceIsParentOfNestedOwner_ReturnsTrue()
    {
        // Arrange — claim on "group" covers "group/subgroup/project"
        GitHubCredential credential = CreateCredential("group");

        // Act
        bool result = credential.Covers(Slug("group/subgroup/project"));

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenNamespaceIsPartialPrefixButNotSegmentAligned_ReturnsFalse()
    {
        // Arrange — "my-o" is not a segment-aligned prefix of "my-org/repo"
        GitHubCredential credential = CreateCredential("my-o");

        // Act
        bool result = credential.Covers(Slug("my-org/repo"));

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenOneOfMultipleNamespacesMatches_ReturnsTrue()
    {
        // Arrange
        GitHubCredential credential = CreateCredential("org-a", "org-b");

        // Act
        bool result = credential.Covers(Slug("org-b/repo"));

        // Assert
        result.ShouldBeTrue();
    }
}
