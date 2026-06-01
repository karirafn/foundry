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

public sealed class WhenReportFieldsAreProjected : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenReportFieldsAreProjected()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<(WorkerRunId RunId, WorkerReport Report)> SeedRunWithOneReportAsync()
    {
        // No HTTP endpoint exists to create active runs — seeded directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        WorkerRunId runId = WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(issueId, runId);
        ActiveRun active = starting.Activate(ContainerId.From("container-field-check"));

        dbContext.Set<WorkerRun>().Add(active);

        WorkerReport report = WorkerReport.Create(runId, 1, "final", "Done!");
        dbContext.Set<WorkerReport>().Add(report);

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (runId, report);
    }

    [Fact]
    public async Task WhenReportFieldsAreProjected_ReturnsCorrectFieldsForEachReport()
    {
        // Arrange
        (WorkerRunId runId, WorkerReport report) = await SeedRunWithOneReportAsync();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/{runId.Value}/reports", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        IReadOnlyList<WorkerReportSummary>? reports = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<WorkerReportSummary>>(TestContext.Current.CancellationToken);
        reports.ShouldNotBeNull();
        reports.Count.ShouldBe(1);

        WorkerReportSummary summary = reports[0];
        summary.ShouldSatisfyAllConditions(
            () => summary.Id.ShouldBe(report.Id.Value),
            () => summary.WorkerRunId.ShouldBe(runId.Value),
            () => summary.SequenceNumber.ShouldBe(1),
            () => summary.ReportType.ShouldBe("final"),
            () => summary.Content.ShouldBe("Done!"),
            () => summary.IngestedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
    }
}
