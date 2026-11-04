using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Runs.RequestRunCancellationTests;

public sealed class RequestRunCancellation : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public RequestRunCancellation()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static ActiveRun NewActiveRun()
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        return starting.Activate(
            ContainerId.From("container-cancel-test"),
            BranchName.From("feat/42-cancel"),
            MonitoredRepositoryId.New());
    }

    private async Task<WorkerRun> SeedAsync(WorkerRun run)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Set<WorkerRun>().Add(run);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return run;
    }

    private async Task<WorkerRun> LoadAsync(WorkerRunId id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        WorkerRun? run = await dbContext.Set<WorkerRun>()
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, TestContext.Current.CancellationToken);
        return run.ShouldNotBeNull();
    }

    private Task<HttpResponseMessage> PostCancellationAsync(Guid runId)
        => _client.PostAsync(
            new Uri($"/api/workers/runs/{runId}/cancellation", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenRunIsActive_Returns202AndPersistsCancelRequest()
    {
        // Arrange
        WorkerRun run = await SeedAsync(NewActiveRun());

        // Act
        HttpResponseMessage response = await PostCancellationAsync(run.Id.Value);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        ActiveRun persisted = (await LoadAsync(run.Id)).ShouldBeOfType<ActiveRun>();
        persisted.CancelRequestedAt.ShouldNotBeNull();

        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("cancelRequestedAt").GetDateTimeOffset()
            .ShouldBe(persisted.CancelRequestedAt.Value);
    }

    [Fact]
    public async Task WhenCalledTwice_Returns202AndKeepsFirstTimestamp()
    {
        // Arrange
        WorkerRun run = await SeedAsync(NewActiveRun());
        (await PostCancellationAsync(run.Id.Value)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        DateTimeOffset? first = ((ActiveRun)await LoadAsync(run.Id)).CancelRequestedAt;

        // Act
        HttpResponseMessage response = await PostCancellationAsync(run.Id.Value);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        ((ActiveRun)await LoadAsync(run.Id)).CancelRequestedAt.ShouldBe(first);
    }

    [Fact]
    public async Task WhenRunDoesNotExist_Returns404()
    {
        // Act
        HttpResponseMessage response = await PostCancellationAsync(Guid.NewGuid());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Type.ShouldEndWith("WorkerRun.NotFound");
    }

    [Fact]
    public async Task WhenRunIsStarting_Returns409()
    {
        // Arrange
        WorkerRun run = await SeedAsync(StartingRun.Begin(IssueId.New(), WorkerRunId.New()));

        // Act
        HttpResponseMessage response = await PostCancellationAsync(run.Id.Value);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Type.ShouldEndWith("WorkerRun.NotCancellable");
    }

    [Fact]
    public async Task WhenRunIsCompleted_Returns409()
    {
        // Arrange
        ActiveRun active = NewActiveRun();
        WorkerRun run = await SeedAsync(active.Complete(0, active.BranchName, PullRequestUrl.From("https://github.com/o/r/pull/1")));

        // Act
        HttpResponseMessage response = await PostCancellationAsync(run.Id.Value);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task WhenRunIsFailed_Returns409()
    {
        // Arrange
        ActiveRun active = NewActiveRun();
        WorkerRun run = await SeedAsync(active.Fail(new FailureReason.TimedOut(), branchNameOrNull: null));

        // Act
        HttpResponseMessage response = await PostCancellationAsync(run.Id.Value);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task WhenCancelRequested_RunDetailExposesCancelRequestedAt()
    {
        // Arrange
        WorkerRun run = await SeedAsync(NewActiveRun());
        await PostCancellationAsync(run.Id.Value);

        // Act
        WorkerRunDetail? detail = await _client.GetFromJsonAsync<WorkerRunDetail>(
            new Uri($"/api/workers/runs/{run.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        detail.ShouldNotBeNull().CancelRequestedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenCompletedRunCarriesCancelRequest_RunDetailExposesIt()
    {
        // Arrange — a run that resolved (PR existed) after a cancel was requested
        ActiveRun active = NewActiveRun();
        DateTimeOffset requestedAt = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
        active.RequestCancel(requestedAt);
        WorkerRun run = await SeedAsync(active.Complete(0, active.BranchName, PullRequestUrl.From("https://github.com/o/r/pull/1")));

        // Act
        WorkerRunDetail? detail = await _client.GetFromJsonAsync<WorkerRunDetail>(
            new Uri($"/api/workers/runs/{run.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        detail.ShouldNotBeNull().ShouldSatisfyAllConditions(
            d => d.State.ShouldBe("completed"),
            d => d.CancelRequestedAt.ShouldBe(requestedAt));
    }
}
