using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Process-wide singleton coordinator that runs the usage-report container at most once
/// concurrently (single-flight JOIN semantics) and routes the outcome to the appropriate
/// state transitions and broadcast.
/// <para>
/// Singleton lifetime: <see cref="_gate"/> provides process-wide single-flight state via an
/// in-flight task tracked under a <see cref="Lock"/>. Scoped dependencies (<see cref="DbContext"/>)
/// are resolved per-invocation via <see cref="IServiceScopeFactory"/>.
/// </para>
/// <para>
/// <b>JOIN semantics (ADR 0052):</b> unlike the credit-probe single-flight (fail-fast), a second
/// concurrent caller joins the in-flight poll — it awaits the same task and receives the same
/// result without starting a second container. This ensures that an event-triggered poll arriving
/// while a periodic tick is in-flight always observes a fresh result rather than being silently
/// dropped.
/// </para>
/// </summary>
internal sealed class UsagePollCoordinator(
    IServiceScopeFactory scopeFactory,
    ICredentialsOrchestrator orchestrator,
    ILoginSessionState loginSessionState,
    IUsageSnapshotState state,
    IUsageSnapshotStateMutator stateMutator,
    IUsageSnapshotBroadcaster broadcaster,
    ILogger<UsagePollCoordinator> logger) : IUsagePollCoordinator
{
    private readonly Lock _gate = new();
    private Task<UsageSnapshot?>? _inFlightTask;

    /// <summary>
    /// Runs a usage poll. If a poll is already in flight, the caller joins it (JOIN semantics):
    /// it awaits the in-flight task and returns the snapshot it produced, without starting a
    /// second container. When no poll is in flight, starts one.
    /// </summary>
    public Task<UsageSnapshot?> PollAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_inFlightTask is not null)
            {
                // JOIN: await the running poll — no second container starts.
                // The joiner receives the snapshot produced by the in-flight poll.
                logger.LogDebug("Usage poll requested while one is in flight; joining the in-flight poll.");
                return _inFlightTask;
            }

            Task<UsageSnapshot?> task = RunPollCoreAsync(cancellationToken);
            _inFlightTask = task;

            // Clear the in-flight reference when the task completes (success or fault).
            // ContinueWith always runs because the continuation handles both outcomes.
            // Log infrastructure faults (e.g. scope creation or DB failures) that escape
            // RunPollCoreAsync before the handled paths (HandleFailure/HandleUnparseable) can run.
            _ = task.ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                    {
                        logger.LogError(
                            t.Exception?.InnerException,
                            "Unhandled exception in usage poll.");
                    }

                    lock (_gate)
                    {
                        _inFlightTask = null;
                    }
                },
                TaskScheduler.Default);

            return task;
        }
    }

    private async Task<UsageSnapshot?> RunPollCoreAsync(CancellationToken cancellationToken)
    {
        // Load the account to check gating conditions.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        // Skip: no account configured yet.
        if (account is null)
        {
            logger.LogDebug("Usage poll skipped — no ClaudeAccount row exists.");
            return null;
        }

        // Skip: account cannot report usage (API-key mode, no committed identity, or invalid).
        if (!account.CanReportUsage)
        {
            logger.LogDebug("Usage poll skipped — CanReportUsage is false.");
            return null;
        }

        // Skip: login in progress — the interactive session takes priority.
        if (loginSessionState.IsLoginActive)
        {
            logger.LogDebug("Usage poll skipped — login session is active.");
            return null;
        }

        string accountEmail = account.OAuthAccountEmail!;
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;

        UsageReportSpec spec = new(UsageReportSpec.DefaultTimeoutSeconds);
        Result<string> orchestratorResult = await orchestrator.RunUsageReportAsync(spec, cancellationToken);

        if (orchestratorResult is Result<string>.Failure failure)
        {
            logger.LogWarning(
                "Usage report orchestrator failed: {Error}. Retaining last good snapshot.",
                failure.Error.Message);
            return HandleFailure(capturedAt);
        }

        string log = ((Result<string>.Success)orchestratorResult).Value;
        UsageParseResult parseResult = UsageReportParser.Parse(log, accountEmail, capturedAt);

        return parseResult switch
        {
            UsageParseResult.Parsed parsed => HandleParsed(parsed.Snapshot),
            UsageParseResult.NoUsageReport => HandleNoUsageReport(),
            UsageParseResult.Unparseable => HandleUnparseable(capturedAt),
            _ => throw new System.Diagnostics.UnreachableException(
                $"Unexpected UsageParseResult: {parseResult.GetType().Name}"),
        };

        // Local function — avoids making the async method large while keeping the broadcast inline.
        UsageSnapshot? HandleParsed(UsageSnapshot snapshot)
        {
            stateMutator.SetSnapshot(snapshot);
            UsageSnapshot? current = state.Current;

            // Fire-and-forget broadcast — use CancellationToken.None so a cancelled caller
            // token does not suppress the push to connected clients.
            _ = TryBroadcastAsync(current, CancellationToken.None);

            logger.LogDebug(
                "Usage poll succeeded. Snapshot captured at {CapturedAt}.",
                snapshot.CapturedAt);

            logger.LogInformation(
                "Usage poll succeeded. Snapshot captured at {CapturedAt}.",
                snapshot.CapturedAt);

            return current;
        }

        UsageSnapshot? HandleNoUsageReport()
        {
            stateMutator.Clear();

            _ = TryBroadcastAsync(null, CancellationToken.None);

            logger.LogInformation(
                "Usage poll returned no usage report (API-key / usage-based billing). Snapshot cleared.");

            return null;
        }

        UsageSnapshot? HandleUnparseable(DateTimeOffset failedAt)
        {
            return HandleFailure(failedAt);
        }
    }

    private UsageSnapshot? HandleFailure(DateTimeOffset failedAt)
    {
        stateMutator.MarkRefreshFailed(failedAt);
        UsageSnapshot? current = state.Current;

        _ = TryBroadcastAsync(current, CancellationToken.None);

        return current;
    }

    private async Task TryBroadcastAsync(UsageSnapshot? snapshot, CancellationToken cancellationToken)
    {
        try
        {
            await broadcaster.SendSnapshotAsync(snapshot, cancellationToken);
        }
#pragma warning disable CA1031 // Broadcast failures (e.g. SignalR connection error) must not crash the coordinator; the warning is sufficient for triage.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to broadcast usage snapshot update.");
        }
    }
}
