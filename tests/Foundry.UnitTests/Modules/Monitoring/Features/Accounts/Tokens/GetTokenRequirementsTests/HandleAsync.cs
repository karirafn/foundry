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
            () => requirements.Permissions.ShouldBe([
                new TokenPermissionRequirement("Repository access: Only select repositories", "not-checked"),
                new TokenPermissionRequirement("Contents (read and write)", "probe"),
                new TokenPermissionRequirement("Issues (read and write)", "probe"),
                new TokenPermissionRequirement("Pull requests (read and write)", "probe"),
                new TokenPermissionRequirement("Administration (read and write)", "probe"),
                new TokenPermissionRequirement("Webhooks (read and write)", "probe"),
                new TokenPermissionRequirement("Commit statuses (read)", "probe"),
                new TokenPermissionRequirement("Actions (read and write)", "probe"),
                new TokenPermissionRequirement("Metadata (read)", "always-granted"),
                new TokenPermissionRequirement("Workflows (read and write)", "unverifiable")]),
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
            () => requirements.TokenTypeLabel.ShouldBe("GitLab fine-grained personal access token"),
            () => requirements.Permissions[0].ShouldBe(new TokenPermissionRequirement(
                "Only selected projects (select each monitored project individually)", "granted", "Group and project access")),
            () => requirements.Permissions[^1].ShouldBe(new TokenPermissionRequirement("Maintainer role", "role", "Repository role")),
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
