using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Persistence.MonitoredRepositoryEligibilityTests;

public sealed class WhenEligibilityIsEligible : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenEligibilityIsEligible()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task EligibilityRoundTripsWithCorrectType()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        MonitoredRepositoryId repositoryId = await SeedRepositoryWithEligibilityAsync(
            accountId,
            new RepositoryEligibility.Eligible());

        // Act
        MonitoredRepository? reloaded = await ReloadRepositoryAsync(repositoryId);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.Eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>();
    }

    [Fact]
    public async Task EligibilityStatusIsEligible()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub 2");
        MonitoredRepositoryId repositoryId = await SeedRepositoryWithEligibilityAsync(
            accountId,
            new RepositoryEligibility.Eligible());

        // Act
        MonitoredRepository? reloaded = await ReloadRepositoryAsync(repositoryId);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.EligibilityStatus.ShouldBe("eligible");
    }

    private async Task<MonitoredRepositoryId> SeedRepositoryWithEligibilityAsync(
        Guid accountId,
        RepositoryEligibility eligibility)
    {
        // No endpoint exists to set eligibility — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        RepositorySlug slug = RepositorySlug.Create("owner/eligible-repo").ValueOrThrow();
        MonitoredRepository repository = MonitoredRepository.Create(slug, CredentialId.From(accountId), "github.com", pollInterval: null);
        repository.SetEligibility(eligibility);

        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repository.Id;
    }

    private async Task<MonitoredRepository?> ReloadRepositoryAsync(MonitoredRepositoryId repositoryId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        return await dbContext.Set<MonitoredRepository>()
            .FirstOrDefaultAsync(
                r => r.Id == repositoryId,
                TestContext.Current.CancellationToken);
    }
}
