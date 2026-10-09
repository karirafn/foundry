using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.RepositoryMappingsTests;

public sealed class ToSummary
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private const string AccountName = "test-account";
    private const string ProviderType = "github";

    [Fact]
    public void WhenRepoHasOwnPollInterval_PollIntervalIsDefaultIsFalse_AndEffectiveMatchesOwn()
    {
        // Arrange
        TimeSpan ownInterval = TimeSpan.FromSeconds(120);
        MonitoredRepository repo = new MonitoredRepositoryBuilder()
            .WithPollInterval(ownInterval)
            .Build();
        int defaultPollIntervalSeconds = 60;

        // Act
        RepositorySummary summary = RepositoryMappings.ToSummary(
            repo,
            AccountId,
            AccountName,
            ProviderType,
            defaultPollIntervalSeconds);

        // Assert
        summary.ShouldSatisfyAllConditions(
            () => summary.PollIntervalIsDefault.ShouldBeFalse(),
            () => summary.EffectivePollIntervalSeconds.ShouldBe(120));
    }

    [Fact]
    public void WhenRepoHasNullPollInterval_PollIntervalIsDefaultIsTrue_AndEffectiveMatchesDefault()
    {
        // Arrange
        MonitoredRepository repo = new MonitoredRepositoryBuilder()
            .WithPollInterval(null)
            .Build();
        int defaultPollIntervalSeconds = 300;

        // Act
        RepositorySummary summary = RepositoryMappings.ToSummary(
            repo,
            AccountId,
            AccountName,
            ProviderType,
            defaultPollIntervalSeconds);

        // Assert
        summary.ShouldSatisfyAllConditions(
            () => summary.PollIntervalIsDefault.ShouldBeTrue(),
            () => summary.EffectivePollIntervalSeconds.ShouldBe(300));
    }
}
