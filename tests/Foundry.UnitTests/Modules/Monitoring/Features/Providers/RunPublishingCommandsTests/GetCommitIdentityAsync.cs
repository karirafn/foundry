using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.RunPublishingCommandsTests;

public sealed class GetCommitIdentityAsync : RunPublishingCommandsTestBase
{
    [Fact]
    public async Task WhenTheProviderResolvesTheAccount_ReturnsItsIdentity()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();

        // Act
        Result<CommitIdentity> result = await Sut.GetCommitIdentityAsync(
            repositoryId, TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBe(new CommitIdentity("Octo", "1+octo@users.noreply.github.com"));
    }

    [Fact]
    public async Task WhenTheProviderFails_ReturnsAClassifiedFailure()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        Provider.Identity = Result<CommitIdentity>.Fail(RunPublishingErrors.Permanent("401"));

        // Act
        Result<CommitIdentity> result = await Sut.GetCommitIdentityAsync(
            repositoryId, TestContext.Current.CancellationToken);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }
}
