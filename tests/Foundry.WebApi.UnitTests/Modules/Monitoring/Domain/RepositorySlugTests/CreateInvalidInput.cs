using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

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
    [InlineData("owner%2Frepo/name")]
    [InlineData("owner?labels=evil/name")]
    [InlineData("owner/name&state=open")]
    [InlineData("owner!/name")]
    public void WhenSlugIsInvalid_ReturnsFailure(string? input)
    {
        // Arrange

        // Act
        Result<RepositorySlug> result = RepositorySlug.Create(input!);

        // Assert
        result.ShouldBeOfType<Result<RepositorySlug>.Failure>();
    }
}
