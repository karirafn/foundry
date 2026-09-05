using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Queries.RepositoryDispatchQueriesTests;

public sealed class GetDispatchInfoAsync : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public GetDispatchInfoAsync()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenAccountIsGitHub_ProjectsGitHubProvider()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId);
        MonitoredRepositoryId monitoredRepositoryId = MonitoredRepositoryId.From(repositoryId);

        // RepositorySeeder uses slug "owner/repo" — seed that namespace so the resolver finds it.
        // No endpoint exposes namespace seeding directly; seed via DbContext to simulate resolver state.
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

        using IServiceScope scope = _factory.Services.CreateScope();
        IRepositoryDispatchQueries sut = scope.ServiceProvider.GetRequiredService<IRepositoryDispatchQueries>();

        // Act
        RepositoryDispatchInfo? result = await sut.GetDispatchInfoAsync(
            monitoredRepositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        RepositoryDispatchInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Provider.ShouldBeOfType<WorkerProvider.GitHub>(),
            () => info.IssueApiUrlBase.ShouldBe("https://api.github.com/repos/owner/repo/issues"));
    }

    [Fact]
    public async Task WhenAccountIsGitLab_ProjectsGitlabProvider()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitLabAccountAsync(_factory, name: "My GitLab 2");
        Guid repositoryId = await SeedRepositoryViaDbContextAsync(accountId, "gitlab-owner/repo");
        MonitoredRepositoryId monitoredRepositoryId = MonitoredRepositoryId.From(repositoryId);

        using IServiceScope scope = _factory.Services.CreateScope();
        IRepositoryDispatchQueries sut = scope.ServiceProvider.GetRequiredService<IRepositoryDispatchQueries>();

        // Act
        RepositoryDispatchInfo? result = await sut.GetDispatchInfoAsync(
            monitoredRepositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        RepositoryDispatchInfo info = result.ShouldNotBeNull();
        info.Provider.ShouldBeOfType<WorkerProvider.GitLab>();
    }

    [Fact]
    public async Task WhenRepositoryDoesNotExist_ReturnsNull()
    {
        // Arrange
        using IServiceScope scope = _factory.Services.CreateScope();
        IRepositoryDispatchQueries sut = scope.ServiceProvider.GetRequiredService<IRepositoryDispatchQueries>();

        // Act
        RepositoryDispatchInfo? result = await sut.GetDispatchInfoAsync(
            MonitoredRepositoryId.New(),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    // Uses DbContext directly because the GitLab account does not support repository creation
    // via the POST endpoint (which validates slug format against GitHub conventions).
    private async Task<Guid> SeedRepositoryViaDbContextAsync(Guid accountId, string slug)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        RepositorySlug repositorySlug = RepositorySlug.Create(slug).ValueOrThrow();

        // Set a namespace on the credential so the resolver can match this repository.
        Credential? credential = await dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == CredentialId.From(accountId), TestContext.Current.CancellationToken);

        if (credential is not null)
        {
            credential.SetNamespaces([Namespace.Create(repositorySlug.Owner).ValueOrThrow()]);
        }

        MonitoredRepository repository = MonitoredRepository.Create(repositorySlug, "gitlab.com", pollInterval: null);

        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repository.Id.Value;
    }
}
