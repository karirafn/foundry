using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class Create
{
    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("octocat/hello-world")).Value;

    [Fact]
    public void WhenAllParametersAreValid_ReturnsMonitoredRepositoryWithCorrectProperties()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;
        AccountId accountId = AccountId.New();
        TimeSpan pollInterval = TimeSpan.FromMinutes(5);

        // Act
        MonitoredRepository repository = MonitoredRepository.Create(slug, accountId, pollInterval);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.Slug.ShouldBe(slug),
            () => repository.AccountId.ShouldBe(accountId),
            () => repository.PollInterval.ShouldBe(pollInterval),
            () => repository.IsActive.ShouldBeTrue(),
            () => repository.LastPolledAt.ShouldBeNull());
    }

    [Fact]
    public void WhenCreatedWithNullPollInterval_HasNullPollInterval()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;
        AccountId accountId = AccountId.New();

        // Act
        MonitoredRepository repository = MonitoredRepository.Create(slug, accountId, null);

        // Assert
        repository.PollInterval.ShouldBeNull();
    }

    [Fact]
    public void WhenCreated_AssignsNewId()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;
        AccountId accountId = AccountId.New();

        // Act
        MonitoredRepository a = MonitoredRepository.Create(slug, accountId, null);
        MonitoredRepository b = MonitoredRepository.Create(slug, accountId, null);

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }
}
