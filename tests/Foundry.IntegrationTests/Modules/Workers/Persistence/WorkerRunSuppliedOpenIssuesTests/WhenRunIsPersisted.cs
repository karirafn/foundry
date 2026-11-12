using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Persistence.WorkerRunSuppliedOpenIssuesTests;

public sealed class WhenRunIsPersisted : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory = new();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task TheSuppliedOpenIssuesSurviveTheTransitionToAnActiveRun()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(IssueId.New(), runId);
        TrackerContext trackerContext = new TrackerContextBuilder()
            .WithOpenIssueNumbers(7, 12, 31)
            .Build();
        starting.RecordSuppliedOpenIssues(trackerContext);
        ActiveRun active = starting.Activate(
            ContainerId.From("container-supplied-open-issues"),
            BranchName.From("feat/44-test"),
            MonitoredRepositoryId.New());

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            dbContext.Set<WorkerRun>().Add(active);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        WorkerRun persisted;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            persisted = await dbContext.Set<WorkerRun>()
                .SingleAsync(run => run.Id == runId, TestContext.Current.CancellationToken);
        }

        // Assert
        persisted.SuppliedOpenIssueNumbers.ShouldBe(active.SuppliedOpenIssueNumbers.ShouldNotBeNull());
    }
}
