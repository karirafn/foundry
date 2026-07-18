using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Queries.RepositoryEligibilityQueryTests;

public sealed class GetEligibleRepositoriesAsync : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public GetEligibleRepositoriesAsync()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenRepositoriesHaveMixedEligibility_ReturnsOnlyEligibleRepositories()
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
        IReadOnlyList<EligibleRepository> result = await QueryEligibleRepositoriesAsync(
            [eligibleId, ineligibleId, unreachableId, nullEligibilityId]);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Count.ShouldBe(1),
            () => result.ShouldContain(r => r.Id == eligibleId),
            () => result.ShouldNotContain(r => r.Id == ineligibleId),
            () => result.ShouldNotContain(r => r.Id == unreachableId),
            () => result.ShouldNotContain(r => r.Id == nullEligibilityId));
    }

    [Fact]
    public async Task WhenRepositoryIdNotInList_IsNotReturned()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org 2");
        await SeedRepositoryAsync(accountId, "owner/eligible-2", new RepositoryEligibility.Eligible());
        Guid unrelatedId = Guid.NewGuid();

        // Act
        IReadOnlyList<EligibleRepository> result = await QueryEligibleRepositoriesAsync([unrelatedId]);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenEmptyListProvided_ReturnsEmptyList()
    {
        // Act
        IReadOnlyList<EligibleRepository> result = await QueryEligibleRepositoriesAsync([]);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenEligibleRepositoryExists_IncludesPosition()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org 3");
        Guid firstId = await SeedRepositoryAsync(accountId, "owner/first", new RepositoryEligibility.Eligible());
        Guid secondId = await SeedRepositoryAsync(accountId, "owner/second", new RepositoryEligibility.Eligible());

        // Act
        IReadOnlyList<EligibleRepository> result = await QueryEligibleRepositoriesAsync([firstId, secondId]);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain(r => r.Id == firstId);
        result.ShouldContain(r => r.Id == secondId);
        int firstPosition = result.Single(r => r.Id == firstId).Position;
        int secondPosition = result.Single(r => r.Id == secondId).Position;
        firstPosition.ShouldBeLessThan(secondPosition);
    }

    private async Task<IReadOnlyList<EligibleRepository>> QueryEligibleRepositoriesAsync(
        IReadOnlyCollection<Guid> repositoryIds)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IRepositoryEligibilityQuery query = scope.ServiceProvider.GetRequiredService<IRepositoryEligibilityQuery>();

        return await query.GetEligibleRepositoriesAsync(repositoryIds, TestContext.Current.CancellationToken);
    }

    private async Task<Guid> SeedRepositoryAsync(
        Guid accountId,
        string slug,
        RepositoryEligibility? eligibility)
    {
        // No endpoint exists to set eligibility — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        RepositorySlug repositorySlug = RepositorySlug.Create(slug).ValueOrThrow();
        int position = await dbContext.Set<MonitoredRepository>().CountAsync(TestContext.Current.CancellationToken);
        MonitoredRepository repository = MonitoredRepository.Create(
            repositorySlug,
            CredentialId.From(accountId),
            "github.com",
            pollInterval: null,
            position);

        if (eligibility is not null)
        {
            repository.SetEligibility(eligibility);
        }

        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repository.Id.Value;
    }
}
