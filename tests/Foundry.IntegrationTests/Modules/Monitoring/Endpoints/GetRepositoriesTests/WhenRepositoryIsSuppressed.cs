using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetRepositoriesTests;

public sealed class WhenRepositoryIsSuppressed : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRepositoryIsSuppressed()
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
    public async Task WhenSuppressionIsSet_ProjectsUntrackSuppressedSince()
    {
        // Arrange
        DateTimeOffset suppressedAt = new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Suppressed Org");
        await SeedRepositoryWithSuppressionAsync(accountId, "owner/suppressed-repo", suppressedAt);

        // No endpoint exposes namespace seeding directly — seed via DbContext to simulate resolver state.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        RepositorySummary repository = repositories.ShouldNotBeNull().ShouldHaveSingleItem();
        repository.UntrackSuppressedSince.ShouldBe(suppressedAt);
    }

    [Fact]
    public async Task WhenSuppressionIsNotSet_UntrackSuppressedSinceIsNull()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Unsuppressed Org");
        await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/unsuppressed-repo");

        // No endpoint exposes namespace seeding directly — seed via DbContext to simulate resolver state.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<RepositorySummary>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<RepositorySummary>>(TestContext.Current.CancellationToken);
        RepositorySummary repository = repositories.ShouldNotBeNull().ShouldHaveSingleItem();
        repository.UntrackSuppressedSince.ShouldBeNull();
    }

    private async Task SeedRepositoryWithSuppressionAsync(
        Guid accountId,
        string slug,
        DateTimeOffset suppressedAt)
    {
        // No endpoint exists to set untrack suppression — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        RepositorySlug repositorySlug = RepositorySlug.Create(slug).ValueOrThrow();

        // Set a namespace on the credential so the resolver can match this repository.
        // No endpoint exposes namespace seeding directly — seed via DbContext to simulate resolver state.
        Credential? credential = await dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == CredentialId.From(accountId), TestContext.Current.CancellationToken);

        if (credential is not null)
        {
            credential.SetNamespaces([Namespace.Create(repositorySlug.Owner).ValueOrThrow()]);
        }

        MonitoredRepository repository = MonitoredRepository.Create(
            repositorySlug,
            "github.com",
            pollInterval: null);

        repository.SuppressUntracking(suppressedAt);

        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
