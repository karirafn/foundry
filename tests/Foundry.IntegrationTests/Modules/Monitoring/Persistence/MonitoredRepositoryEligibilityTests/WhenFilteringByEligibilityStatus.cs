using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Persistence.MonitoredRepositoryEligibilityTests;

public sealed class WhenFilteringByEligibilityStatus : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenFilteringByEligibilityStatus()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task CanQueryEligibleRepositoriesViaLinq()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        await SeedRepositoryWithEligibilityAsync(accountId, "owner/eligible", new RepositoryEligibility.Eligible());
        IReadOnlyList<EligibilityViolation> violations = [EligibilityViolation.AllowDeletion()];
        await SeedRepositoryWithEligibilityAsync(accountId, "owner/ineligible", new RepositoryEligibility.Ineligible(violations));
        await SeedRepositoryWithEligibilityAsync(accountId, "owner/unreachable", new RepositoryEligibility.Unreachable());

        // Act
        IReadOnlyList<MonitoredRepository> eligibleRepos = await QueryByStatusAsync("eligible");

        // Assert
        eligibleRepos.Count.ShouldBe(1);
        eligibleRepos[0].EligibilityStatus.ShouldBe("eligible");
    }

    [Fact]
    public async Task CanQueryIneligibleRepositoriesViaLinq()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub 2");
        await SeedRepositoryWithEligibilityAsync(accountId, "owner/eligible-2", new RepositoryEligibility.Eligible());
        IReadOnlyList<EligibilityViolation> violations =
        [
            EligibilityViolation.AllowDirectPushes(),
            EligibilityViolation.AllowForcePushes(),
        ];
        await SeedRepositoryWithEligibilityAsync(
            accountId,
            "owner/ineligible-2",
            new RepositoryEligibility.Ineligible(violations));
        await SeedRepositoryWithEligibilityAsync(accountId, "owner/unreachable-2", new RepositoryEligibility.Unreachable());

        // Act
        IReadOnlyList<MonitoredRepository> ineligibleRepos = await QueryByStatusAsync("ineligible");

        // Assert
        ineligibleRepos.Count.ShouldBe(1);
        ineligibleRepos[0].EligibilityStatus.ShouldBe("ineligible");
    }

    private async Task<MonitoredRepositoryId> SeedRepositoryWithEligibilityAsync(
        Guid accountId,
        string slug,
        RepositoryEligibility eligibility)
    {
        // No endpoint exists to set eligibility — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        RepositorySlug repositorySlug = RepositorySlug.Create(slug).ValueOrThrow();
        int position = await dbContext.Set<MonitoredRepository>().CountAsync(TestContext.Current.CancellationToken);
        MonitoredRepository repository = MonitoredRepository.Create(repositorySlug, AccountId.From(accountId), "github.com", pollInterval: null, position);
        repository.SetEligibility(eligibility);

        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repository.Id;
    }

    private async Task<IReadOnlyList<MonitoredRepository>> QueryByStatusAsync(string status)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        return await dbContext.Set<MonitoredRepository>()
            .Where(r => r.EligibilityStatus == status)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
