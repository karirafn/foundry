using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.PostExitProviderQueriesTests;

public sealed class GetOpenBlockersAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CountingIssueProvider _provider = new(dependencies: [4, 9]);
    private readonly IIssueBlockerQueries _sut;

    public GetOpenBlockersAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();

        _sut = new PostExitProviderQueries(
            _dbContext,
            new StubProviderFactory(_provider),
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<MonitoredRepositoryId> SeedRepoAsync(bool withCredential = true)
    {
        if (withCredential)
        {
            GitHubCredential credential = GitHubCredential.Create(
                "my-org",
                "ghp_test_token",
                BaseUrl.Create("https://github.com").ValueOrThrow());
            credential.SetNamespaces([Namespace.Create("owner").ValueOrThrow()]);
            _dbContext.Set<Credential>().Add(credential);
        }

        RepositorySlug slug = RepositorySlug.Create("owner/repo").ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(slug, "github.com", null).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repo.Id;
    }

    [Fact]
    public async Task WhenRepositoryResolves_ReturnsProviderOpenBlockers()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync();

        // Act
        Result<IReadOnlyList<int>> result = await _sut.GetOpenBlockersAsync(
            repoId,
            issueNumber: 12,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>().Value.ShouldBe([4, 9]);
        _provider.GetDependenciesCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenRepositoryNotFound_ReturnsFailureWithoutProviderCall()
    {
        // Act
        Result<IReadOnlyList<int>> result = await _sut.GetOpenBlockersAsync(
            MonitoredRepositoryId.New(),
            issueNumber: 12,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IReadOnlyList<int>>.Failure>()
            .Error.Code.ShouldBe("PostExitProviderQueries.RepositoryNotFound");
        _provider.TotalCalls.ShouldBe(0);
    }

    [Fact]
    public async Task WhenNoCredentialCoversRepository_ReturnsFailureWithoutProviderCall()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync(withCredential: false);

        // Act
        Result<IReadOnlyList<int>> result = await _sut.GetOpenBlockersAsync(
            repoId,
            issueNumber: 12,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IReadOnlyList<int>>.Failure>()
            .Error.Code.ShouldBe("PostExitProviderQueries.CredentialNotFound");
        _provider.TotalCalls.ShouldBe(0);
    }

    private sealed class StubProviderFactory(IIssueProvider provider) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) => provider;
    }
}
