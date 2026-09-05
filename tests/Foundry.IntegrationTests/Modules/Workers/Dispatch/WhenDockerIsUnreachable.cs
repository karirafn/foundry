using System.Runtime.CompilerServices;

using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Dispatch;

/// <summary>
/// Pre-ship integration test for design decision D4 (ADR 0069): the reservation sweep
/// is a separate service with no Docker dependency, so it can release stranded
/// <see cref="DispatchReservation"/> rows even when the Docker daemon is unreachable.
///
/// <para>
/// This proves that a Docker outage cannot cause permanent dispatch deadlock:
/// stale reservations are released by <see cref="StaleReservationService"/> regardless
/// of whether <see cref="IWorkerOrchestrator"/> throws a connectivity exception.
/// </para>
/// </summary>
public sealed class WhenDockerIsUnreachable : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenDockerIsUnreachable()
    {
        // Replace the real Docker orchestrator with one that always throws a connectivity
        // exception — this simulates a completely unreachable Docker daemon.
        // Re-register StaleReservationService as a singleton so the test can call
        // TickForTest deterministically without starting the background timer loop.
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IWorkerOrchestrator>();
            services.AddSingleton<IWorkerOrchestrator>(new ConnectivityErrorOrchestrator());

            services.AddSingleton(sp => new StaleReservationService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<StaleReservationService>.Instance));
        });

        // EnsureCreated is called inside FoundryWebAppFactory.ConfigureWebHost.
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task<DispatchReservation> SeedStaleReservationAsync()
    {
        // Seed a reservation with a ReservedAt far enough in the past to be stale
        // (beyond the 2-minute StaleReservationThreshold).
        DateTimeOffset staleTime = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        DispatchReservation reservation = new DispatchReservationBuilder()
            .WithReservedAt(staleTime)
            .Build();

        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Set<DispatchReservation>().Add(reservation);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return reservation;
    }

    [Fact]
    public async Task WhenDockerDaemonUnreachable_StaleReservationIsStillReleased()
    {
        // Arrange — seed a stale reservation that should be swept.
        DispatchReservation staleReservation = await SeedStaleReservationAsync();

        StaleReservationService sweep = _factory.Services.GetRequiredService<StaleReservationService>();

        // Act — tick the sweep with Docker unreachable.
        // StaleReservationService must complete without throwing and must delete the reservation.
        await Should.NotThrowAsync(
            () => sweep.TickForTest(TestContext.Current.CancellationToken));

        // Assert — the stale reservation was deleted despite the Docker daemon being unreachable.
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext assertDb = assertScope.ServiceProvider.GetRequiredService<DbContext>();

        DispatchReservation? persisted = await assertDb.Set<DispatchReservation>()
            .FirstOrDefaultAsync(
                r => r.Id == staleReservation.Id,
                TestContext.Current.CancellationToken);

        persisted.ShouldBeNull();
    }

    [Fact]
    public async Task WhenDockerDaemonUnreachable_FreshReservationIsNotReleased()
    {
        // Arrange — seed a fresh reservation (within the 2-minute threshold).
        DateTimeOffset freshTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(30);
        DispatchReservation freshReservation = new DispatchReservationBuilder()
            .WithReservedAt(freshTime)
            .Build();

        using (IServiceScope seedScope = _factory.Services.CreateScope())
        {
            DbContext seedDb = seedScope.ServiceProvider.GetRequiredService<DbContext>();
            seedDb.Set<DispatchReservation>().Add(freshReservation);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        StaleReservationService sweep = _factory.Services.GetRequiredService<StaleReservationService>();

        // Act — sweep runs; fresh reservation is below the stale threshold.
        await sweep.TickForTest(TestContext.Current.CancellationToken);

        // Assert — fresh reservation remains; Docker unreachability has no effect.
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext assertDb = assertScope.ServiceProvider.GetRequiredService<DbContext>();

        DispatchReservation? persisted = await assertDb.Set<DispatchReservation>()
            .FirstOrDefaultAsync(
                r => r.Id == freshReservation.Id,
                TestContext.Current.CancellationToken);

        persisted.ShouldNotBeNull();
    }

    /// <summary>
    /// Simulates a Docker daemon that is completely unreachable — every method throws
    /// <see cref="HttpRequestException"/> as the Docker SDK would on a connection failure.
    /// </summary>
    private sealed class ConnectivityErrorOrchestrator : IWorkerOrchestrator
    {
        private static HttpRequestException ConnectivityError()
            => new("Docker daemon connection refused (simulated for test)");

        public Task<Result<ContainerId>> StartAsync(
            WorkerContainerSpec spec,
            CancellationToken cancellationToken)
            => throw ConnectivityError();

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => throw ConnectivityError();

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => throw ConnectivityError();

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => throw ConnectivityError();

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.FromException(ConnectivityError());
            yield break;
        }

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => throw ConnectivityError();

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => throw ConnectivityError();

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => throw ConnectivityError();

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => throw ConnectivityError();
    }
}
