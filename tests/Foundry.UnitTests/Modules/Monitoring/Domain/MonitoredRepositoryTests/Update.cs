using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class Update
{
    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("octocat/hello-world")).Value;

    private static MonitoredRepository CreateRepository(TimeSpan? pollInterval = null) =>
        MonitoredRepository.Create(ValidSlug, AccountId.New(), pollInterval);

    [Fact]
    public void WhenCalled_SetsPollIntervalAndIsActive()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(pollInterval: TimeSpan.FromMinutes(5));
        TimeSpan newPollInterval = TimeSpan.FromMinutes(10);

        // Act
        repository.Update(newPollInterval, isActive: false);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.PollInterval.ShouldBe(newPollInterval),
            () => repository.IsActive.ShouldBeFalse());
    }

    [Fact]
    public void WhenCalled_PreservesSlugAndAccountId()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;
        AccountId accountId = AccountId.New();
        MonitoredRepository repository = MonitoredRepository.Create(slug, accountId, null);

        // Act
        repository.Update(TimeSpan.FromMinutes(15), isActive: true);

        // Assert
        repository.ShouldSatisfyAllConditions(
            () => repository.Slug.ShouldBe(slug),
            () => repository.AccountId.ShouldBe(accountId));
    }

    [Fact]
    public void WhenNullPollIntervalIsPassed_ClearsPollInterval()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(pollInterval: TimeSpan.FromMinutes(5));

        // Act
        repository.Update(null, isActive: true);

        // Assert
        repository.PollInterval.ShouldBeNull();
    }
}
