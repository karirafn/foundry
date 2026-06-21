using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetRepositoriesTests;

public sealed class WhenRepositoryHasEligibility : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRepositoryHasEligibility()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenRepositoryIsEligible_SummaryIncludesEligibleStatus()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org A");
        await SeedRepositoryWithEligibilityAsync(
            accountId,
            "owner/eligible-repo",
            new RepositoryEligibility.Eligible());

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        RepositorySummary repo = repositories.ShouldNotBeNull().ShouldHaveSingleItem();
        repo.Eligibility.ShouldNotBeNull();
        repo.Eligibility.ShouldSatisfyAllConditions(
            () => repo.Eligibility.Status.ShouldBe("eligible"),
            () => repo.Eligibility.Violations.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenRepositoryIsIneligible_SummaryIncludesViolations()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org B");
        RepositoryEligibility.Ineligible ineligible = new(
            [EligibilityViolation.AllowDirectPushes(), EligibilityViolation.AllowForcePushes()]);
        await SeedRepositoryWithEligibilityAsync(
            accountId,
            "owner/ineligible-repo",
            ineligible);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        RepositorySummary repo = repositories.ShouldNotBeNull().ShouldHaveSingleItem();
        repo.Eligibility.ShouldNotBeNull();
        repo.Eligibility.ShouldSatisfyAllConditions(
            () => repo.Eligibility.Status.ShouldBe("ineligible"),
            () => repo.Eligibility.Violations.Count.ShouldBe(2),
            () => repo.Eligibility.Violations.ShouldContain(
                v => v.Rule == EligibilityViolationInfo.AllowDirectPushesRule),
            () => repo.Eligibility.Violations.ShouldContain(
                v => v.Rule == EligibilityViolationInfo.AllowForcePushesRule));
    }

    [Fact]
    public async Task WhenRepositoryHasNoExplicitEligibility_SummaryEligibilityIsUnreachable()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Org C");

        // Seed without overriding eligibility — MonitoredRepository.Create initializes to Unreachable.
        await SeedRepositoryWithEligibilityAsync(accountId, "owner/no-eligibility-repo", eligibility: null);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        RepositorySummary repo = repositories.ShouldNotBeNull().ShouldHaveSingleItem();
        repo.Eligibility.ShouldNotBeNull();
        repo.Eligibility.Status.ShouldBe("unreachable");
    }

    private async Task SeedRepositoryWithEligibilityAsync(
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
            "github.com",
            pollInterval: null);

        if (eligibility is not null)
        {
            repository.SetEligibility(eligibility);
        }

        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
