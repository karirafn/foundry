using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.DeleteRepositoryHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CredentialId _accountId;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();

        GitHubCredential account = GitHubCredential.Create("org", "TOKEN", BaseUrl.Create("https://github.com").ValueOrThrow());
        _dbContext.Set<Credential>().Add(account);
        _dbContext.SaveChanges();
        _accountId = account.Id;
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private MonitoredRepository CreateRepo(string owner, int position)
    {
        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create($"{owner}/repo").ValueOrThrow(),
            _accountId,
            "github.com",
            null,
            position);
        _dbContext.Set<MonitoredRepository>().Add(repo);
        _dbContext.SaveChanges();
        return repo;
    }

    private DeleteRepository.Handler CreateHandler() => new(_dbContext);

    [Fact]
    public async Task WhenMiddleRepositoryDeleted_SurvivorPositionsAreContiguous()
    {
        // Arrange — three repos at 0, 1, 2; delete the middle one
        MonitoredRepository repoA = CreateRepo("owner-a", 0);
        MonitoredRepository repoB = CreateRepo("owner-b", 1);
        MonitoredRepository repoC = CreateRepo("owner-c", 2);

        DeleteRepository.Handler sut = CreateHandler();

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(_accountId.Value, repoB.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        _dbContext.ChangeTracker.Clear();
        List<MonitoredRepository> reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        reloaded.Count.ShouldBe(2);
        reloaded.Select(r => r.Position).ShouldBe([0, 1]);
        reloaded[0].Id.ShouldBe(repoA.Id);
        reloaded[1].Id.ShouldBe(repoC.Id);
    }
}
