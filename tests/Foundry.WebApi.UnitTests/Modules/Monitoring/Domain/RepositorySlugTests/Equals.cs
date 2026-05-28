using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Domain.RepositorySlugTests;

public sealed class Equals
{
    private static RepositorySlug Create(string input) =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create(input)).Value;

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
