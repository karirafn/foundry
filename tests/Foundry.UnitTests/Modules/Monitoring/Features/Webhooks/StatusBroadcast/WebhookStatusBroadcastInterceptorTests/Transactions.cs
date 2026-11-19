using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore.Storage;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.StatusBroadcast.WebhookStatusBroadcastInterceptorTests;

public sealed class Transactions : IAsyncDisposable
{
    private readonly InterceptorHarness _harness = new();

    ValueTask IAsyncDisposable.DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task SaveInsideTransaction_BroadcastsNothingUntilCommit()
    {
        // Arrange
        MonitoredRepository repository = _harness.Seed("alice/repo", WebhookStatus.Pending);
        await using IDbContextTransaction transaction = await _harness.DbContext.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        repository.RecordWebhookDelivery();

        // Act
        await _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        _harness.Broadcaster.Changed.ShouldBeEmpty();
    }

    [Fact]
    public async Task CommitAsync_BroadcastsTheChangesSavedInTheTransaction()
    {
        // Arrange
        MonitoredRepository repository = _harness.Seed("alice/repo", WebhookStatus.Pending);
        await using IDbContextTransaction transaction = await _harness.DbContext.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        repository.RecordWebhookDelivery();
        await _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        // Assert
        _harness.Broadcaster.Changed.ShouldBe([repository.Id]);
    }

    [Fact]
    public void CommitSync_BroadcastsTheChangesSavedInTheTransaction()
    {
        // Arrange
        MonitoredRepository repository = _harness.Seed("alice/repo", WebhookStatus.Pending);
        using IDbContextTransaction transaction = _harness.DbContext.Database.BeginTransaction();
        repository.RecordWebhookDelivery();
        _harness.DbContext.SaveChanges();

        // Act
        transaction.Commit();

        // Assert
        _harness.Broadcaster.Changed.ShouldBe([repository.Id]);
    }

    [Fact]
    public async Task RepositorySavedTwiceInOneTransaction_BroadcastsItOnce()
    {
        // Arrange
        MonitoredRepository repository = _harness.Seed("alice/repo", WebhookStatus.Pending);
        await using IDbContextTransaction transaction = await _harness.DbContext.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        repository.RecordWebhookDelivery();
        await _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        repository.RecordMissedChange();
        await _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        // Assert
        _harness.Broadcaster.Changed.ShouldBe([repository.Id]);
    }

    [Fact]
    public async Task RollbackAsync_BroadcastsNothing()
    {
        // Arrange
        MonitoredRepository repository = _harness.Seed("alice/repo", WebhookStatus.Pending);
        await using IDbContextTransaction transaction = await _harness.DbContext.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        repository.RecordWebhookDelivery();
        await _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);

        // Assert
        _harness.Broadcaster.Changed.ShouldBeEmpty();
    }

    [Fact]
    public async Task TransactionDisposedWithoutCommit_IsNotBroadcastByTheNextCommit()
    {
        // Arrange
        MonitoredRepository abandoned = _harness.Seed("alice/abandoned", WebhookStatus.Pending);
        MonitoredRepository committed = _harness.Seed("alice/committed", WebhookStatus.Pending);
        await using (IDbContextTransaction first = await _harness.DbContext.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            abandoned.RecordWebhookDelivery();
            await _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _harness.DbContext.ChangeTracker.Clear();

        // Act
        await using IDbContextTransaction second = await _harness.DbContext.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        MonitoredRepository reloaded = await _harness.DbContext.Set<MonitoredRepository>()
            .FindAsync([committed.Id], TestContext.Current.CancellationToken) ?? throw new InvalidOperationException("Seeded repository missing.");
        reloaded.RecordWebhookDelivery();
        await _harness.DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await second.CommitAsync(TestContext.Current.CancellationToken);

        // Assert
        _harness.Broadcaster.Changed.ShouldBe([committed.Id]);
    }
}
