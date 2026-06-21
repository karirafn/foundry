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

    [Fact]
    public void WhenSlugHasThreeSegments_ReturnsSuccessWithGroupOwnerAndName()
    {
        // Arrange
        string input = "my-group/my-subgroup/my-project";

        // Act
        Result<RepositorySlug>.Success result = RepositorySlug.Create(input)
            .ShouldBeOfType<Result<RepositorySlug>.Success>();

        // Assert
        result.Value.ShouldSatisfyAllConditions(
            () => result.Value.Owner.ShouldBe("my-group/my-subgroup"),
            () => result.Value.Name.ShouldBe("my-project"));
    }

    [Fact]
    public void WhenSlugHasThreeSegments_FullPathMatchesInput()
    {
        // Arrange
        string input = "my-group/my-subgroup/my-project";

        // Act
        Result<RepositorySlug>.Success result = RepositorySlug.Create(input)
            .ShouldBeOfType<Result<RepositorySlug>.Success>();

        // Assert
        result.Value.FullPath.ShouldBe("my-group/my-subgroup/my-project");
    }

    [Fact]
    public void WhenSlugHasTwoSegments_FullPathMatchesInput()
    {
        // Arrange
        string input = "octocat/hello-world";

        // Act
        Result<RepositorySlug>.Success result = RepositorySlug.Create(input)
            .ShouldBeOfType<Result<RepositorySlug>.Success>();

        // Assert
        result.Value.FullPath.ShouldBe("octocat/hello-world");
    }
}
