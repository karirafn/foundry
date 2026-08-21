using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.TransientRetry;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.TransientRetry;

// TransientRetryService has no HTTP endpoint — it is a background periodic service.
// These tests resolve it as a plain singleton and call TickForTest against the real wired SQLite context.
public sealed class WhenTransientFailedIssueIsElapsed : IAsyncDisposable
{
    // SeedFailedAt is fixed in the past so the coarse SQL prefilter (FailedAt <= now - 1min) always passes.
    private static readonly DateTimeOffset SeedFailedAt =
        new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // NowOverride is 5 minutes after SeedFailedAt — clearly past the 1-minute initial backoff.
    private static readonly DateTimeOffset NowOverride = SeedFailedAt.AddMinutes(5);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public WhenTransientFailedIssueIsElapsed()
    {
        // Override: register TransientRetryService as a plain singleton (not IHostedService)
        // with a fixed clock. The factory already removes all IHostedService registrations.
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.AddSingleton(sp => new TransientRetryService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<TransientRetryService>.Instance,
                nowOverride: NowOverride));
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<FailedIssue> SeedTransientFailedIssueAsync(
        DateTimeOffset failedAt,
        int issueNumber = 1)
    {
        // Use DbContext directly — no POST endpoint exists to seed issues directly to FailedIssue state.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Transient test issue",
            body: "Test body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: failedAt.AddHours(-2));
        FreshQueuedIssue queued = FreshQueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        FailedIssue failed = inProgress.MarkFailed(
            Guid.NewGuid(),
            "Transient Anthropic API fault",
            failedAt,
            "transient_api_error");

        dbContext.Set<Issue>().Add(failed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return failed;
    }

    private async Task SeedTransientFailedRunsAsync(IssueId issueId, int count)
    {
        // Seed FailedRun rows with TransientApiError reason so CountConsecutiveTransientRunsAsync
        // returns the desired count. We go through Starting → Failed transitions to create real runs.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        for (int i = 0; i < count; i++)
        {
            StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
            FailedRun failedRun = starting.Fail(new FailureReason.TransientApiError());
            dbContext.Set<WorkerRun>().Add(failedRun);
        }

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenElapsedAndUnderCap_TransitionsToQueued()
    {
        // Arrange — no prior transient worker runs (attempt=0 < MaxTransientRetries=2).
        // NowOverride is 5 minutes after SeedFailedAt — clearly past the 1-minute backoff.
        FailedIssue failed = await SeedTransientFailedIssueAsync(SeedFailedAt);

        // Act
        TransientRetryService sut = _factory.Services.GetRequiredService<TransientRetryService>();
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == failed.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenTwoPriorTransientRuns_StaysFailedAndAutoRetrySkips()
    {
        // Arrange — 2 prior transient worker runs (attempt=2 >= MaxTransientRetries=2 → skip).
        FailedIssue failed = await SeedTransientFailedIssueAsync(SeedFailedAt, issueNumber: 2);
        await SeedTransientFailedRunsAsync(failed.Id, count: 2);

        // Act
        TransientRetryService sut = _factory.Services.GetRequiredService<TransientRetryService>();
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — stays failed (auto-retry exhausted)
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == failed.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<FailedIssue>();
    }

    [Fact]
    public async Task WhenTwoPriorTransientRuns_ManualRetryStillTransitions()
    {
        // Arrange — same exhausted issue; manual POST /issues/{id}/retry must still work (AC 5).
        FailedIssue failed = await SeedTransientFailedIssueAsync(SeedFailedAt, issueNumber: 3);
        await SeedTransientFailedRunsAsync(failed.Id, count: 2);

        // Act — manual retry via HTTP endpoint
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/issues/{failed.Id.Value}/retry", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert — manual retry transitions it to queued regardless of exhausted auto-retry
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IssueDetail? detail = await response.Content.ReadFromJsonAsync<IssueDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull();
        detail.State.ShouldBe("queued");
    }
}
