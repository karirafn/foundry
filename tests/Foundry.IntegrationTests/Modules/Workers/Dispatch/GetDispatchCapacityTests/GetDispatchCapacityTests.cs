using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Dispatch.GetDispatchCapacityTests;

public sealed class WhenDispatchCapacityRequested : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenDispatchCapacityRequested()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedSettingsAsync(int maxConcurrent)
    {
        // SettingsSeeder is a hosted service and is removed in tests — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdateLimits(maxConcurrent, timeoutMinutes: 60).IsSuccess.ShouldBeTrue();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedOccupyingRunsAsync()
    {
        // No POST endpoint for worker runs — seed directly via DbContext: one reservation,
        // one starting run and one active run each hold a slot; a completed run does not.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        dbContext.Set<DispatchReservation>().Add(
            DispatchReservation.Reserve(WorkerRunId.New(), DateTimeOffset.UtcNow));

        dbContext.Set<WorkerRun>().Add(StartingRun.Begin(IssueId.New(), WorkerRunId.New()));

        dbContext.Set<WorkerRun>().Add(StartingRun.Begin(IssueId.New(), WorkerRunId.New()).Activate(
            ContainerId.From("container-capacity-active"),
            BranchName.From("feat/1-capacity-active"),
            MonitoredRepositoryId.New()));

        dbContext.Set<WorkerRun>().Add(StartingRun.Begin(IssueId.New(), WorkerRunId.New())
            .Activate(
                ContainerId.From("container-capacity-completed"),
                BranchName.From("feat/2-capacity-completed"),
                MonitoredRepositoryId.New())
            .Complete(
                exitCode: 0,
                branchName: BranchName.From("feat/2-capacity-completed"),
                pullRequestUrl: null,
                resultSummary: null));

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<DispatchCapacity> GetCapacityAsync()
    {
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/workers/capacity", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        DispatchCapacity? capacity = await response.Content.ReadFromJsonAsync<DispatchCapacity>(
            TestContext.Current.CancellationToken);
        capacity.ShouldNotBeNull();
        return capacity;
    }

    [Fact]
    public async Task WhenSlotsAreHeld_CountsReservationsStartingAndActiveRuns()
    {
        // Arrange
        await SeedSettingsAsync(maxConcurrent: 5);
        await SeedOccupyingRunsAsync();

        // Act
        DispatchCapacity capacity = await GetCapacityAsync();

        // Assert
        capacity.ShouldSatisfyAllConditions(
            () => capacity.MaxConcurrent.ShouldBe(5),
            () => capacity.OccupiedSlots.ShouldBe(3),
            () => capacity.FreeSlots.ShouldBe(2));
    }

    [Fact]
    public async Task WhenOccupancyExceedsLimit_ReturnsZeroFreeSlots()
    {
        // Arrange — the limit was lowered below the number of runs already in flight.
        await SeedSettingsAsync(maxConcurrent: 1);
        await SeedOccupyingRunsAsync();

        // Act
        DispatchCapacity capacity = await GetCapacityAsync();

        // Assert
        capacity.ShouldSatisfyAllConditions(
            () => capacity.MaxConcurrent.ShouldBe(1),
            () => capacity.OccupiedSlots.ShouldBe(3),
            () => capacity.FreeSlots.ShouldBe(0));
    }

    [Fact]
    public async Task WhenNothingIsRunning_AllSlotsAreFree()
    {
        // Arrange
        await SeedSettingsAsync(maxConcurrent: 3);

        // Act
        DispatchCapacity capacity = await GetCapacityAsync();

        // Assert
        capacity.ShouldSatisfyAllConditions(
            () => capacity.MaxConcurrent.ShouldBe(3),
            () => capacity.OccupiedSlots.ShouldBe(0),
            () => capacity.FreeSlots.ShouldBe(3));
    }
}
