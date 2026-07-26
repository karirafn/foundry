using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Accounts.GetTokenRequirementsTests;

public sealed class HandleAsync
{
    private static GetTokenRequirements.Handler BuildHandler() => new();

    [Fact]
    public async Task WhenProviderIsGitHub_ReturnsRequirements()
    {
        // Arrange
        GetTokenRequirements.Handler handler = BuildHandler();
        GetTokenRequirements.Query query = new("github");

        // Act
        Result<TokenRequirements> result = await handler.HandleAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        TokenRequirements requirements = result
            .ShouldBeOfType<Result<TokenRequirements>.Success>()
            .Value;

        requirements.ShouldSatisfyAllConditions(
            () => requirements.ProviderType.ShouldBe("github"),
            () => requirements.TokenTypeLabel.ShouldBe("GitHub classic personal access token"),
            () => requirements.Scopes.ShouldBe(["repo"]));
    }

    [Fact]
    public async Task WhenProviderIsGitLab_ReturnsRequirements()
    {
        // Arrange
        GetTokenRequirements.Handler handler = BuildHandler();
        GetTokenRequirements.Query query = new("gitlab");

        // Act
        Result<TokenRequirements> result = await handler.HandleAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        TokenRequirements requirements = result
            .ShouldBeOfType<Result<TokenRequirements>.Success>()
            .Value;

        requirements.ShouldSatisfyAllConditions(
            () => requirements.ProviderType.ShouldBe("gitlab"),
            () => requirements.TokenTypeLabel.ShouldBe("GitLab personal access token"),
            () => requirements.Scopes.ShouldBe(["api"]));
    }

    [Fact]
    public async Task WhenProviderIsUnknown_ReturnsFailure()
    {
        // Arrange
        GetTokenRequirements.Handler handler = BuildHandler();
        GetTokenRequirements.Query query = new("bitbucket");

        // Act
        Result<TokenRequirements> result = await handler.HandleAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Result<TokenRequirements>.Failure failure = result.ShouldBeOfType<Result<TokenRequirements>.Failure>();
        failure.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }
}
