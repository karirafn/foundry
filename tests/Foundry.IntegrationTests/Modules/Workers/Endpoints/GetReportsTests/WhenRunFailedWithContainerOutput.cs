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

public sealed class WhenRunFailedWithContainerOutput : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRunFailedWithContainerOutput()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<WorkerRunId> SeedFailedRunWithContainerOutputAsync(string containerOutput)
    {
        // No HTTP endpoint exists to create failed runs — seeded directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        WorkerRunId runId = WorkerRunId.New();
        StartingRun starting = StartingRun.Begin(issueId, runId);
        ActiveRun active = starting.Activate(ContainerId.From("container-failed-with-output"));
        FailedRun failed = active.Fail(new FailureReason.NonZeroExit(1), containerOutput);

        dbContext.Set<WorkerRun>().Add(failed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return runId;
    }

    [Fact]
    public async Task WhenRunFailedWithContainerOutput_ReturnsContainerOutputInResponse()
    {
        // Arrange
        const string expectedOutput = "Error: something went wrong\nProcess exited with code 1";
        WorkerRunId runId = await SeedFailedRunWithContainerOutputAsync(expectedOutput);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/{runId.Value}/reports", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        GetReportsResponse? result = await response.Content
            .ReadFromJsonAsync<GetReportsResponse>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.ContainerOutput.ShouldBe(expectedOutput);
        result.Reports.ShouldBeEmpty();
    }
}
