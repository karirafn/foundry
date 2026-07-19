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

public sealed class GetEligibilityAsync : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public GetEligibilityAsync()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenRepositoryIsEligible_ReturnsEligibleInfo()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await SeedRepositoryAsync(accountId, "owner/eligible-repo", new RepositoryEligibility.Eligible());

        // Act
        RepositoryEligibilityInfo? result = await QueryEligibilityAsync(repositoryId);

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("eligible"),
            () => info.Violations.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenRepositoryIsIneligible_ReturnsIneligibleInfoWithViolations()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org 2");
        RepositoryEligibility.Ineligible ineligible = new(
            [EligibilityViolation.AllowDirectPushes(), EligibilityViolation.AllowForcePushes()]);
        Guid repositoryId = await SeedRepositoryAsync(accountId, "owner/ineligible-repo", ineligible);

        // Act
        RepositoryEligibilityInfo? result = await QueryEligibilityAsync(repositoryId);

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("ineligible"),
            () => info.Violations.Count.ShouldBe(2),
            () => info.Violations.ShouldContain(v => v.Rule == EligibilityViolationInfo.AllowDirectPushesRule),
            () => info.Violations.ShouldContain(v => v.Rule == EligibilityViolationInfo.AllowForcePushesRule));
    }

    [Fact]
    public async Task WhenRepositoryIsUnreachable_ReturnsUnreachableInfo()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org 3");
        Guid repositoryId = await SeedRepositoryAsync(
            accountId, "owner/unreachable-repo", new RepositoryEligibility.Unreachable());

        // Act
        RepositoryEligibilityInfo? result = await QueryEligibilityAsync(repositoryId);

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("unreachable"),
            () => info.Violations.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenRepositoryDoesNotExist_ReturnsNull()
    {
        // Act
        RepositoryEligibilityInfo? result = await QueryEligibilityAsync(Guid.NewGuid());

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenRepositoryHasNoEligibilitySet_ReturnsUnreachable()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org 4");
        Guid repositoryId = await SeedRepositoryAsync(accountId, "owner/no-eligibility", eligibility: null);

        // Act
        RepositoryEligibilityInfo? result = await QueryEligibilityAsync(repositoryId);

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("unreachable"),
            () => info.Violations.ShouldBeEmpty());
    }

    private async Task<RepositoryEligibilityInfo?> QueryEligibilityAsync(Guid repositoryId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IRepositoryEligibilityQuery query = scope.ServiceProvider.GetRequiredService<IRepositoryEligibilityQuery>();

        return await query.GetEligibilityAsync(repositoryId, TestContext.Current.CancellationToken);
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
        MonitoredRepository repository = MonitoredRepository.Create(
            repositorySlug,
            "github.com",
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
