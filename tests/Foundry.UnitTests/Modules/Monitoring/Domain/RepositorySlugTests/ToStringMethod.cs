using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.RepositorySlugTests;

public sealed class ToStringMethod
{
    [Fact]
    public void WhenCalled_ReturnsOwnerSlashName()
    {
        // Arrange
        RepositorySlug slug = ((Result<RepositorySlug>.Success)RepositorySlug.Create("octocat/hello-world")).Value;

        // Act
        string result = slug.ToString();

        // Assert
        result.ShouldBe("octocat/hello-world");
    }
}
