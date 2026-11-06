using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Persistence.WorkerRunRoleAndImageTests;

public sealed class WhenRunTransitions : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory = new();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task TheRoleAndImageRecordedAtStartArePersistedOnTheTerminalRun()
    {
        // Arrange
        WorkerRunId runId = WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(IssueId.New(), runId, WorkerRole.Implement, "foundry-worker:local@sha256:abc");
        ActiveRun active = starting.Activate(
            ContainerId.From("container-role-image"),
            BranchName.From("feat/42-test"),
            MonitoredRepositoryId.New());
        CompletedRun completed = active.Complete(0, BranchName.From("feat/42-test"), pullRequestUrl: null);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            dbContext.Set<WorkerRun>().Add(completed);
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
        persisted.ShouldSatisfyAllConditions(
            () => persisted.ShouldBeOfType<CompletedRun>(),
            () => persisted.Role.ShouldBe(WorkerRole.Implement),
            () => persisted.Image.ShouldBe("foundry-worker:local@sha256:abc"));
    }
}
