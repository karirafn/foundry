using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.DeleteRepositoryHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const string AccountToken = "github_pat_account";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

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

    private MonitoredRepository CreateRepo(string owner, int position)
    {
        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create($"{owner}/repo").ValueOrThrow(),
            "github.com",
            null,
            position).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        _dbContext.SaveChanges();
        return repo;
    }

    private async Task SeedAccountAsync(string ns, string? token = AccountToken)
    {
        GitHubCredential credential = GitHubCredential.Create(
            AccountName.Create(ns).ValueOrThrow(),
            token is null ? null : new IssuedToken(token, new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create(ns).ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private DeleteRepository.Handler CreateHandler(RecordingBranchRulesProviderFactory? factory = null) =>
        new(
            _dbContext,
            new RepositoryWebhookDeleter(
                new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance),
                factory ?? new RecordingBranchRulesProviderFactory(),
                NullLogger<RepositoryWebhookDeleter>.Instance));

    private async Task<bool> ExistsAsync(MonitoredRepository repository)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<MonitoredRepository>()
            .AnyAsync(r => r.Id == repository.Id, CancellationToken.None);
    }

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
            new DeleteRepository.Command(Guid.NewGuid(), repoB.Id.Value),
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

    [Fact]
    public async Task DeletesTheRepositorysHooksWithTheAccountToken()
    {
        // Arrange
        await SeedAccountAsync("alice");
        MonitoredRepository repository = CreateRepo("alice", 0);
        RecordingBranchRulesProviderFactory factory = new();
        DeleteRepository.Handler sut = CreateHandler(factory);

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        factory.TokensUsed.ShouldBe([AccountToken]);
        factory.WebhookDeletes.ShouldBe([("alice/repo", repository.WebhookHookId)]);
        (await ExistsAsync(repository)).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenNoAccountCoversTheRepository_RemovesItWithoutAProviderCall()
    {
        // Arrange
        MonitoredRepository repository = CreateRepo("alice", 0);
        RecordingBranchRulesProviderFactory factory = new();
        DeleteRepository.Handler sut = CreateHandler(factory);

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        factory.WebhookDeletes.ShouldBeEmpty();
        (await ExistsAsync(repository)).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheAccountTokenIsAbsent_RemovesItWithoutAProviderCall()
    {
        // Arrange
        await SeedAccountAsync("alice", token: null);
        MonitoredRepository repository = CreateRepo("alice", 0);
        RecordingBranchRulesProviderFactory factory = new();
        DeleteRepository.Handler sut = CreateHandler(factory);

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        factory.WebhookDeletes.ShouldBeEmpty();
        (await ExistsAsync(repository)).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheProviderRefusesTheDeletion_StillRemovesTheRepository()
    {
        // Arrange
        await SeedAccountAsync("alice");
        MonitoredRepository repository = CreateRepo("alice", 0);
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithWebhookDeleteResult("alice/repo", Result<int>.Fail(WebhookWriteErrors.InsufficientPermission("403")));
        DeleteRepository.Handler sut = CreateHandler(factory);

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        factory.WebhookDeletes.ShouldHaveSingleItem();
        (await ExistsAsync(repository)).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheProviderThrows_StillRemovesTheRepository()
    {
        // Arrange
        await SeedAccountAsync("alice");
        MonitoredRepository repository = CreateRepo("alice", 0);
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithException("alice/repo", new HttpRequestException("unreachable"));
        DeleteRepository.Handler sut = CreateHandler(factory);

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await ExistsAsync(repository)).ShouldBeFalse();
    }
}
