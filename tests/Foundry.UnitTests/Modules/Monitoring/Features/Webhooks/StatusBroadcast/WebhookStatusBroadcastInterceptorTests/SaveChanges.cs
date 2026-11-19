using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.StatusBroadcast.WebhookStatusBroadcastInterceptorTests;

public sealed class SaveChanges : IAsyncDisposable
{
    private readonly InterceptorHarness _harness = new();

    ValueTask IAsyncDisposable.DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public void AddingARepository_BroadcastsNothing()
    {
        // Act
        _harness.Seed("alice/repo", WebhookStatus.Pending);

        // Assert
        _harness.Broadcaster.Changed.ShouldBeEmpty();
    }

    [Fact]
    public async Task StatusChangeSavedAsync_BroadcastsTheRepositoryOnce()
    {
        // Arrange
        MonitoredRepository repository = _harness.Seed("alice/repo", WebhookStatus.Pending);
        repository.RecordWebhookDelivery();

        // Act
        await _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        _harness.Broadcaster.Changed.ShouldBe([repository.Id]);
    }

    [Fact]
    public void StatusChangeSavedSync_BroadcastsTheRepositoryOnce()
    {
        // Arrange
        MonitoredRepository repository = _harness.Seed("alice/repo", WebhookStatus.Pending);
        repository.RecordWebhookDelivery();

        // Act
        _harness.DbContext.SaveChanges();

        // Assert
        _harness.Broadcaster.Changed.ShouldBe([repository.Id]);
    }

    [Fact]
    public async Task TwoRepositoriesChangedInOneSave_BroadcastsEach()
    {
        // Arrange
        MonitoredRepository first = _harness.Seed("alice/first", WebhookStatus.Pending);
        MonitoredRepository second = _harness.Seed("alice/second", WebhookStatus.Active);
        first.RecordWebhookDelivery();
        second.RecordMissedChange();

        // Act
        await _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        _harness.Broadcaster.Changed.ShouldBe([first.Id, second.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task ModifiedWithoutStatusChange_BroadcastsNothing()
    {
        // Arrange
        MonitoredRepository repository = _harness.Seed("alice/repo", WebhookStatus.Active);
        repository.MarkWebhookRegistered("new-stamp");

        // Act
        await _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        _harness.DbContext.Entry(repository).Property(r => r.WebhookStatus).CurrentValue.ShouldBe(WebhookStatus.Active);
        _harness.Broadcaster.Changed.ShouldBeEmpty();
    }

    [Fact]
    public async Task FailedSave_BroadcastsNothingAndTheNextSaveOnlyItsOwnChanges()
    {
        // Arrange
        MonitoredRepository failing = _harness.Seed("alice/failing", WebhookStatus.Pending);
        MonitoredRepository next = _harness.Seed("alice/next", WebhookStatus.Pending);
        failing.RecordWebhookDelivery();
        _harness.DbContext.Set<MonitoredRepository>().Add(new MonitoredRepositoryBuilder()
            .WithSlug(failing.Slug)
            .WithPosition(5)
            .Build());

        // Act
        await Should.ThrowAsync<DbUpdateException>(() => _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
        _harness.DbContext.ChangeTracker.Clear();
        MonitoredRepository reloaded = await _harness.DbContext.Set<MonitoredRepository>()
            .SingleAsync(r => r.Id == next.Id, TestContext.Current.CancellationToken);
        reloaded.RecordWebhookDelivery();
        await _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        _harness.Broadcaster.Changed.ShouldBe([next.Id]);
    }

    [Fact]
    public async Task CancelledSave_BroadcastsNothing()
    {
        // Arrange
        MonitoredRepository repository = _harness.Seed("alice/repo", WebhookStatus.Pending);
        repository.RecordWebhookDelivery();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        // Act
        await Should.ThrowAsync<OperationCanceledException>(
            () => _harness.DbContext.SaveChangesAsync(cancellation.Token));

        // Assert
        _harness.Broadcaster.Changed.ShouldBeEmpty();
    }

    [Fact]
    public async Task BroadcasterThrows_LogsAWarningAndTheSaveReturns()
    {
        // Arrange
        MonitoredRepository repository = _harness.Seed("alice/repo", WebhookStatus.Pending);
        repository.RecordWebhookDelivery();
        _harness.Broadcaster.ThrowOnSend = new InvalidOperationException("hub down");

        // Act
        int written = await _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        written.ShouldBe(1);
        _harness.Logger.Entries.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Level.ShouldBe(LogLevel.Warning),
            e => e.Exception.ShouldBeOfType<InvalidOperationException>());
    }
}
