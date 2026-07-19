using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Persistence.MonitoredRepositoryEligibilityTests;

public sealed class WhenEligibilityIsIneligible : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenEligibilityIsIneligible()
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
        IReadOnlyList<EligibilityViolation> violations =
        [
            EligibilityViolation.AllowDirectPushes(),
            EligibilityViolation.AllowForcePushes(),
        ];
        MonitoredRepositoryId repositoryId = await SeedRepositoryWithEligibilityAsync(
            accountId,
            "owner/ineligible-repo",
            new RepositoryEligibility.Ineligible(violations));

        // Act
        MonitoredRepository? reloaded = await ReloadRepositoryAsync(repositoryId);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
    }

    [Fact]
    public async Task ViolationsArePreserved()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub 2");
        IReadOnlyList<EligibilityViolation> violations =
        [
            EligibilityViolation.AllowDirectPushes(),
            EligibilityViolation.AllowForcePushes(),
        ];
        MonitoredRepositoryId repositoryId = await SeedRepositoryWithEligibilityAsync(
            accountId,
            "owner/ineligible-repo-2",
            new RepositoryEligibility.Ineligible(violations));

        // Act
        MonitoredRepository? reloaded = await ReloadRepositoryAsync(repositoryId);

        // Assert
        reloaded.ShouldNotBeNull();
        RepositoryEligibility.Ineligible ineligible = reloaded.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.ShouldSatisfyAllConditions(
            () => ineligible.Violations.Count.ShouldBe(2),
            () => ineligible.Violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowDirectPushesRule),
            () => ineligible.Violations.ShouldContain(v => v.Rule == EligibilityViolation.AllowForcePushesRule));
    }

    [Fact]
    public async Task EligibilityStatusIsIneligible()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub 3");
        IReadOnlyList<EligibilityViolation> violations = [EligibilityViolation.AllowDeletion()];
        MonitoredRepositoryId repositoryId = await SeedRepositoryWithEligibilityAsync(
            accountId,
            "owner/ineligible-repo-3",
            new RepositoryEligibility.Ineligible(violations));

        // Act
        MonitoredRepository? reloaded = await ReloadRepositoryAsync(repositoryId);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.EligibilityStatus.ShouldBe("ineligible");
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
        MonitoredRepository repository = MonitoredRepository.Create(repositorySlug, "github.com", pollInterval: null);
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
