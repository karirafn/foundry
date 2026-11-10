using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.RunTranscriptRetentionServiceTests;

public sealed class TickAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly SteppingTimeProvider _time = new(Now);

    public TickAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private RunTranscriptRetentionService BuildService()
    {
        ServiceCollection services = new();
        services.AddScoped<DbContext>(_ => CreateDbContext());
        ServiceProvider provider = services.BuildServiceProvider();

        return new RunTranscriptRetentionService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _time,
            NullLogger<RunTranscriptRetentionService>.Instance);
    }

    private async Task<WorkerRunId> SeedRunWithTranscriptAsync(DateTimeOffset capturedAt)
    {
        await using FoundryDbContext db = CreateDbContext();
        CompletedRun run = StartingRun.Begin(IssueId.New(), WorkerRunId.New())
            .Activate(ContainerId.From("container-retention"), BranchName.From("feat/4-retention"), MonitoredRepositoryId.New())
            .Complete(exitCode: 0, branchName: BranchName.From("feat/4-retention"), pullRequestUrl: null, containerOutput: "raw tail");
        db.Set<WorkerRun>().Add(run);
        db.Set<WorkerRunTranscript>().Add(WorkerRunTranscript.Capture(run.Id, "transcript", capturedAt));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return run.Id;
    }

    private async Task SeedExpiredTranscriptsAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await SeedRunWithTranscriptAsync(Now - RunTranscriptRetentionService.RetentionPeriod - TimeSpan.FromDays(1));
        }
    }

    private async Task<int> CountTranscriptsAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        return await db.Set<WorkerRunTranscript>().CountAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenTranscriptIsPastRetention_DeletesTheTranscriptAndKeepsTheRun()
    {
        // Arrange
        WorkerRunId runId = await SeedRunWithTranscriptAsync(Now - RunTranscriptRetentionService.RetentionPeriod - TimeSpan.FromMinutes(1));
        RunTranscriptRetentionService sut = BuildService();

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        CompletedRun run = (await db.Set<CompletedRun>().SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken));
        int transcripts = await CountTranscriptsAsync();
        run.ShouldSatisfyAllConditions(
            () => transcripts.ShouldBe(0),
            () => run.ContainerOutput.ShouldBe("raw tail"));
    }

    [Fact]
    public async Task WhenTranscriptIsWithinRetention_KeepsTheTranscript()
    {
        // Arrange
        await SeedRunWithTranscriptAsync(Now - RunTranscriptRetentionService.RetentionPeriod + TimeSpan.FromMinutes(1));
        RunTranscriptRetentionService sut = BuildService();

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        (await CountTranscriptsAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task WhenBatchIsFull_NextTickSweepsAgainImmediately()
    {
        // Arrange
        await SeedExpiredTranscriptsAsync(RunTranscriptRetentionService.DeleteBatchSize + 1);
        RunTranscriptRetentionService sut = BuildService();
        await sut.TickForTest(TestContext.Current.CancellationToken);
        int afterFirstTick = await CountTranscriptsAsync();

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        int afterSecondTick = await CountTranscriptsAsync();
        afterFirstTick.ShouldSatisfyAllConditions(
            () => afterFirstTick.ShouldBe(1),
            () => afterSecondTick.ShouldBe(0));
    }

    [Fact]
    public async Task WhenBatchIsUnderfull_NextTickWithinTheSweepIntervalSkips()
    {
        // Arrange
        RunTranscriptRetentionService sut = BuildService();
        await sut.TickForTest(TestContext.Current.CancellationToken);
        await SeedExpiredTranscriptsAsync(1);
        _time.Advance(RunTranscriptRetentionService.SweepInterval - TimeSpan.FromMinutes(1));

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        (await CountTranscriptsAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task WhenSweepIntervalHasElapsed_SweepsAgain()
    {
        // Arrange
        RunTranscriptRetentionService sut = BuildService();
        await sut.TickForTest(TestContext.Current.CancellationToken);
        await SeedExpiredTranscriptsAsync(1);
        _time.Advance(RunTranscriptRetentionService.SweepInterval);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        (await CountTranscriptsAsync()).ShouldBe(0);
    }

    private sealed class SteppingTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
