using System.Data.Common;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Webhooks.StatusBroadcast;

/// <summary>
/// Broadcasts every committed change of a <see cref="MonitoredRepository.WebhookStatus"/>, so every save
/// site that changes the status reaches the dashboard without having to remember to.
/// </summary>
/// <remarks>
/// <para>
/// The changed repositories are recorded in <c>SavingChanges</c>: by <c>SavedChanges</c> the change
/// tracker has accepted the changes and the original values are gone. Each <c>SavingChanges</c> replaces
/// the previous record, so a save that throws or is cancelled leaves nothing behind.
/// </para>
/// <para>
/// A save outside an explicit transaction is committed when it returns, so it is broadcast then. A save
/// inside one is held until the transaction commits and dropped when it rolls back or fails.
/// </para>
/// <para>
/// Limits: writes that bypass the change tracker (<c>ExecuteUpdate</c>, raw SQL) are never seen, and
/// transactions EF Core does not own (<c>TransactionScope</c>, <c>Database.UseTransaction</c>) bypass the
/// commit and rollback callbacks.
/// </para>
/// </remarks>
internal sealed class WebhookStatusBroadcastInterceptor(
    IRepositoryWebhookBroadcaster broadcaster,
    ILogger<WebhookStatusBroadcastInterceptor> logger)
    : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly List<MonitoredRepositoryId> _awaitingCommit = [];
    private IReadOnlyList<MonitoredRepositoryId> _saving = [];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        _saving = ChangedRepositories(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        _saving = ChangedRepositories(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        IReadOnlyList<MonitoredRepositoryId> committed = TakeCommitted(eventData.Context);
        BroadcastAsync(committed).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MonitoredRepositoryId> committed = TakeCommitted(eventData.Context);
        await BroadcastAsync(committed);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _saving = [];

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _saving = [];
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData) => _saving = [];

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _saving = [];
        return Task.CompletedTask;
    }

    // A transaction disposed without commit or rollback raises no callback, so anything still held
    // belongs to such a transaction and is dropped when the next one starts.
    public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        _awaitingCommit.Clear();
        return result;
    }

    public ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        _awaitingCommit.Clear();
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        => BroadcastAsync(TakeAwaitingCommit()).GetAwaiter().GetResult();

    public Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
        => BroadcastAsync(TakeAwaitingCommit());

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        => _awaitingCommit.Clear();

    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _awaitingCommit.Clear();
        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
        => _awaitingCommit.Clear();

    public Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _awaitingCommit.Clear();
        return Task.CompletedTask;
    }

    private static List<MonitoredRepositoryId> ChangedRepositories(DbContext? context)
    {
        if (context is null)
        {
            return [];
        }

        return context.ChangeTracker.Entries<MonitoredRepository>()
            .Where(e => e.State == EntityState.Modified && WebhookStatusChanged(e))
            .Select(e => e.Entity.Id)
            .ToList();
    }

    private static bool WebhookStatusChanged(EntityEntry<MonitoredRepository> entry)
    {
        PropertyEntry<MonitoredRepository, Domain.ValueObjects.WebhookStatus> status =
            entry.Property(r => r.WebhookStatus);
        return status.IsModified && status.OriginalValue != status.CurrentValue;
    }

    private IReadOnlyList<MonitoredRepositoryId> TakeCommitted(DbContext? context)
    {
        IReadOnlyList<MonitoredRepositoryId> saved = _saving;
        _saving = [];

        if (context?.Database.CurrentTransaction is null)
        {
            return saved;
        }

        foreach (MonitoredRepositoryId id in saved.Where(id => !_awaitingCommit.Contains(id)))
        {
            _awaitingCommit.Add(id);
        }

        return [];
    }

    private List<MonitoredRepositoryId> TakeAwaitingCommit()
    {
        List<MonitoredRepositoryId> committed = [.. _awaitingCommit];
        _awaitingCommit.Clear();
        return committed;
    }

    private async Task BroadcastAsync(IReadOnlyList<MonitoredRepositoryId> repositoryIds)
    {
        foreach (MonitoredRepositoryId repositoryId in repositoryIds)
        {
            try
            {
                // The change is already committed, so the caller's cancellation no longer applies to it.
                await broadcaster.RepositoryWebhookChangedAsync(repositoryId, CancellationToken.None);
            }
#pragma warning disable CA1031 // A committed save must never fail over a notification
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogWarning(
                    ex,
                    "Could not broadcast the webhook status change of repository {RepositoryId}.",
                    repositoryId.Value);
            }
        }
    }
}
