using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Endpoints.GetReportsTests;

public sealed class WhenRunHasReports : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRunHasReports()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<WorkerRunId> SeedRunWithReportsAsync()
    {
        // No HTTP endpoint exists to create active runs — seeded directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        WorkerRunId runId = WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(issueId, runId);
        ActiveRun active = starting.Activate(ContainerId.From("container-with-reports"));

        dbContext.Set<WorkerRun>().Add(active);

        // Seed reports out of order to verify ordering by sequence number.
        dbContext.Set<WorkerReport>().Add(WorkerReport.Create(runId, 3, "progress", "Step 3"));
        dbContext.Set<WorkerReport>().Add(WorkerReport.Create(runId, 1, "progress", "Step 1"));
        dbContext.Set<WorkerReport>().Add(WorkerReport.Create(runId, 2, "progress", "Step 2"));

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return runId;
    }

    [Fact]
    public async Task ReturnsReportsOrderedBySequenceNumber()
    {
        // Arrange
        WorkerRunId runId = await SeedRunWithReportsAsync();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/{runId.Value}/reports", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        IReadOnlyList<WorkerReportSummary>? reports = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<WorkerReportSummary>>(TestContext.Current.CancellationToken);
        reports.ShouldNotBeNull();
        reports.Count.ShouldBe(3);
        reports[0].SequenceNumber.ShouldBe(1);
        reports[1].SequenceNumber.ShouldBe(2);
        reports[2].SequenceNumber.ShouldBe(3);
    }
}
