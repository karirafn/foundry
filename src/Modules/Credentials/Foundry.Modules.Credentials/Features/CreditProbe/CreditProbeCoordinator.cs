using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features.Broadcasts;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using DispatchPausedEvent = Foundry.Modules.Workers.Contracts.DispatchPaused;

namespace Foundry.Modules.Credentials.Features.CreditProbe;

/// <summary>
/// Process-wide singleton coordinator that runs the credit-probe container at most once
/// concurrently (single-flight) and routes the outcome to the appropriate state transitions.
/// <para>
/// Singleton lifetime: <see cref="_semaphore"/> provides process-wide single-flight state.
/// Scoped dependencies (<see cref="DbContext"/>, <see cref="IIntegrationEventDispatcher"/>,
/// <see cref="IGlobalSettingsQueries"/>, <see cref="IIntegrationEventProcessor"/>) are resolved
/// per-invocation via <see cref="IServiceScopeFactory"/>.
/// </para>
/// </summary>
internal sealed class CreditProbeCoordinator(
    IServiceScopeFactory scopeFactory,
    ICredentialsOrchestrator orchestrator,
    IProbeOutcomeClassifier classifier,
    ILoginSessionState loginSessionState,
    ILogger<CreditProbeCoordinator> logger) : ICreditProbeCoordinator, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public void Dispose() => _semaphore.Dispose();

    /// <summary>
    /// Attempts to run the credit probe. The probe is single-flight: if one is already
    /// in progress the call returns <see cref="CreditProbeResult.AlreadyRunning"/> immediately.
    /// </summary>
    public async Task<CreditProbeResult> TryRunProbeAsync(CancellationToken cancellationToken)
    {
        // Single-flight: non-blocking acquire; return immediately if probe already running.
        // CancellationToken.None: Wait(0) is a non-blocking try-acquire — cancellation is irrelevant.
        if (!_semaphore.Wait(0, CancellationToken.None))
        {
            return new CreditProbeResult.AlreadyRunning();
        }

        try
        {
            return await RunProbeInternalAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<CreditProbeResult> RunProbeInternalAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        IIntegrationEventDispatcher dispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();
        IGlobalSettingsQueries settingsQueries =
            scope.ServiceProvider.GetRequiredService<IGlobalSettingsQueries>();
        ISystemNotificationBroadcaster broadcaster =
            scope.ServiceProvider.GetRequiredService<ISystemNotificationBroadcaster>();
        IIntegrationEventProcessor integrationEventProcessor =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();

        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            logger.LogWarning(
                "Credit probe requested but no ClaudeAccount row exists; skipping.");
            return new CreditProbeResult.NoAccount();
        }

        // Guard: only probe when the account is actually Blocked.
        // If a manual resume cleared the block before the probe ran, this is a no-op so we
        // do not undo the restore (idempotency invariant).
        if (account.SpendState is not SpendState.Blocked)
        {
            return new CreditProbeResult.NotBlocked();
        }

        int intervalMinutes = await settingsQueries.GetProbeIntervalMinutesAsync(cancellationToken);
        DateTimeOffset nextArm = DateTimeOffset.UtcNow.AddMinutes(intervalMinutes);

        // Defer while a login is active — the login may be resolving a credential problem, and
        // probing concurrently could interfere. Re-arm so the probe is rescheduled.
        if (loginSessionState.IsLoginActive)
        {
            account.RearmProbe(nextArm);
            await dbContext.SaveChangesAsync(cancellationToken);
            await SendCreditsRearmBroadcastAsync(broadcaster, cancellationToken);
            return new CreditProbeResult.Deferred();
        }

        CreditProbeSpec spec = new(
            account.AuthMode,
            CreditProbeSpec.DefaultPrompt,
            CreditProbeSpec.DefaultTimeoutSeconds);
        Result<string> probeResult = await orchestrator.RunCreditProbeAsync(spec, cancellationToken);

        // Orchestrator-level failure → treat as InfrastructureFailure.
        if (probeResult is Result<string>.Failure failure)
        {
            logger.LogWarning(
                "Credit probe failed at the orchestrator level: {Error}. Re-arming probe.",
                failure.Error.Message);
            account.RearmProbe(nextArm);
            await dbContext.SaveChangesAsync(cancellationToken);
            await SendCreditsRearmBroadcastAsync(broadcaster, cancellationToken);
            return new CreditProbeResult.InfrastructureFailure(nextArm);
        }

        string logs = ((Result<string>.Success)probeResult).Value;
        ProbeOutcome outcome = classifier.Classify(logs);

        return outcome switch
        {
            ProbeOutcome.Available => await HandleAvailableAsync(
                account, dbContext, dispatcher, cancellationToken),
            ProbeOutcome.CreditsStillBlocked => await HandleStillBlockedAsync(
                account, dbContext, nextArm, broadcaster, cancellationToken),
            ProbeOutcome.UsageLimited usageLimited => await HandleUsageLimitedAsync(
                account, dbContext, dispatcher, integrationEventProcessor, usageLimited.ResetsAt, cancellationToken),
            ProbeOutcome.InfrastructureFailure => await HandleInfrastructureFailureAsync(
                account, dbContext, nextArm, broadcaster, cancellationToken),
            _ => throw new System.Diagnostics.UnreachableException(
                $"Unexpected ProbeOutcome: {outcome.GetType().Name}"),
        };
    }

    private async Task<CreditProbeResult> HandleAvailableAsync(
        ClaudeAccount account,
        DbContext dbContext,
        IIntegrationEventDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        bool changed = account.RestoreSpend();

        if (changed)
        {
            await dispatcher.DispatchAsync([new CreditsRestored()], cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Credit probe: credits are available. Spend state restored.");
        return new CreditProbeResult.Restored();
    }

    private async Task<CreditProbeResult> HandleStillBlockedAsync(
        ClaudeAccount account,
        DbContext dbContext,
        DateTimeOffset nextArm,
        ISystemNotificationBroadcaster broadcaster,
        CancellationToken cancellationToken)
    {
        account.RearmProbe(nextArm);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SendCreditsRearmBroadcastAsync(broadcaster, cancellationToken);

        logger.LogInformation(
            "Credit probe: credits still exhausted. Next probe at {NextProbeAt}.", nextArm);
        return new CreditProbeResult.StillBlocked(nextArm);
    }

    private async Task<CreditProbeResult> HandleUsageLimitedAsync(
        ClaudeAccount account,
        DbContext dbContext,
        IIntegrationEventDispatcher dispatcher,
        IIntegrationEventProcessor integrationEventProcessor,
        DateTimeOffset resetsAt,
        CancellationToken cancellationToken)
    {
        // Mirror PersistUsageLimitIfNeededAsync in WorkerDispatchService:
        // load GlobalSettings, call SetUsageLimitResetsAt, save, then deliver DispatchPaused
        // directly (ephemeral broadcast — no outbox row needed).
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is not null)
        {
            DateTimeOffset? resetsAtBefore = settings.UsageLimitResetsAt;
            settings.SetUsageLimitResetsAt(resetsAt);
            await dbContext.SaveChangesAsync(cancellationToken);

            bool resetsAtChanged = settings.UsageLimitResetsAt != resetsAtBefore;
            if (resetsAtChanged)
            {
                // DispatchPaused has only a SignalR broadcast consumer — no durable DB side-effect.
                // Route via IIntegrationEventProcessor for direct in-process delivery (no outbox row,
                // no relay latency on a transient notification).
                await TryDeliverDirectAsync(
                    integrationEventProcessor,
                    new DispatchPausedEvent(settings.UsageLimitResetsAt!.Value),
                    cancellationToken);
            }
        }
        else
        {
            logger.LogWarning(
                "Credit probe detected a usage limit but no GlobalSettings row exists; " +
                "DispatchPaused could not be persisted.");
        }

        // Design decision: UsageLimited proves the spend/money path works — the time-based usage
        // limit is now the operative constraint, not credit exhaustion. Clear the credit block so
        // the credit-exhaustion banner clears and the usage-limit timer takes over. Do NOT re-arm
        // the probe: it would fire while the usage limit is still in effect and add noise.
        bool creditChanged = account.RestoreSpend();
        if (creditChanged)
        {
            await dispatcher.DispatchAsync([new CreditsRestored()], cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Credit probe: usage-limited. Dispatch paused until {ResetsAt}.", resetsAt);

        return new CreditProbeResult.UsageLimited(resetsAt);
    }

    private async Task<CreditProbeResult> HandleInfrastructureFailureAsync(
        ClaudeAccount account,
        DbContext dbContext,
        DateTimeOffset nextArm,
        ISystemNotificationBroadcaster broadcaster,
        CancellationToken cancellationToken)
    {
        // Infrastructure failure: do not change spend state or raise credit-problem events.
        // The banner must not report a credit problem for infra failures — the root cause is
        // Docker / container infrastructure, not the credentials themselves.
        account.RearmProbe(nextArm);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SendCreditsRearmBroadcastAsync(broadcaster, cancellationToken);

        logger.LogWarning(
            "Credit probe: infrastructure failure. Probe re-armed at {NextProbeAt}.", nextArm);

        return new CreditProbeResult.InfrastructureFailure(nextArm);
    }

    /// <summary>
    /// Sends a credits re-arm broadcast directly. <c>IsActive:true</c> tells clients the credit
    /// block is still active and the countdown has been refreshed — they should re-sync from
    /// <c>/api/credentials</c> to pick up the new <c>nextProbeAt</c>.
    /// </summary>
    private static Task SendCreditsRearmBroadcastAsync(
        ISystemNotificationBroadcaster broadcaster,
        CancellationToken cancellationToken)
        => broadcaster.SendAsync(
            new SystemNotification(NotificationCategories.Credits, IsActive: true, Message: string.Empty),
            cancellationToken);

    /// <summary>
    /// Delivers an ephemeral event directly via <see cref="IIntegrationEventProcessor"/> without
    /// writing an outbox row. Use only for pure SignalR broadcast notifications with no durable
    /// DB consumer.
    /// </summary>
    private async Task TryDeliverDirectAsync(
        IIntegrationEventProcessor integrationEventProcessor,
        IIntegrationEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            await integrationEventProcessor.ProcessAsync(Guid.NewGuid(), @event, cancellationToken);
        }
#pragma warning disable CA1031 // Direct broadcast delivery failures (e.g. SignalR connection error) must not crash the coordinator; the warning is sufficient for triage.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to deliver ephemeral integration event {EventType} directly.",
                @event.GetType().Name);
        }
    }
}
