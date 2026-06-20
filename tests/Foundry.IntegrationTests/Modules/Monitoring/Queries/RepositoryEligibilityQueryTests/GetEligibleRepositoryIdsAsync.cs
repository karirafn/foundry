using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Queries.RepositoryEligibilityQueryTests;

public sealed class GetEligibleRepositoryIdsAsync : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public GetEligibleRepositoryIdsAsync()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenRepositoriesHaveMixedEligibility_ReturnsOnlyEligibleIds()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);

        Guid eligibleId = await SeedRepositoryAsync(accountId, "owner/eligible", new RepositoryEligibility.Eligible());
        Guid ineligibleId = await SeedRepositoryAsync(
            accountId,
            "owner/ineligible",
            new RepositoryEligibility.Ineligible(
                [EligibilityViolation.AllowDirectPushes()]));
        Guid unreachableId = await SeedRepositoryAsync(accountId, "owner/unreachable", new RepositoryEligibility.Unreachable());
        Guid nullEligibilityId = await SeedRepositoryAsync(accountId, "owner/null-eligibility", eligibility: null);

        // Act
        IReadOnlySet<Guid> result = await QueryEligibleIdsAsync(
            [eligibleId, ineligibleId, unreachableId, nullEligibilityId]);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Count.ShouldBe(1),
            () => result.ShouldContain(eligibleId),
            () => result.ShouldNotContain(ineligibleId),
            () => result.ShouldNotContain(unreachableId),
            () => result.ShouldNotContain(nullEligibilityId));
    }

    [Fact]
    public async Task WhenRepositoryIdNotInList_IsNotReturned()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org 2");
        await SeedRepositoryAsync(accountId, "owner/eligible-2", new RepositoryEligibility.Eligible());
        Guid unrelatedId = Guid.NewGuid();

        // Act
        IReadOnlySet<Guid> result = await QueryEligibleIdsAsync([unrelatedId]);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenEmptyListProvided_ReturnsEmptySet()
    {
        // Act
        IReadOnlySet<Guid> result = await QueryEligibleIdsAsync([]);

        // Assert
        result.ShouldBeEmpty();
    }

    private async Task<IReadOnlySet<Guid>> QueryEligibleIdsAsync(IReadOnlyCollection<Guid> repositoryIds)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IRepositoryEligibilityQuery query = scope.ServiceProvider.GetRequiredService<IRepositoryEligibilityQuery>();

        return await query.GetEligibleRepositoryIdsAsync(repositoryIds, TestContext.Current.CancellationToken);
    }

    private async Task<Guid> SeedRepositoryAsync(
        Guid accountId,
        string slug,
        RepositoryEligibility? eligibility)
    {
        // No endpoint exists to set eligibility — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        RepositorySlug repositorySlug = ((Result<RepositorySlug>.Success)RepositorySlug.Create(slug)).Value;
        MonitoredRepository repository = MonitoredRepository.Create(
            repositorySlug,
            AccountId.From(accountId),
            pollInterval: null);

        if (eligibility is not null)
        {
            repository.SetEligibility(eligibility);
        }

        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repository.Id.Value;
    }
}
