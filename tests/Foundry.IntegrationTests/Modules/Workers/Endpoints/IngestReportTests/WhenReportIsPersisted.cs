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

public sealed class WhenReportIsPersisted : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenReportIsPersisted()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<WorkerRunId> SeedActiveRunAsync()
    {
        // No HTTP endpoint to create active runs — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        WorkerRunId runId = WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(issueId, runId);
        ActiveRun active = starting.Activate(ContainerId.From("container-persist"));

        dbContext.Set<WorkerRun>().Add(active);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return runId;
    }

    [Fact]
    public async Task SavesReportToDatabase()
    {
        // Arrange
        WorkerRunId runId = await SeedActiveRunAsync();
        object request = new { Type = "progress", Content = "Compiling..." };

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

        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        WorkerReport? persisted = await dbContext.Set<WorkerReport>()
            .FirstOrDefaultAsync(
                r => r.Id == WorkerReportId.From(summary.Id),
                TestContext.Current.CancellationToken);

        persisted.ShouldNotBeNull();
        persisted.ShouldSatisfyAllConditions(
            () => persisted.WorkerRunId.ShouldBe(runId),
            () => persisted.ReportType.ShouldBe("progress"),
            () => persisted.Content.ShouldBe("Compiling..."),
            () => persisted.SequenceNumber.ShouldBe(1));
    }
}
