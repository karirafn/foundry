using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.GetUnclaimedRepositoriesHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private int _nextPosition;

    public HandleAsync()
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

    private async Task<MonitoredRepository> SeedRepositoryAsync(string host, string slug)
    {
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            host,
            null,
            _nextPosition++).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repository);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return repository;
    }

    private async Task SeedGitHubAccountAsync(string ns)
    {
        GitHubCredential credential = GitHubCredential.Create(
            AccountName.Create(ns).ValueOrThrow(),
            new IssuedToken("github_pat_account", new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create(ns).ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<IReadOnlyList<UnclaimedRepositorySummary>> ListAsync()
    {
        GetUnclaimedRepositories.Handler sut = new(new UnclaimedRepositories(
            _dbContext,
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance)));

        Result<IReadOnlyList<UnclaimedRepositorySummary>> result = await sut.HandleAsync(
            new GetUnclaimedRepositories.Query(),
            CancellationToken.None);

        return result.ValueOrThrow();
    }

    [Fact]
    public async Task ListsOnlyRepositoriesNoAccountCoversAndNotRemoved()
    {
        // Arrange
        await SeedGitHubAccountAsync("covered");
        MonitoredRepository orphan = await SeedRepositoryAsync("gitlab.example.com", "group/project");
        await SeedRepositoryAsync("github.com", "covered/repo");
        MonitoredRepository removed = await SeedRepositoryAsync("gitlab.example.com", "group/removed");
        removed.Remove(Now);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        // Act
        IReadOnlyList<UnclaimedRepositorySummary> unclaimed = await ListAsync();

        // Assert
        unclaimed.ShouldHaveSingleItem().Id.ShouldBe(orphan.Id.Value);
    }

    [Fact]
    public async Task ListsRepositoryOnCoveredHostOutsideEveryClaim()
    {
        // Arrange
        await SeedGitHubAccountAsync("covered");
        MonitoredRepository orphan = await SeedRepositoryAsync("github.com", "elsewhere/repo");

        // Act
        IReadOnlyList<UnclaimedRepositorySummary> unclaimed = await ListAsync();

        // Assert
        unclaimed.ShouldHaveSingleItem().Id.ShouldBe(orphan.Id.Value);
    }

    [Fact]
    public async Task ProjectsHostSlugAndNoCredentialViolationForTopLevelNamespace()
    {
        // Arrange
        await SeedRepositoryAsync("gitlab.example.com", "group/sub/project");

        // Act
        UnclaimedRepositorySummary summary = (await ListAsync()).ShouldHaveSingleItem();

        // Assert
        summary.Host.ShouldBe("gitlab.example.com");
        summary.Slug.ShouldBe("group/sub/project");
        summary.Violation.ShouldBe("no-credential:group");
        summary.ViolationDescription.ShouldBe(EligibilityViolationInfo.NoCredentialDescription("group"));
    }

    [Fact]
    public async Task OrdersByHostThenSlug()
    {
        // Arrange
        await SeedRepositoryAsync("gitlab.example.com", "beta/repo");
        await SeedRepositoryAsync("github.com", "zeta/repo");
        await SeedRepositoryAsync("gitlab.example.com", "alpha/repo");

        // Act
        IReadOnlyList<UnclaimedRepositorySummary> unclaimed = await ListAsync();

        // Assert
        unclaimed.Select(r => $"{r.Host}/{r.Slug}").ShouldBe(
        [
            "github.com/zeta/repo",
            "gitlab.example.com/alpha/repo",
            "gitlab.example.com/beta/repo",
        ]);
    }
}
