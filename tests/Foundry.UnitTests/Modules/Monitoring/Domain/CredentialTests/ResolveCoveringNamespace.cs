using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class ResolveCoveringNamespace
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
    public void WhenNoNamespaces_ReturnsNull()
    {
        // Arrange
        GitHubCredential credential = CreateCredential();

        // Act
        Namespace? result = credential.ResolveCoveringNamespace(Slug("owner/repo"));

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenNamespaceMatchesOwner_ReturnsNamespace()
    {
        // Arrange
        GitHubCredential credential = CreateCredential("my-org");

        // Act
        Namespace? result = credential.ResolveCoveringNamespace(Slug("my-org/repo"));

        // Assert
        result.ShouldNotBeNull();
        result!.Value.ShouldBe("my-org");
    }

    [Fact]
    public void WhenNamespaceDoesNotMatchOwner_ReturnsNull()
    {
        // Arrange
        GitHubCredential credential = CreateCredential("other-org");

        // Act
        Namespace? result = credential.ResolveCoveringNamespace(Slug("my-org/repo"));

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenSlugHasNestedOwner_MatchesLongestPrefix()
    {
        // Arrange
        GitHubCredential credential = CreateCredential("efla", "efla/databridge");

        // Act
        Namespace? result = credential.ResolveCoveringNamespace(Slug("efla/databridge/akraborg"));

        // Assert
        result.ShouldNotBeNull();
        result!.Value.ShouldBe("efla/databridge");
    }

    [Fact]
    public void WhenOnlyShortPrefixMatches_ReturnsShortPrefix()
    {
        // Arrange
        GitHubCredential credential = CreateCredential("efla");

        // Act
        Namespace? result = credential.ResolveCoveringNamespace(Slug("efla/databridge/akraborg"));

        // Assert
        result.ShouldNotBeNull();
        result!.Value.ShouldBe("efla");
    }

    [Fact]
    public void WhenNamespaceIsPartialPrefixButNotSegmentAligned_ReturnsNull()
    {
        // Arrange
        // "my-o" is not a segment-aligned prefix of "my-org/repo"
        GitHubCredential credential = CreateCredential("my-o");

        // Act
        Namespace? result = credential.ResolveCoveringNamespace(Slug("my-org/repo"));

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenMultipleNamespacesMatch_ReturnsLongestMatch()
    {
        // Arrange
        GitHubCredential credential = CreateCredential("efla", "efla/databridge");

        // Act — slug owner has two segments
        Namespace? result = credential.ResolveCoveringNamespace(Slug("efla/databridge/akraborg"));

        // Assert — longer prefix wins
        result.ShouldNotBeNull();
        result!.Value.ShouldBe("efla/databridge");
    }
}
