using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Endpoints.IngestReportTests;

public sealed class WhenRunIsNotActive : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRunIsNotActive()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<WorkerRunId> SeedStartingRunAsync()
    {
        // Seed a run that is in 'starting' state — not yet active.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        WorkerRunId runId = WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(issueId, runId);

        dbContext.Set<WorkerRun>().Add(starting);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return runId;
    }

    [Fact]
    public async Task WhenRunIsNotActive_ReturnsBadRequest()
    {
        // Arrange
        WorkerRunId runId = await SeedStartingRunAsync();
        object request = new { Type = "progress", Content = "Building..." };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/workers/{runId.Value}/reports", UriKind.Relative),
            request,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
