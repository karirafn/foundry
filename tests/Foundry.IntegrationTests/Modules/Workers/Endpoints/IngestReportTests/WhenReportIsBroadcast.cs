using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Endpoints.IngestReportTests;

public sealed class WhenReportIsBroadcast : IAsyncDisposable
{
    private readonly FakeWorkerLogBroadcaster _broadcaster;
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenReportIsBroadcast()
    {
        _broadcaster = new FakeWorkerLogBroadcaster();
        _factory = new FoundryWebAppFactory(services =>
        {
            services.RemoveAll<IWorkerLogBroadcaster>();
            services.AddSingleton<IWorkerLogBroadcaster>(_broadcaster);
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<(WorkerRunId RunId, IssueId IssueId)> SeedActiveRunAsync()
    {
        // No HTTP endpoint to create active runs — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        WorkerRunId runId = WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(issueId, runId);
        ActiveRun active = starting.Activate(ContainerId.From("container-broadcast"));

        dbContext.Set<WorkerRun>().Add(active);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (runId, issueId);
    }

    [Fact]
    public async Task PushesReportToIssueGroup()
    {
        // Arrange
        (WorkerRunId runId, IssueId issueId) = await SeedActiveRunAsync();
        object request = new { Type = "progress", Content = "Broadcasting..." };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/workers/{runId.Value}/reports", UriKind.Relative),
            request,
            TestContext.Current.CancellationToken);

        // Assert
        response.IsSuccessStatusCode.ShouldBeTrue();

        _broadcaster.PushedReports.Count.ShouldBe(1);

        (Guid pushedIssueId, WorkerReportSummary pushedReport) = _broadcaster.PushedReports[0];
        pushedIssueId.ShouldBe(issueId.Value);
        pushedReport.ReportType.ShouldBe("progress");
        pushedReport.Content.ShouldBe("Broadcasting...");
    }

    private sealed class FakeWorkerLogBroadcaster : IWorkerLogBroadcaster
    {
        public List<(Guid IssueId, WorkerReportSummary Report)> PushedReports { get; } = [];

        public Task PushAsync(Guid issueId, WorkerReportSummary report, CancellationToken cancellationToken)
        {
            PushedReports.Add((issueId, report));
            return Task.CompletedTask;
        }
    }
}
