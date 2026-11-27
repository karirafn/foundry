using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.WorkerReactions;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.WorkerReactions.WorkerRunAccessRefusedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const string Version = "github-current";
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly SaveCounter _saves = new();
    private readonly FoundryDbContext _dbContext;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_saves)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private WorkerRunAccessRefusedHandler BuildSut() =>
        new(_dbContext, NullLogger<WorkerRunAccessRefusedHandler>.Instance);

    private async Task<MonitoredRepository> SeedRepoAsync(WriteProbeVerdict verdict, RepositoryEligibility eligibility)
    {
        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create("alice/repo").ValueOrThrow(), "github.com", pollInterval: null).ValueOrThrow();
        repo.SetWriteProbeVerdict(verdict);
        repo.SetEligibility(eligibility);
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
        _saves.Count = 0;
        return repo;
    }

    private async Task<MonitoredRepository> ReloadAsync(MonitoredRepositoryId id)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<MonitoredRepository>()
            .IgnoreQueryFilters()
            .SingleAsync(r => r.Id == id, CancellationToken.None);
    }

    private static WorkerRunAccessRefused EventFor(MonitoredRepositoryId id) =>
        new(WorkerRunId.New(), id.Value);

    [Fact]
    public async Task WhenVerdictIsGranted_StoresUnknownAccessRefusedAndUnreachableInOneSave()
    {
        // Arrange
        MonitoredRepository seeded = await SeedRepoAsync(
            new WriteProbeVerdict.Granted { PermissionListVersion = Version },
            new RepositoryEligibility.Eligible());

        // Act
        await BuildSut().HandleAsync(EventFor(seeded.Id), CancellationToken.None);

        // Assert
        MonitoredRepository stored = await ReloadAsync(seeded.Id);
        stored.ShouldSatisfyAllConditions(
            () => stored.WriteProbeVerdict.ShouldBe(
                new WriteProbeVerdict.Unknown(LastAttemptedAt: null, Reason: UnknownReason.AccessRefused)
                {
                    PermissionListVersion = Version,
                }),
            () => stored.Eligibility.ShouldBe(new RepositoryEligibility.Unreachable(UnreachableReason.AccessRefused)),
            () => _saves.Count.ShouldBe(1));
    }

    [Fact]
    public async Task WhenVerdictIsUnknownRateLimited_ChangesNothingAndDoesNotSave()
    {
        // Arrange
        WriteProbeVerdict verdict = new WriteProbeVerdict.Unknown(Now.AddMinutes(-5), UnknownReason.RateLimited)
        {
            PermissionListVersion = Version,
        };
        MonitoredRepository seeded = await SeedRepoAsync(
            verdict, new RepositoryEligibility.Unreachable(UnreachableReason.RateLimited));

        // Act
        await BuildSut().HandleAsync(EventFor(seeded.Id), CancellationToken.None);

        // Assert
        MonitoredRepository stored = await ReloadAsync(seeded.Id);
        stored.ShouldSatisfyAllConditions(
            () => stored.WriteProbeVerdict.ShouldBe(verdict),
            () => stored.Eligibility.ShouldBe(new RepositoryEligibility.Unreachable(UnreachableReason.RateLimited)),
            () => _saves.Count.ShouldBe(0));
    }

    [Fact]
    public async Task WhenVerdictIsDenied_ChangesNothingAndDoesNotSave()
    {
        // Arrange
        WriteProbeVerdict verdict = new WriteProbeVerdict.Denied(Now.AddMinutes(-5)) { PermissionListVersion = Version };
        MonitoredRepository seeded = await SeedRepoAsync(verdict, new RepositoryEligibility.Ineligible([new EligibilityViolation("cannot-push:alice/repo")]));

        // Act
        await BuildSut().HandleAsync(EventFor(seeded.Id), CancellationToken.None);

        // Assert
        MonitoredRepository stored = await ReloadAsync(seeded.Id);
        stored.ShouldSatisfyAllConditions(
            () => stored.WriteProbeVerdict.ShouldBe(verdict),
            () => stored.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>(),
            () => _saves.Count.ShouldBe(0));
    }

    [Fact]
    public async Task WhenTwoEventsArriveForOneRepository_OnlyTheFirstSaves()
    {
        // Arrange
        MonitoredRepository seeded = await SeedRepoAsync(
            new WriteProbeVerdict.Granted { PermissionListVersion = Version },
            new RepositoryEligibility.Eligible());
        WorkerRunAccessRefusedHandler sut = BuildSut();
        await sut.HandleAsync(EventFor(seeded.Id), CancellationToken.None);
        _dbContext.ChangeTracker.Clear();

        // Act
        await sut.HandleAsync(EventFor(seeded.Id), CancellationToken.None);

        // Assert
        _saves.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenRepositoryIsUnknown_ChangesNothingAndDoesNotThrow()
    {
        // Act
        await BuildSut().HandleAsync(new WorkerRunAccessRefused(WorkerRunId.New(), Guid.NewGuid()), CancellationToken.None);

        // Assert
        _saves.Count.ShouldBe(0);
    }

    [Fact]
    public async Task WhenRepositoryIsRemoved_ChangesNothingAndDoesNotThrow()
    {
        // Arrange
        MonitoredRepository seeded = await SeedRepoAsync(
            new WriteProbeVerdict.Granted { PermissionListVersion = Version },
            new RepositoryEligibility.Eligible());
        MonitoredRepository tracked = await _dbContext.Set<MonitoredRepository>().SingleAsync(CancellationToken.None);
        tracked.Remove(Now);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
        _saves.Count = 0;

        // Act
        await BuildSut().HandleAsync(EventFor(seeded.Id), CancellationToken.None);

        // Assert
        MonitoredRepository stored = await ReloadAsync(seeded.Id);
        stored.ShouldSatisfyAllConditions(
            () => stored.WriteProbeVerdict.ShouldBeOfType<WriteProbeVerdict.Granted>(),
            () => _saves.Count.ShouldBe(0));
    }

    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
