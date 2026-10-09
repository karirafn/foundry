using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Eligibility.RepositoryDispatchQueriesTests;

public sealed class GetDispatchInfoAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public GetDispatchInfoAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<MonitoredRepositoryId> SeedRepoAsync(string slug = "owner/repo")
    {
        RepositorySlug repoSlug = RepositorySlug.Create(slug).ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(repoSlug, "github.com", null).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repo.Id;
    }

    private RepositoryDispatchQueries BuildSut(ICredentialResolver resolver)
        => new(_dbContext, resolver);

    [Fact]
    public async Task WhenCredentialTokenIsUnreadable_ReturnsNullWithoutCallingProvider()
    {
        // Arrange — seed a real repo so the repo lookup succeeds; inject a stub resolver
        // returning an Unreadable credential (state only reachable at EF materialization time).
        MonitoredRepositoryId repoId = await SeedRepoAsync();
        GitHubCredential unreadableCredential = GitHubCredential.CreateWithUnreadableToken(
            "my-org",
            BaseUrl.Create("https://github.com").ValueOrThrow());

        RepositoryDispatchQueries sut = BuildSut(new StubCredentialResolver(unreadableCredential));

        // Act
        RepositoryDispatchInfo? result = await sut.GetDispatchInfoAsync(
            repoId,
            TestContext.Current.CancellationToken);

        // Assert — unreadable token short-circuits to null; no provider request sent
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenRepositoryNotFound_ReturnsNull()
    {
        // Arrange
        RepositoryDispatchQueries sut = BuildSut(new StubCredentialResolver(null));

        // Act
        RepositoryDispatchInfo? result = await sut.GetDispatchInfoAsync(
            MonitoredRepositoryId.New(),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    private sealed class StubCredentialResolver(Credential? credential) : ICredentialResolver
    {
        public Task<Credential?> ResolveAsync(string host, RepositorySlug slug, CancellationToken cancellationToken)
            => Task.FromResult(credential);
    }
}
