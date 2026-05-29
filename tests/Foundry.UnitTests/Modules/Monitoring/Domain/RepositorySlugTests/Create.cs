using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.RepositorySlugTests;

public sealed class Create
{
    [Fact]
    public void WhenSlugIsValid_ReturnsSuccessWithOwnerAndName()
    {
        // Arrange
        string input = "octocat/hello-world";

        // Act
        Result<RepositorySlug>.Success result = RepositorySlug.Create(input)
            .ShouldBeOfType<Result<RepositorySlug>.Success>();

        // Assert
        result.Value.ShouldSatisfyAllConditions(
            () => result.Value.Owner.ShouldBe("octocat"),
            () => result.Value.Name.ShouldBe("hello-world"));
    }
}
