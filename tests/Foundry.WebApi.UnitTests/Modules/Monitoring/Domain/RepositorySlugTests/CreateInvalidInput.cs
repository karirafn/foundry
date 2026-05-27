using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Domain.RepositorySlugTests;

public sealed class CreateInvalidInput
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    [InlineData("no-slash")]
    [InlineData("/no-owner")]
    [InlineData("no-name/")]
    [InlineData("owner/name/extra")]
    public void WhenSlugIsInvalid_ReturnsFailure(string? input)
    {
        // Arrange

        // Act
        Result<RepositorySlug> result = RepositorySlug.Create(input!);

        // Assert
        result.ShouldBeOfType<Result<RepositorySlug>.Failure>();
    }
}
