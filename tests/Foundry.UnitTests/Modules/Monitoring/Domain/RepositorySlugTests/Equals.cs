using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.RepositorySlugTests;

public sealed class Equals
{
    private static RepositorySlug Create(string input) =>
        RepositorySlug.Create(input).ValueOrThrow();

    [Fact]
    public void WhenTwoSlugsHaveSameOwnerAndName_AreEqual()
    {
        // Arrange
        RepositorySlug a = Create("octocat/hello-world");
        RepositorySlug b = Create("octocat/hello-world");

        // Act & Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenTwoSlugsHaveDifferentNames_AreNotEqual()
    {
        // Arrange
        RepositorySlug a = Create("octocat/hello-world");
        RepositorySlug b = Create("octocat/other-repo");

        // Act & Assert
        a.ShouldNotBe(b);
    }
}
