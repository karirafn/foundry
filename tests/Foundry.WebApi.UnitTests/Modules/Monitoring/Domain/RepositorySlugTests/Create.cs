using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Domain.RepositorySlugTests;

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
