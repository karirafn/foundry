using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.WorkerRunQueriesTests;

public sealed class GetWorkerRunDetailAsync_DiffGate : IAsyncDisposable
{
    private static readonly CommitSha Head = CommitSha.From("3333333333333333333333333333333333333333");

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public GetWorkerRunDetailAsync_DiffGate()
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
    public async Task ABlockedRun_ShowsItsFindingsAndDiscardTimeButNoMatchedValue()
    {
        // Arrange
        PublishBlockedRun blocked = PublishingRunFixture.Publishing().BlockPublish(
            [new SecretFinding("src/appsettings.json", 14, Head, "aws-access-token")],
            PublishingRunFixture.ExitedAt);
        await SeedAsync(blocked);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(blocked.Id);

        // Assert
        detail.ShouldSatisfyAllConditions(
            () => detail.State.ShouldBe("publish_blocked"),
            () => detail.SecretFindings.ShouldBe([new WorkerRunSecretFinding("src/appsettings.json", 14, Head.Value, "aws-access-token")]),
            () => detail.DiscardsAt.ShouldBe(blocked.DiscardsAt),
            () => detail.PublishRetry.ShouldBeNull(),
            () => detail.Publish.ShouldBeNull(),
            () => typeof(WorkerRunSecretFinding).GetProperties().Select(property => property.Name)
                .ShouldBe(["Path", "Line", "CommitSha", "RuleId"], ignoreOrder: true));
    }

    [Fact]
    public async Task ACompletedRun_ShowsTheFilesTheDiffGateReverted()
    {
        // Arrange
        PublishingRun publishing = PublishingRunFixture.Publishing();
        publishing.RecordPublished(
            new PublishResult.Published(
                Head,
                2,
                ["feat: add demo"],
                [
                    new RevertedFile("assets/demo.mp4", RevertedFileReason.Oversized, 50_540_134),
                    new RevertedFile("tools/protoc.exe", RevertedFileReason.Binary, 3_250_585),
                ]),
            PublishingRunFixture.ExitedAt);
        CompletedRun completed = publishing.Complete(0, PublishingRunFixture.Branch, pullRequestUrl: null);
        await SeedAsync(completed);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(completed.Id);

        // Assert
        detail.ShouldSatisfyAllConditions(
            () => detail.RevertedFiles.ShouldBe(
            [
                new WorkerRunRevertedFile("assets/demo.mp4", "oversized", 50_540_134),
                new WorkerRunRevertedFile("tools/protoc.exe", "binary", 3_250_585),
            ]),
            () => detail.SecretFindings.ShouldBeEmpty());
    }

    [Fact]
    public async Task ARunWithNothingReverted_ShowsAnEmptyList()
    {
        // Arrange
        PublishingRun publishing = PublishingRunFixture.Publishing();
        await SeedAsync(publishing);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(publishing.Id);

        // Assert
        detail.RevertedFiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task ABlockedRun_ShowsWhenItWasBlockedAndHasNoConfirmation()
    {
        // Arrange
        DateTimeOffset blockedAt = PublishingRunFixture.ExitedAt.AddMinutes(1);
        PublishBlockedRun blocked = PublishingRunFixture.Publishing().BlockPublish(
            [new SecretFinding("src/appsettings.json", 14, Head, "aws-access-token")],
            blockedAt);
        await SeedAsync(blocked);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(blocked.Id);

        // Assert
        detail.ShouldSatisfyAllConditions(
            () => detail.PublishBlockedAt.ShouldBe(blockedAt),
            () => detail.FalsePositivesConfirmedAt.ShouldBeNull(),
            () => detail.ConfirmedFindings.ShouldBeEmpty());
    }

    [Fact]
    public async Task AConfirmedRun_ShowsTheConfirmedFindingsAndTimeButNoBlockTime()
    {
        // Arrange
        DateTimeOffset confirmedAt = PublishingRunFixture.ExitedAt.AddHours(1);
        PublishingRun confirmed = PublishingRunFixture.Publishing()
            .BlockPublish([new SecretFinding("src/store.ts", 3, Head, "generic-api-key")], PublishingRunFixture.ExitedAt)
            .ConfirmFalsePositives(confirmedAt);
        await SeedAsync(confirmed);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(confirmed.Id);

        // Assert
        detail.ShouldSatisfyAllConditions(
            () => detail.State.ShouldBe("publishing"),
            () => detail.PublishBlockedAt.ShouldBeNull(),
            () => detail.FalsePositivesConfirmedAt.ShouldBe(confirmedAt),
            () => detail.ConfirmedFindings.ShouldBe([new WorkerRunConfirmedFinding("src/store.ts", 3, "generic-api-key")]),
            () => typeof(WorkerRunConfirmedFinding).GetProperties().Select(property => property.Name)
                .ShouldBe(["Path", "Line", "RuleId"], ignoreOrder: true));
    }

    private async Task SeedAsync(WorkerRun run)
    {
        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<WorkerRunDetail> GetDetailAsync(WorkerRunId id)
    {
        WorkerRunQueries sut = new(_dbContext);
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(id.Value, TestContext.Current.CancellationToken);
        return result.ShouldBeOfType<Result<WorkerRunDetail>.Success>().Value;
    }
}
