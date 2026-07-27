using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Endpoints.GetWorkerRunDetailTests;

public sealed class WhenRunExists : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRunExists()
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
            ContainerId.From("container-detail-test"),
            BranchName.From("feat/42-test"),
            MonitoredRepositoryId.New());

        FailedRun failed = active.Fail(
            new FailureReason.NonZeroExit(1),
            branchNameOrNull: null,
            containerOutput: containerOutput);

        dbContext.Set<WorkerRun>().Add(failed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return failed;
    }

    [Fact]
    public async Task WhenFailedRunExists_Returns200WithDetail()
    {
        // Arrange
        FailedRun run = await SeedFailedRunAsync();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WorkerRunDetail? detail = await response.Content.ReadFromJsonAsync<WorkerRunDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull();
        detail.ShouldSatisfyAllConditions(
            () => detail.WorkerRunId.ShouldBe(run.Id.Value),
            () => detail.State.ShouldBe("failed"),
            () => detail.FailureCategory.ShouldBe("non_zero_exit"),
            () => detail.HasStoredLog.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenFailedRunHasContainerOutput_HasStoredLogIsTrue()
    {
        // Arrange
        FailedRun run = await SeedFailedRunAsync(containerOutput: "some log output");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WorkerRunDetail? detail = await response.Content.ReadFromJsonAsync<WorkerRunDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull();
        detail.HasStoredLog.ShouldBeTrue();
    }
}
