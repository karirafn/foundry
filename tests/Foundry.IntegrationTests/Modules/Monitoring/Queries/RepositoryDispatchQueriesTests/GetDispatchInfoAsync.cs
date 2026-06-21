using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;
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
    public async Task WhenAccountIsGitHub_ProjectsGithubProviderType()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId);
        MonitoredRepositoryId monitoredRepositoryId = MonitoredRepositoryId.From(repositoryId);

        using IServiceScope scope = _factory.Services.CreateScope();
        IRepositoryDispatchQueries sut = scope.ServiceProvider.GetRequiredService<IRepositoryDispatchQueries>();

        // Act
        RepositoryDispatchInfo? result = await sut.GetDispatchInfoAsync(
            monitoredRepositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        RepositoryDispatchInfo info = result.ShouldNotBeNull();
        info.ProviderType.ShouldBe("github");
    }

    [Fact]
    public async Task WhenAccountIsGitLab_ProjectsGitlabProviderType()
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
        info.ProviderType.ShouldBe("gitlab");
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

        RepositorySlug repositorySlug = ((Result<RepositorySlug>.Success)RepositorySlug.Create(slug)).Value;
        MonitoredRepository repository = MonitoredRepository.Create(
            repositorySlug,
            AccountId.From(accountId),
            "gitlab.com",
            pollInterval: null);

        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repository.Id.Value;
    }
}
