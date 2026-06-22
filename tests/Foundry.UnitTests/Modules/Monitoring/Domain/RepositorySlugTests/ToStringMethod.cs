using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.RepositorySlugTests;

public sealed class ToStringMethod
{
    [Fact]
    public void WhenCalled_ReturnsOwnerSlashName()
    {
        // Arrange
        RepositorySlug slug = RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

        // Act
        string result = slug.ToString();

        // Assert
        result.ShouldBe("octocat/hello-world");
    }
}
