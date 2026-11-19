using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Runs.FindLiveRunRepositoryTests;

public sealed class FindLiveRunRepository : IAsyncDisposable
{
    private static readonly RunBase Base = new(CommitSha.From("1111111111111111111111111111111111111111"), RunBranchHead: null);

    private readonly FoundryWebAppFactory _factory = new();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenTheRunIsStarting_ReturnsTheRepositoryItWasPreparedFrom()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordPreparedBase(Base, repositoryId);
        await SaveAsync(starting);

        // Act
        Guid? found = await FindAsync(starting.SourceToken!.Value);

        // Assert
        found.ShouldBe(repositoryId.Value);
    }

    [Fact]
    public async Task WhenTheRunIsActive_ReturnsItsRepository()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordPreparedBase(Base, repositoryId);
        await SaveAsync(starting.Activate(ContainerId.From("container-1"), BranchName.From("feat/1-x"), repositoryId));

        // Act
        Guid? found = await FindAsync(starting.SourceToken!.Value);

        // Assert
        found.ShouldBe(repositoryId.Value);
    }

    [Fact]
    public async Task WhenTheRunIsPublishing_ReturnsNothing()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordPreparedBase(Base, repositoryId);
        PublishingRun publishing = starting
            .Activate(ContainerId.From("container-1"), BranchName.From("feat/1-x"), repositoryId)
            .BeginPublishing(
                new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow),
                containerOutput: "log tail",
                RunVerdict.Done(),
                new RunPullRequestText("Title", new RunPullRequestDescription(null, ["Body"], ["dotnet test: passed."], [], null)));
        await SaveAsync(publishing);

        // Act
        Guid? found = await FindAsync(starting.SourceToken!.Value);

        // Assert
        found.ShouldBeNull();
    }

    [Fact]
    public async Task WhenNoRunHasTheToken_ReturnsNothing()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordPreparedBase(Base, MonitoredRepositoryId.New());
        await SaveAsync(starting);

        // Act
        Guid? found = await FindAsync("not-the-token");

        // Assert
        found.ShouldBeNull();
    }

    private async Task SaveAsync(WorkerRun run)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Set<WorkerRun>().Add(run);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid?> FindAsync(string token)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IRunSourceTokenQueries queries = scope.ServiceProvider.GetRequiredService<IRunSourceTokenQueries>();
        return await queries.FindLiveRunRepositoryAsync(RunSourceTokenHash.Of(token), TestContext.Current.CancellationToken);
    }
}
