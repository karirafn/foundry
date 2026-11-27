using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.RunPublishingCommandsTests;

public sealed class UpdatePullRequestBodyAsync : RunPublishingCommandsTestBase
{
    private Task<Result> UpdateAsync(MonitoredRepositoryId repositoryId) =>
        Sut.UpdatePullRequestBodyAsync(
            repositoryId, "https://github.com/owner/repo/pull/7", "Closes #42", TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenTheProviderAcceptsIt_Succeeds()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();

        // Act
        Result result = await UpdateAsync(repositoryId);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Success>(),
            () => Provider.Calls.ShouldBe(["update-pr-body owner/repo https://github.com/owner/repo/pull/7 Closes #42"]));
    }

    [Fact]
    public async Task WhenTheProviderRefusesTheToken_ReturnsTheRefusalUnchanged()
    {
        // Arrange
        Error error = RunPublishingErrors.Refused("returned 403");
        Provider.BodyUpdate = Result.Fail(error);

        // Act
        Result result = await UpdateAsync(await SeedRepoAsync());

        // Assert
        result.ErrorOrThrow().ShouldBe(error);
    }

    [Fact]
    public async Task WhenTheProviderCannotBeReached_FailsTransient()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        Provider.ThrowOnCall = new HttpRequestException("connection refused");

        // Act
        Result result = await UpdateAsync(repositoryId);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.TransientCode);
    }

    [Fact]
    public async Task WhenTheRepositoryIsUnknown_FailsPermanentWithoutCallingTheProvider()
    {
        // Act
        Result result = await UpdateAsync(MonitoredRepositoryId.New());

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => Provider.Calls.ShouldBeEmpty());
    }
}
