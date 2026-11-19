using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.PackageSourceProxy;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        => new(
            _dbContext,
            resolver,
            Options.Create(new PackageSourceProxyOptions()),
            NullLogger<RepositoryDispatchQueries>.Instance);

    [Fact]
    public async Task WhenCredentialTokenIsUnreadable_ReturnsNullWithoutCallingProvider()
    {
        // Arrange — seed a real repo so the repo lookup succeeds; inject a stub resolver
        // returning an Unreadable credential (state only reachable at EF materialization time).
        MonitoredRepositoryId repoId = await SeedRepoAsync();
        GitHubCredential unreadableCredential = GitHubCredential.CreateWithUnreadableToken(
            AccountName.Create("my-org").ValueOrThrow(),
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
    public async Task WhenCredentialTokenIsInvalid_ReturnsNull()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedRepoAsync();
        GitHubCredential invalidCredential = GitHubCredential.Create(
            AccountName.Create("my-org").ValueOrThrow(),
            new IssuedToken(string.Concat("ghp_", "rejected"), new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());
        invalidCredential.MarkTokenInvalid(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));

        RepositoryDispatchQueries sut = BuildSut(new StubCredentialResolver(invalidCredential));

        // Act
        RepositoryDispatchInfo? result = await sut.GetDispatchInfoAsync(
            repoId,
            TestContext.Current.CancellationToken);

        // Assert
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

    [Fact]
    public async Task WhenRepositoryHasWebhook_DispatchInfoDoesNotCarryTheWebhookSecret()
    {
        // Arrange — the repository's webhook secret must not reach a worker, or the worker could forge deliveries.
        const string AccountToken = "ghp_worker_token";
        MonitoredRepositoryId repoId = await SeedRepoAsync();
        MonitoredRepository repository = await _dbContext.Set<MonitoredRepository>()
            .SingleAsync(r => r.Id == repoId, TestContext.Current.CancellationToken);
        string webhookSecret = repository.EnsureWebhookSecret();
        repository.MarkWebhookRegistered();
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        GitHubCredential credential = GitHubCredential.Create(
            AccountName.Create("owner").ValueOrThrow(),
            new IssuedToken(AccountToken, new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());

        RepositoryDispatchQueries sut = BuildSut(new StubCredentialResolver(credential));

        // Act
        RepositoryDispatchInfo? result = await sut.GetDispatchInfoAsync(
            repoId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull();
        result.AccountToken.ShouldBe(AccountToken);
        string serialized = System.Text.Json.JsonSerializer.Serialize(result);
        serialized.ShouldNotContain(webhookSecret);
    }

    private sealed class StubCredentialResolver(Credential? credential) : ICredentialResolver
    {
        public Task<Credential?> ResolveAsync(string host, RepositorySlug slug, CancellationToken cancellationToken)
            => Task.FromResult(credential);
    }
}
