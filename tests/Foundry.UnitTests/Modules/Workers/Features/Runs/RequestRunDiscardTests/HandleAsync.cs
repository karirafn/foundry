using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.RequestRunDiscardTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly FakeWorkerOrchestrator _orchestrator = new();

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

    [Fact]
    public async Task ABlockedRun_IsDiscardedAndItsHandOffVolumeDeleted()
    {
        // Arrange
        PublishBlockedRun blocked = PublishingRunFixture.Publishing().BlockPublish(
            [new SecretFinding("a.env", 1, CommitSha.From("3333333333333333333333333333333333333333"), "github-pat")],
            PublishingRunFixture.ExitedAt);
        await SeedAsync(blocked);

        // Act
        Result<RequestRunDiscard.Response> result = await DiscardAsync(blocked.Id);

        // Assert
        result.ShouldBeOfType<Result<RequestRunDiscard.Response>.Success>();
        (await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<FailedRun>().Reason.CategoryToken.ShouldBe(FailureCategory.RunDiscardedToken);
        _orchestrator.DeletedHandOffVolumes.ShouldBe([blocked.Id]);
    }

    [Fact]
    public async Task AnExhaustedPublishFailedRun_IsDiscarded()
    {
        // Arrange
        PublishFailedRun failed = PublishingRunFixture.Publishing()
            .FailPublish("refused", transient: false, PublishingRunFixture.ExitedAt);
        await SeedAsync(failed);

        // Act
        Result<RequestRunDiscard.Response> result = await DiscardAsync(failed.Id);

        // Assert
        result.ShouldBeOfType<Result<RequestRunDiscard.Response>.Success>();
        (await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken)).ShouldBeOfType<FailedRun>();
    }

    [Fact]
    public async Task APublishStillRetrying_IsNotDiscarded()
    {
        // Arrange
        PublishFailedRun retrying = PublishingRunFixture.Publishing()
            .FailPublish("timed out", transient: true, PublishingRunFixture.ExitedAt);
        await SeedAsync(retrying);

        // Act
        Result<RequestRunDiscard.Response> result = await DiscardAsync(retrying.Id);

        // Assert
        result.ShouldBeOfType<Result<RequestRunDiscard.Response>.Failure>()
            .Error.Code.ShouldBe("WorkerRun.NotHoldingUnpublishedWork");
        _orchestrator.DeletedHandOffVolumes.ShouldBeEmpty();
    }

    [Fact]
    public async Task APublishingRun_IsNotDiscarded()
    {
        // Arrange
        PublishingRun publishing = PublishingRunFixture.Publishing();
        await SeedAsync(publishing);

        // Act
        Result<RequestRunDiscard.Response> result = await DiscardAsync(publishing.Id);

        // Assert
        result.ShouldBeOfType<Result<RequestRunDiscard.Response>.Failure>()
            .Error.Code.ShouldBe("WorkerRun.NotHoldingUnpublishedWork");
    }

    [Fact]
    public async Task AnUnknownRun_IsNotFound()
    {
        // Act
        Result<RequestRunDiscard.Response> result = await DiscardAsync(WorkerRunId.New());

        // Assert
        result.ShouldBeOfType<Result<RequestRunDiscard.Response>.Failure>().Error.Code.ShouldBe("WorkerRun.NotFound");
    }

    private async Task SeedAsync(WorkerRun run)
    {
        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private Task<Result<RequestRunDiscard.Response>> DiscardAsync(WorkerRunId id)
    {
        RequestRunDiscard.Handler sut = new(
            _dbContext,
            _orchestrator,
            new DomainEventDispatcher(new ServiceCollection().BuildServiceProvider()));
        return sut.HandleAsync(new RequestRunDiscard.Command(id), TestContext.Current.CancellationToken);
    }
}
