using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.RepositorySlugTests;

public sealed class CreateInvalidInput
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    [InlineData("no-slash")]
    [InlineData("/no-owner")]
    [InlineData("no-name/")]
    [InlineData("owner//empty-middle")]
    [InlineData("owner%2Frepo/name")]
    [InlineData("owner?labels=evil/name")]
    [InlineData("owner/name&state=open")]
    [InlineData("owner!/name")]
    [InlineData("./target")]
    [InlineData("../target")]
    [InlineData("owner/./name")]
    [InlineData("owner/../name")]
    public void WhenSlugIsInvalid_ReturnsFailure(string? input)
    {
        // Arrange

        // Act
        Result<RepositorySlug> result = RepositorySlug.Create(input!);

        // Assert
        result.ShouldBeOfType<Result<RepositorySlug>.Failure>();
    }
}
