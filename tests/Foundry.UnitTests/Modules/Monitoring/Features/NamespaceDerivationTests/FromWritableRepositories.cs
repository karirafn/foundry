using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.NamespaceDerivationTests;

public sealed class FromWritableRepositories
{
    [Fact]
    public void WhenNoRepositories_ReturnsEmptySet()
    {
        // Arrange
        IReadOnlyList<ProviderRepository> repos = [];

        // Act
        IReadOnlyCollection<Namespace> result = NamespaceDerivation.FromWritableRepositories(repos);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void WhenAllRepositoriesNotWritable_ReturnsEmptySet()
    {
        // Arrange
        IReadOnlyList<ProviderRepository> repos =
        [
            new ProviderRepository("alice/repo-a", IsPrivate: false, CanPush: false),
            new ProviderRepository("bob/repo-b", IsPrivate: true, CanPush: false),
        ];

        // Act
        IReadOnlyCollection<Namespace> result = NamespaceDerivation.FromWritableRepositories(repos);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void WhenSingleWritableRepository_ReturnsOwnerNamespace()
    {
        // Arrange
        IReadOnlyList<ProviderRepository> repos =
        [
            new ProviderRepository("octocat/hello-world", IsPrivate: false, CanPush: true),
        ];

        // Act
        IReadOnlyCollection<Namespace> result = NamespaceDerivation.FromWritableRepositories(repos);

        // Assert
        result.Count.ShouldBe(1);
        result.ShouldContain(ns => ns.Value == "octocat");
    }

    [Fact]
    public void WhenMultipleWritableRepositoriesUnderSameOwner_DeduplicatesNamespace()
    {
        // Arrange
        IReadOnlyList<ProviderRepository> repos =
        [
            new ProviderRepository("octocat/repo-a", IsPrivate: false, CanPush: true),
            new ProviderRepository("octocat/repo-b", IsPrivate: false, CanPush: true),
            new ProviderRepository("octocat/repo-c", IsPrivate: false, CanPush: true),
        ];

        // Act
        IReadOnlyCollection<Namespace> result = NamespaceDerivation.FromWritableRepositories(repos);

        // Assert
        result.Count.ShouldBe(1);
        result.ShouldContain(ns => ns.Value == "octocat");
    }

    [Fact]
    public void WhenMultipleWritableRepositoriesUnderDifferentOwners_ReturnsAllOwnerNamespaces()
    {
        // Arrange — classic GitHub PAT covering many owners
        IReadOnlyList<ProviderRepository> repos =
        [
            new ProviderRepository("alice/repo-a", IsPrivate: false, CanPush: true),
            new ProviderRepository("bob/repo-b", IsPrivate: true, CanPush: true),
            new ProviderRepository("org-name/repo-c", IsPrivate: false, CanPush: true),
        ];

        // Act
        IReadOnlyCollection<Namespace> result = NamespaceDerivation.FromWritableRepositories(repos);

        // Assert
        result.Count.ShouldBe(3);
        result.ShouldContain(ns => ns.Value == "alice");
        result.ShouldContain(ns => ns.Value == "bob");
        result.ShouldContain(ns => ns.Value == "org-name");
    }

    [Fact]
    public void WhenMixOfWritableAndNonWritable_OnlyIncludesWritableOwners()
    {
        // Arrange
        IReadOnlyList<ProviderRepository> repos =
        [
            new ProviderRepository("alice/writable-repo", IsPrivate: false, CanPush: true),
            new ProviderRepository("bob/read-only-repo", IsPrivate: false, CanPush: false),
            new ProviderRepository("alice/another-repo", IsPrivate: false, CanPush: true),
        ];

        // Act
        IReadOnlyCollection<Namespace> result = NamespaceDerivation.FromWritableRepositories(repos);

        // Assert
        result.Count.ShouldBe(1);
        result.ShouldContain(ns => ns.Value == "alice");
        result.ShouldNotContain(ns => ns.Value == "bob");
    }

    [Fact]
    public void WhenGitLabNestedGroupPath_ReturnsFullGroupOwnerAsNamespace()
    {
        // Arrange — GitLab slug owner is the full group path (e.g. "company/team")
        IReadOnlyList<ProviderRepository> repos =
        [
            new ProviderRepository("company/team/project", IsPrivate: false, CanPush: true),
        ];

        // Act
        IReadOnlyCollection<Namespace> result = NamespaceDerivation.FromWritableRepositories(repos);

        // Assert — owner "company/team" is registered, not just "company"
        result.Count.ShouldBe(1);
        result.ShouldContain(ns => ns.Value == "company/team");
    }

    [Fact]
    public void WhenGitLabNestedGroupMultipleProjects_DeduplicatesGroupOwner()
    {
        // Arrange
        IReadOnlyList<ProviderRepository> repos =
        [
            new ProviderRepository("company/team/project-a", IsPrivate: false, CanPush: true),
            new ProviderRepository("company/team/project-b", IsPrivate: false, CanPush: true),
        ];

        // Act
        IReadOnlyCollection<Namespace> result = NamespaceDerivation.FromWritableRepositories(repos);

        // Assert
        result.Count.ShouldBe(1);
        result.ShouldContain(ns => ns.Value == "company/team");
    }

    [Fact]
    public void WhenGitLabAccessLevelIsDeveloper_IncludesOwner()
    {
        // Arrange — access_level 30 = Developer threshold
        IReadOnlyList<ProviderRepository> repos =
        [
            new ProviderRepository("group/project", IsPrivate: false, CanPush: true),
        ];

        // Act
        IReadOnlyCollection<Namespace> result = NamespaceDerivation.FromWritableRepositories(repos);

        // Assert
        result.Count.ShouldBe(1);
    }

    [Fact]
    public void WhenGitLabAccessLevelBelowDeveloper_ExcludesOwner()
    {
        // Arrange — CanPush=false captures "below Developer" from the caller
        IReadOnlyList<ProviderRepository> repos =
        [
            new ProviderRepository("group/project", IsPrivate: false, CanPush: false),
        ];

        // Act
        IReadOnlyCollection<Namespace> result = NamespaceDerivation.FromWritableRepositories(repos);

        // Assert
        result.ShouldBeEmpty();
    }
}
