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

public sealed class WhenRunIsActive : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRunIsActive()
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
        // No HTTP endpoint exists to create active runs — seeded directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        WorkerRunId runId = WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(issueId, runId);
        ActiveRun active = starting.Activate(ContainerId.From("container-abc"));

        dbContext.Set<WorkerRun>().Add(active);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return runId;
    }

    [Fact]
    public async Task WhenRunIsActive_ReturnsCreatedWithSummary()
    {
        // Arrange
        WorkerRunId runId = await SeedActiveRunAsync();
        object request = new { Type = "progress", Content = "Building the thing..." };

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
        summary.ShouldSatisfyAllConditions(
            () => summary.WorkerRunId.ShouldBe(runId.Value),
            () => summary.SequenceNumber.ShouldBe(1),
            () => summary.ReportType.ShouldBe("progress"),
            () => summary.Content.ShouldBe("Building the thing..."),
            () => summary.IngestedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)));
    }
}
