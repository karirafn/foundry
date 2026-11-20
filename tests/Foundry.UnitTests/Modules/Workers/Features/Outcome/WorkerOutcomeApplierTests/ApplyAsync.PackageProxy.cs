using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerOutcomeApplierTests;

public sealed partial class ApplyAsync
{
    private static readonly DateTimeOffset ProxyFailedAt = new(2026, 11, 19, 7, 39, 0, TimeSpan.Zero);

    [Fact]
    public async Task WhenARunFailsPackageSourceUnreachable_PausesThePackageProxyAndBroadcasts()
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        await SeedSettingsAsync(_ => { });
        CapturingSystemNotificationBroadcaster broadcaster = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, new RecordingOrchestrator(), broadcaster: broadcaster, timeProvider: new FakeTimeProvider(ProxyFailedAt));

        // Act
        await sut.ApplyAsync(PackageSourceUnreachableFailure(), activeRun, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        DispatchPause settings = await assertDb.Set<DispatchPause>().SingleAsync(TestContext.Current.CancellationToken);
        settings.PackageProxyPause.ShouldBe(PackageProxyPause.Start(ProxyFailedAt));
        broadcaster.SentNotifications.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            n => n.Category.ShouldBe("dispatch"),
            n => n.IsActive.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenThePackageProxyIsAlreadyPaused_LeavesItsBackoffAndDoesNotBroadcast()
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        await SeedSettingsAsync(settings =>
        {
            settings.PausePackageProxy(ProxyFailedAt.AddMinutes(-5));
            settings.RecordPackageProxyProbeUnreachable(ProxyFailedAt.AddMinutes(-4));
        });
        CapturingSystemNotificationBroadcaster broadcaster = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, new RecordingOrchestrator(), broadcaster: broadcaster, timeProvider: new FakeTimeProvider(ProxyFailedAt));

        // Act
        await sut.ApplyAsync(PackageSourceUnreachableFailure(), activeRun, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        DispatchPause settings = await assertDb.Set<DispatchPause>().SingleAsync(TestContext.Current.CancellationToken);
        settings.PackageProxyPause.ShouldBe(
            PackageProxyPause.Start(ProxyFailedAt.AddMinutes(-5)).AfterUnreachableProbe(ProxyFailedAt.AddMinutes(-4)));
        broadcaster.SentNotifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenARunFailsForAnotherReason_DoesNotPauseThePackageProxy()
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        await SeedSettingsAsync(_ => { });
        CapturingSystemNotificationBroadcaster broadcaster = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, new RecordingOrchestrator(), broadcaster: broadcaster);

        // Act
        await sut.ApplyAsync(
            new WorkerOutcome.Failure(new FailureReason.WorkerBootstrapFailed("bad"), null, null),
            activeRun,
            TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        DispatchPause settings = await assertDb.Set<DispatchPause>().SingleAsync(TestContext.Current.CancellationToken);
        settings.PackageProxyPause.ShouldBeNull();
        broadcaster.SentNotifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenARunFailsPackageRestoreFailed_DoesNotPauseThePackageProxy()
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        await SeedSettingsAsync(_ => { });
        CapturingSystemNotificationBroadcaster broadcaster = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, new RecordingOrchestrator(), broadcaster: broadcaster, timeProvider: new FakeTimeProvider(ProxyFailedAt));

        // Act
        await sut.ApplyAsync(
            new WorkerOutcome.Failure(new FailureReason.PackageRestoreFailed("App.sln: error NU1301"), null, null),
            activeRun,
            TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings settings = await assertDb.Set<GlobalSettings>().SingleAsync(TestContext.Current.CancellationToken);
        settings.PackageProxyPause.ShouldBeNull();
        broadcaster.SentNotifications.ShouldBeEmpty();
    }

    private static WorkerOutcome.Failure PackageSourceUnreachableFailure() =>
        new(new FailureReason.PackageSourceUnreachable("could not reach host.docker.internal:5287"), null, null);

    private async Task SeedSettingsAsync(Action<DispatchPause> configure)
    {
        await using FoundryDbContext seedDb = CreateDbContext();
        DispatchPause settings = DispatchPause.Create();
        configure(settings);
        seedDb.Set<DispatchPause>().Add(settings);
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
