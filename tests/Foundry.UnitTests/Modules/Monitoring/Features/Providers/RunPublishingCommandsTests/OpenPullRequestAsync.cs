using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.RunPublishingCommandsTests;

public sealed class OpenPullRequestAsync : RunPublishingCommandsTestBase
{
    private Task<Result<string>> OpenAsync(MonitoredRepositoryId repositoryId) =>
        Sut.OpenPullRequestAsync(
            repositoryId, "feat/42-add-retry", "feat: add retry", "Closes #42", TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenTheProviderOpensIt_ReturnsTheWebUrl()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();

        // Act
        Result<string> result = await OpenAsync(repositoryId);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ValueOrThrow().ShouldBe("https://github.com/owner/repo/pull/1"),
            () => Provider.Calls.ShouldBe(["create-pr owner/repo feat/42-add-retry feat: add retry Closes #42"]));
    }

    [Fact]
    public async Task WhenTheProviderFailsClassified_ReturnsTheFailureUnchanged()
    {
        // Arrange
        Error error = RunPublishingErrors.Permanent("422");
        Provider.PullRequest = Result<string>.Fail(error);

        // Act
        Result<string> result = await OpenAsync(await SeedRepoAsync());

        // Assert
        result.ErrorOrThrow().ShouldBe(error);
    }

    [Fact]
    public async Task WhenTheProviderFailsUnclassified_FailsTransientKeepingTheMessage()
    {
        // Arrange
        Provider.PullRequest = Result<string>.Fail(new Error("GitHub.UnexpectedStatusCode", "returned 502"));

        // Act
        Result<string> result = await OpenAsync(await SeedRepoAsync());

        // Assert
        Error error = result.ErrorOrThrow();
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(RunPublishingErrors.TransientCode),
            () => error.Message.ShouldContain("returned 502"));
    }

    [Fact]
    public async Task WhenTheProviderFailsNotFound_FailsPermanent()
    {
        // Arrange
        Provider.PullRequest = Result<string>.Fail(
            new Error("GitHub.UnexpectedStatusCode", "returned 404") { Kind = ErrorKind.NotFound });

        // Act
        Result<string> result = await OpenAsync(await SeedRepoAsync());

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenTheProviderCannotBeReached_FailsTransient()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        Provider.ThrowOnCall = new HttpRequestException("connection refused");

        // Act
        Result<string> result = await OpenAsync(repositoryId);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.TransientCode);
    }

    [Fact]
    public async Task WhenTheRepositoryIsUnknown_FailsPermanentWithoutCallingTheProvider()
    {
        // Act
        Result<string> result = await OpenAsync(MonitoredRepositoryId.New());

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => Provider.Calls.ShouldBeEmpty());
    }
}
