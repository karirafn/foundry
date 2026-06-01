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

namespace Foundry.IntegrationTests.Modules.Workers.Endpoints.IngestReportTests;

public sealed class WhenReportsAlreadyExist : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenReportsAlreadyExist()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<WorkerRunId> SeedActiveRunWithReportsAsync(int existingReportCount)
    {
        // No HTTP endpoint to create active runs — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        WorkerRunId runId = WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(issueId, runId);
        ActiveRun active = starting.Activate(ContainerId.From("container-xyz"));

        dbContext.Set<WorkerRun>().Add(active);

        for (int i = 1; i <= existingReportCount; i++)
        {
            WorkerReport report = WorkerReport.Create(runId, i, "progress", $"Step {i}");
            dbContext.Set<WorkerReport>().Add(report);
        }

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return runId;
    }

    [Fact]
    public async Task AssignsNextSequenceNumber()
    {
        // Arrange
        WorkerRunId runId = await SeedActiveRunWithReportsAsync(existingReportCount: 3);
        object request = new { Type = "progress", Content = "Step 4" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/workers/{runId.Value}/reports", UriKind.Relative),
            request,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        WorkerReportSummary? summary = await response.Content.ReadFromJsonAsync<WorkerReportSummary>(
            TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.SequenceNumber.ShouldBe(4);
    }
}
