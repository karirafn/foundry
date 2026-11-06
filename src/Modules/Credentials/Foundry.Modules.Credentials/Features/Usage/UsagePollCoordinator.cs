using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Process-wide singleton coordinator that runs a usage poll at most once concurrently
/// (single-flight JOIN semantics) and routes the outcome to the appropriate state transitions and
/// broadcast. A poll runs <c>claude -p "/usage"</c> and, only when its report carries no rate limits,
/// the probe-turn fallback after it — sequentially, inside the same single-flight task (ADR 0097).
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
    /// <summary>
    /// Minimum age of the last <em>successful</em> snapshot before an on-demand refresh may start
    /// a new poll. Protects the rate-limited upstream usage endpoint from rapid clicks and
    /// multiple tabs. Failed polls do not count — a refresh right after a failure is allowed.
    /// </summary>
    internal static readonly TimeSpan RefreshFloor = TimeSpan.FromSeconds(30);

    /// <summary>Maximum length of the output sample logged when a usage report is unparseable.</summary>
    internal const int UnparseableSampleMaxLength = 500;

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

            return StartPollLocked(cancellationToken);
        }
    }

    /// <summary>
    /// Requests an on-demand poll and returns without waiting for the container. The gating
    /// checks mirror <see cref="RunPollCoreAsync"/> so the caller gets a visible refusal instead
    /// of a silently skipped poll; the in-flight and floor checks run under <see cref="_gate"/>
    /// so concurrent requests (repeated clicks, several tabs) start at most one poll.
    /// </summary>
    public async Task<UsageRefreshOutcome> RequestRefreshAsync(CancellationToken cancellationToken)
    {
        await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

            ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (account is null || !account.CanReportUsage)
            {
                logger.LogDebug("Usage refresh refused — account cannot report usage.");
                return UsageRefreshOutcome.Unavailable;
            }
        }

        if (loginSessionState.IsLoginActive)
        {
            logger.LogDebug("Usage refresh refused — login session is active.");
            return UsageRefreshOutcome.LoginActive;
        }

        lock (_gate)
        {
            if (_inFlightTask is not null)
            {
                logger.LogDebug("Usage refresh requested while a poll is in flight; joining it.");
                return UsageRefreshOutcome.InFlight;
            }

            if (IsWithinRefreshFloor(state.Current, DateTimeOffset.UtcNow))
            {
                logger.LogDebug("Usage refresh skipped — last successful snapshot is younger than the refresh floor.");
                return UsageRefreshOutcome.AlreadyCurrent;
            }

            // CancellationToken.None: the poll outlives the HTTP request that triggered it.
            _ = StartPollLocked(CancellationToken.None);
            return UsageRefreshOutcome.Started;
        }
    }

    private static bool IsWithinRefreshFloor(UsageSnapshot? current, DateTimeOffset now)
        => current is not null
            && current.LastRefreshFailedAt is null
            && current.CapturedAt is { } capturedAt
            && now - capturedAt < RefreshFloor;

    /// <summary>Starts a poll and tracks it as in flight. Caller must hold <see cref="_gate"/>.</summary>
    private Task<UsageSnapshot?> StartPollLocked(CancellationToken cancellationToken)
    {
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

        // Primary source: the /usage report (ADR 0097).
        Result<string> usageResult = await orchestrator.RunUsageReportAsync(
            new UsageReportSpec(UsageReportCommand.UsageCommand, UsageReportSpec.DefaultTimeoutSeconds),
            cancellationToken);

        if (usageResult is Result<string>.Failure failure)
        {
            logger.LogWarning(
                "Usage report orchestrator failed: {Error}. Retaining last good snapshot, if any.",
                failure.Error.Message);
            return HandleFailure(accountEmail, capturedAt);
        }

        string log = ((Result<string>.Success)usageResult).Value;
        UsageParseResult parseResult = UsageReportParser.Parse(log, accountEmail, capturedAt);

        return parseResult switch
        {
            UsageParseResult.Parsed parsed => HandleParsed(parsed.Snapshot),
            UsageParseResult.NoUsageReport => HandleNoUsageReport(),
            UsageParseResult.Unparseable => HandleUnparseable(log, capturedAt),
            UsageParseResult.RateLimitsMissing missing =>
                await RunProbeTurnFallbackAsync(missing.ExtraCredits, UsageReportParser.ReadClaudeCodeVersion(log)),
            _ => throw new System.Diagnostics.UnreachableException(
                $"Unexpected UsageParseResult: {parseResult.GetType().Name}"),
        };

        // Fallback: /usage print mode returned no rate_limits (a known CLI-version regression).
        // Runs the probe turn sequentially inside this same single-flight poll, never concurrently.
        async Task<UsageSnapshot?> RunProbeTurnFallbackAsync(UsageExtraCredits? extraCredits, string? cliVersion)
        {
            logger.LogWarning(
                "Usage report from Claude Code CLI {CliVersion} carried no rate limits; falling back to a probe turn for the session and weekly meters.",
                cliVersion ?? "(unknown version)");

            Result<string> probeResult = await orchestrator.RunUsageReportAsync(
                new UsageReportSpec(UsageReportCommand.ProbeTurn, UsageReportSpec.DefaultTimeoutSeconds),
                cancellationToken);

            if (probeResult is Result<string>.Failure probeFailure)
            {
                logger.LogWarning(
                    "Usage probe-turn orchestrator failed: {Error}. Retaining last good snapshot, if any.",
                    probeFailure.Error.Message);
                return HandleFailure(accountEmail, capturedAt);
            }

            string probeLog = ((Result<string>.Success)probeResult).Value;

            // /usage already showed the account is reportable, so a probe without windows is a failure, not a clear.
            return ProbeTurnUsageParser.Parse(probeLog, accountEmail, capturedAt) is UsageParseResult.Parsed probeParsed
                ? HandleParsed(probeParsed.Snapshot with { ExtraUsage = extraCredits })
                : HandleUnparseable(probeLog, capturedAt);
        }

        // Local function — avoids making the async method large while keeping the broadcast inline.
        UsageSnapshot? HandleParsed(UsageSnapshot snapshot)
        {
            stateMutator.SetSnapshot(snapshot);
            UsageSnapshot? current = state.Current;

            // Fire-and-forget broadcast — use CancellationToken.None so a cancelled caller
            // token does not suppress the push to connected clients.
            _ = TryBroadcastAsync(current, CancellationToken.None);

            logger.LogInformation(
                "Usage poll succeeded from {Source}. Snapshot captured at {CapturedAt}.",
                snapshot.Source,
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

        UsageSnapshot? HandleUnparseable(string output, DateTimeOffset failedAt)
        {
            logger.LogWarning(
                "Usage report output was unparseable. Retaining last good snapshot, if any. Output sample: {OutputSample}",
                BuildOutputSample(output));

            return HandleFailure(accountEmail, failedAt);
        }
    }

    /// <summary>
    /// Builds a short, redacted sample of the usage-report output for diagnostics. Redaction runs
    /// before truncation so a secret cut at the boundary is still masked.
    /// </summary>
    internal static string BuildOutputSample(string output)
    {
        string redacted = SecretRedactor.Redact(output).Trim();

        if (redacted.Length == 0)
        {
            return "<empty>";
        }

        return redacted.Length > UnparseableSampleMaxLength
            ? string.Concat(redacted.AsSpan(0, UnparseableSampleMaxLength), "…")
            : redacted;
    }

    private UsageSnapshot? HandleFailure(string accountEmail, DateTimeOffset failedAt)
    {
        stateMutator.MarkRefreshFailed(accountEmail, failedAt);
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
