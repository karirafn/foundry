using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Accounts.Tokens.GetTokenRequirementsTests;

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
            () => requirements.TokenTypeLabel.ShouldBe("GitHub fine-grained personal access token"),
            () => requirements.Scopes.ShouldBe([
                "Contents (read and write)",
                "Issues (read and write)",
                "Pull requests (read and write)",
                "Workflows (write)",
                "Metadata (read)"]),
            () => requirements.ResourceOwnerHint.ShouldNotBeNull());
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
            () => requirements.Scopes.ShouldBe(["api"]),
            () => requirements.ResourceOwnerHint.ShouldBeNull());
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
