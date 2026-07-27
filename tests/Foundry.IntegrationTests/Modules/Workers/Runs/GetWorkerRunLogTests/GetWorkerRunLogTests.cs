using System.Net;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Runs.GetWorkerRunLogTests;

public sealed class WhenRunLogRequested : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRunLogRequested()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<FailedRun> SeedFailedRunAsync(string? containerOutput = null)
    {
        // No POST endpoint exists for worker runs — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-log-test"),
            BranchName.From("feat/99-log-test"),
            MonitoredRepositoryId.New());

        FailedRun failed = active.Fail(
            new FailureReason.NonZeroExit(1),
            branchNameOrNull: null,
            containerOutput: containerOutput);

        dbContext.Set<WorkerRun>().Add(failed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return failed;
    }

    private async Task<ActiveRun> SeedActiveRunAsync()
    {
        // No POST endpoint exists for worker runs — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-active-log-test"),
            BranchName.From("feat/100-active"),
            MonitoredRepositoryId.New());

        dbContext.Set<WorkerRun>().Add(active);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return active;
    }

    [Fact]
    public async Task WhenRunDoesNotExist_Returns404()
    {
        // Arrange
        Guid unknownId = Guid.NewGuid();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{unknownId}/log", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenFailedRunHasStoredLog_Returns200WithLogContent()
    {
        // Arrange
        FailedRun run = await SeedFailedRunAsync(
            containerOutput: "2024-01-01T00:00:00Z line one\n2024-01-01T00:00:01Z line two\n");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}/log", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.ShouldContain("line one");
        content.ShouldContain("line two");
    }

    [Fact]
    public async Task WhenFailedRunHasNoStoredLog_Returns204()
    {
        // Arrange
        FailedRun run = await SeedFailedRunAsync(containerOutput: null);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}/log", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task WhenRunIsActive_Returns204()
    {
        // Arrange
        ActiveRun run = await SeedActiveRunAsync();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}/log", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
