using Foundry.Shared;
using Foundry.WebApi.Shared.Infrastructure;

using Microsoft.Extensions.Configuration;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Infrastructure.ConfigurationProviderAuthTests;

public sealed class GetTokenAsync
{
    private static IProviderAuth BuildSut(Dictionary<string, string?> config)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        return new ConfigurationProviderAuth(configuration);
    }

    [Fact]
    public async Task WhenKeyExists_ReturnsSuccessWithToken()
    {
        // Arrange
        IProviderAuth sut = BuildSut(new Dictionary<string, string?>
        {
            ["GITHUB_TOKEN"] = "ghp_abc123",
        });

        // Act
        Result<string> result = await sut.GetTokenAsync("GITHUB_TOKEN", CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.ShouldBe("ghp_abc123");
    }

    [Fact]
    public async Task WhenKeyIsMissing_ReturnsFailure()
    {
        // Arrange
        IProviderAuth sut = BuildSut(new Dictionary<string, string?>());

        // Act
        Result<string> result = await sut.GetTokenAsync("GITHUB_TOKEN", CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenKeyIsEmpty_ReturnsFailure()
    {
        // Arrange
        IProviderAuth sut = BuildSut(new Dictionary<string, string?>
        {
            ["GITHUB_TOKEN"] = string.Empty,
        });

        // Act
        Result<string> result = await sut.GetTokenAsync("GITHUB_TOKEN", CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
