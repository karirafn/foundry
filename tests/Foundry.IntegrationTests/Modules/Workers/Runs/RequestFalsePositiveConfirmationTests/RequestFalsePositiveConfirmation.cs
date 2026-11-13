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

namespace Foundry.IntegrationTests.Modules.Workers.Runs.RequestFalsePositiveConfirmationTests;

public sealed class RequestFalsePositiveConfirmation : IAsyncDisposable
{
    // Sub-millisecond precision, so the test proves the client's rendered value survives the JSON and SQLite round trip.
    private static readonly DateTimeOffset BlockedAt = new DateTimeOffset(2026, 11, 13, 14, 56, 40, TimeSpan.Zero).AddTicks(1234567);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public RequestFalsePositiveConfirmation()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static PublishBlockedRun NewBlockedRun()
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-confirm-test"),
            BranchName.From("feat/42-confirm"),
            MonitoredRepositoryId.New());
        return active
            .BeginPublishing(
                new RunExit(0, OomKilled: false, RunStopReason.Exited, BlockedAt.AddMinutes(-1)),
                containerOutput: null,
                RunVerdict.Done(),
                pullRequestText: null)
            .BlockPublish(
                [new SecretFinding("src/store.ts", 3, CommitSha.From("3333333333333333333333333333333333333333"), "generic-api-key")],
                BlockedAt);
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
        return await dbContext.Set<WorkerRun>()
            .AsNoTracking()
            .SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
    }

    private async Task<string> RenderedPublishBlockedAtAsync(WorkerRunId id)
    {
        string json = await _client.GetStringAsync(
            new Uri($"/api/workers/runs/{id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);
        using JsonDocument detail = JsonDocument.Parse(json);
        return detail.RootElement.GetProperty("publishBlockedAt").GetString().ShouldNotBeNull();
    }

    private Task<HttpResponseMessage> PostConfirmationAsync(Guid runId, object body)
        => _client.PostAsJsonAsync(
            new Uri($"/api/workers/runs/{runId}/confirm-false-positives", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenTheBlockTimeTheClientRenderedIsEchoed_Returns202AndPublishesTheRun()
    {
        // Arrange
        WorkerRun run = await SeedAsync(NewBlockedRun());
        string rendered = await RenderedPublishBlockedAtAsync(run.Id);

        // Act
        HttpResponseMessage response = await PostConfirmationAsync(run.Id.Value, new { publishBlockedAt = rendered });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        PublishingRun publishing = (await LoadAsync(run.Id)).ShouldBeOfType<PublishingRun>();
        publishing.ConfirmedFindings.ShouldHaveSingleItem().Path.ShouldBe("src/store.ts");
        publishing.FalsePositivesConfirmedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenAConfirmedRunIsRead_TheDetailShowsTheConfirmation()
    {
        // Arrange
        WorkerRun run = await SeedAsync(NewBlockedRun());
        await PostConfirmationAsync(run.Id.Value, new { publishBlockedAt = await RenderedPublishBlockedAtAsync(run.Id) });

        // Act
        string json = await _client.GetStringAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        using JsonDocument detail = JsonDocument.Parse(json);
        JsonElement root = detail.RootElement;
        root.ShouldSatisfyAllConditions(
            () => root.GetProperty("publishBlockedAt").ValueKind.ShouldBe(JsonValueKind.Null),
            () => root.GetProperty("falsePositivesConfirmedAt").ValueKind.ShouldBe(JsonValueKind.String),
            () => root.GetProperty("confirmedFindings").EnumerateArray().ShouldHaveSingleItem()
                .GetProperty("ruleId").GetString().ShouldBe("generic-api-key"));
    }

    [Fact]
    public async Task WhenTheRunWasBlockedAgainSinceTheClientRenderedIt_Returns409()
    {
        // Arrange
        WorkerRun run = await SeedAsync(NewBlockedRun());

        // Act
        HttpResponseMessage response = await PostConfirmationAsync(
            run.Id.Value,
            new { publishBlockedAt = BlockedAt.AddMinutes(-5) });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Type.ShouldEndWith("WorkerRun.FindingsChanged");
        (await LoadAsync(run.Id)).ShouldBeOfType<PublishBlockedRun>();
    }

    [Fact]
    public async Task WhenTheRunIsAlreadyConfirmed_Returns409()
    {
        // Arrange
        WorkerRun run = await SeedAsync(NewBlockedRun());
        string rendered = await RenderedPublishBlockedAtAsync(run.Id);
        (await PostConfirmationAsync(run.Id.Value, new { publishBlockedAt = rendered })).StatusCode
            .ShouldBe(HttpStatusCode.Accepted);

        // Act
        HttpResponseMessage response = await PostConfirmationAsync(run.Id.Value, new { publishBlockedAt = rendered });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Type.ShouldEndWith("WorkerRun.NotPublishBlocked");
    }

    [Fact]
    public async Task WhenTheRunDoesNotExist_Returns404()
    {
        // Act
        HttpResponseMessage response = await PostConfirmationAsync(Guid.NewGuid(), new { publishBlockedAt = BlockedAt });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenTheBodyOmitsPublishBlockedAt_Returns400AndLeavesTheRunBlocked()
    {
        // Arrange
        WorkerRun run = await SeedAsync(NewBlockedRun());

        // Act
        HttpResponseMessage response = await PostConfirmationAsync(run.Id.Value, new { });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Type.ShouldEndWith("WorkerRun.PublishBlockedAtRequired");
        (await LoadAsync(run.Id)).ShouldBeOfType<PublishBlockedRun>();
    }
}
