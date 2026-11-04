using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Polling;

/// <summary>
/// The single poll loop (ADR 0005). Runs scheduled polls on a fixed heartbeat and is also the only
/// consumer of the webhook nudge channel (ADR 0085): a nudge wakes the loop immediately, and nudged
/// passes run in the same loop as scheduled polls, so a repository is never polled concurrently.
/// </summary>
internal sealed class MonitoringService : PeriodicBackgroundService
{
    // Fixed scheduler heartbeat/resolution — the effective per-repository poll cadence is the
    // operator-configured value read each tick via IGlobalSettingsQueries.GetPollIntervalSecondsAsync
    // and applied through repo.IsDueForPoll(...). The 30 s heartbeat is intentional by design.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Delay before the dependency pass that follows a nudged listing which found changes, so the
    /// outbox relay has created newly detected issues before their blockers are read.
    /// </summary>
    internal static readonly TimeSpan DependencyFollowUpDelay = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WebhookNudgeQueue? _nudgeQueue;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MonitoringService> _logger;
    private readonly NudgeScheduler _scheduler = new();

    public MonitoringService(
        IServiceScopeFactory scopeFactory,
        ILogger<MonitoringService> logger)
        : this(scopeFactory, null, TimeProvider.System, logger)
    {
    }

    public MonitoringService(
        IServiceScopeFactory scopeFactory,
        WebhookNudgeQueue? nudgeQueue,
        TimeProvider timeProvider,
        ILogger<MonitoringService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
        _nudgeQueue = nudgeQueue;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override TimeSpan TickInterval => Interval;

    internal NudgeScheduler Scheduler => _scheduler;

    protected override Task TickAsync(CancellationToken cancellationToken)
        => ExecuteTickAsync(_timeProvider.GetUtcNow(), cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_nudgeQueue is null)
        {
            await base.ExecuteAsync(stoppingToken);
            return;
        }

        DateTimeOffset nextHeartbeat = _timeProvider.GetUtcNow() + Interval;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                bool nudged = await WaitForWorkAsync(nextHeartbeat, stoppingToken);

                if (nudged)
                {
                    // Coalescing window: let a burst of deliveries for one key collapse into one run.
                    await Task.Delay(NudgeScheduler.CoalescingWindow, _timeProvider, stoppingToken);
                }

                DrainNudges(_timeProvider.GetUtcNow());

                DateTimeOffset now = _timeProvider.GetUtcNow();
                if (now >= nextHeartbeat)
                {
                    await RunGuardedAsync(ct => ExecuteTickAsync(now, ct), "scheduled poll", stoppingToken);
                    nextHeartbeat = now + Interval;
                }

                await RunGuardedAsync(
                    ct => ExecuteNudgesAsync(_timeProvider.GetUtcNow(), ct),
                    "nudged passes",
                    stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Clean shutdown.
        }
    }

    /// <summary>
    /// Waits until the next heartbeat, the next scheduled nudge, or a new nudge in the channel —
    /// whichever comes first. Returns <see langword="true"/> when woken by a new nudge.
    /// </summary>
    private async Task<bool> WaitForWorkAsync(DateTimeOffset nextHeartbeat, CancellationToken stoppingToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset wakeAt = _scheduler.NextWakeAt is { } nudgeWake && nudgeWake < nextHeartbeat
            ? nudgeWake
            : nextHeartbeat;
        TimeSpan delay = wakeAt > now ? wakeAt - now : TimeSpan.Zero;

        if (delay == TimeSpan.Zero)
        {
            return false;
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(delay);

        try
        {
            return await _nudgeQueue!.Reader.WaitToReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void DrainNudges(DateTimeOffset now)
    {
        while (_nudgeQueue!.Reader.TryRead(out Nudge nudge))
        {
            _scheduler.Add(nudge, now);
        }
    }

    private async Task RunGuardedAsync(
        Func<CancellationToken, Task> work,
        string description,
        CancellationToken stoppingToken)
    {
        try
        {
            await work(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A failed pass must not crash the poll loop or the host; the error log surfaces it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Unhandled exception in MonitoringService {Work}.", description);
        }
    }

    internal async Task ExecuteTickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        IIssueProviderFactory providerFactory = scope.ServiceProvider.GetRequiredService<IIssueProviderFactory>();
        ICredentialResolver credentialResolver = scope.ServiceProvider.GetRequiredService<ICredentialResolver>();
        RepositoryPoller poller = scope.ServiceProvider.GetRequiredService<RepositoryPoller>();
        IGlobalSettingsQueries settingsQueries = scope.ServiceProvider.GetRequiredService<IGlobalSettingsQueries>();

        int pollIntervalSeconds = await settingsQueries.GetPollIntervalSecondsAsync(cancellationToken);
        TimeSpan pollInterval = TimeSpan.FromSeconds(pollIntervalSeconds);

        // The backstop applies only while webhook ingress is configured (port + public base URL).
        WebhookIngressState? ingress = await GetIngressStateAsync(scope.ServiceProvider, cancellationToken);
        TimeSpan? backstopInterval = ingress is { IsConfigured: true } ? ingress.BackstopInterval : null;

        List<MonitoredRepository> repos = await LoadActiveReposAsync(dbContext, cancellationToken);

        foreach (MonitoredRepository repo in repos)
        {
            if (!repo.IsDueForPoll(pollInterval, now, backstopInterval))
            {
                continue;
            }

            IIssueProvider? provider = await ResolveProviderAsync(repo, credentialResolver, providerFactory, cancellationToken);
            if (provider is null)
            {
                continue;
            }

            Result<bool> pollResult = await poller.PollDetectingChangesAsync(repo, provider, now, cancellationToken);

            if (pollResult is Result<bool>.Failure pollFailure)
            {
                _logger.LogWarning(
                    "Poll failed for repo '{Slug}': {Error}",
                    repo.Slug,
                    pollFailure.Error.Message);
                continue;
            }

            // Missed change: a scheduled poll of an Active hook found a webhook-mapped change that no
            // delivery announced — fall back to the normal interval until the next verified delivery.
            if (backstopInterval is not null
                && pollResult is Result<bool>.Success { Value: true }
                && repo.RecordMissedChange())
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogWarning(
                    "Webhook for repository '{Slug}' missed a change; polling at the normal interval until the next delivery.",
                    repo.Slug);
            }
        }
    }

    /// <summary>
    /// Runs every nudged pass that is due at <paramref name="now"/>. Each key is rate-capped at the
    /// repository's normal effective poll interval, so webhook mode never polls a pass more often than
    /// normal polling would.
    /// </summary>
    internal async Task ExecuteNudgesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_scheduler.PendingCount == 0)
        {
            return;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        IIssueProviderFactory providerFactory = scope.ServiceProvider.GetRequiredService<IIssueProviderFactory>();
        ICredentialResolver credentialResolver = scope.ServiceProvider.GetRequiredService<ICredentialResolver>();
        RepositoryPoller poller = scope.ServiceProvider.GetRequiredService<RepositoryPoller>();
        IGlobalSettingsQueries settingsQueries = scope.ServiceProvider.GetRequiredService<IGlobalSettingsQueries>();

        TimeSpan pollInterval = TimeSpan.FromSeconds(await settingsQueries.GetPollIntervalSecondsAsync(cancellationToken));

        Dictionary<MonitoredRepositoryId, MonitoredRepository> repos =
            (await LoadActiveReposAsync(dbContext, cancellationToken)).ToDictionary(r => r.Id);

        IReadOnlyList<NudgeKey> due = _scheduler.TakeDue(
            now,
            id => repos.TryGetValue(id, out MonitoredRepository? repo) ? repo.EffectivePollInterval(pollInterval) : null);

        foreach (IGrouping<MonitoredRepositoryId, NudgeKey> group in due.GroupBy(k => k.RepositoryId))
        {
            MonitoredRepository repo = repos[group.Key];

            IIssueProvider? provider = await ResolveProviderAsync(repo, credentialResolver, providerFactory, cancellationToken);
            if (provider is null)
            {
                continue;
            }

            foreach (NudgeKey key in group)
            {
                await RunNudgedPassAsync(poller, repo, provider, key, now, cancellationToken);
            }
        }
    }

    private async Task RunNudgedPassAsync(
        RepositoryPoller poller,
        MonitoredRepository repo,
        IIssueProvider provider,
        NudgeKey key,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Running nudged {Pass} pass for repository '{Slug}' (issue {IssueNumber}).",
            key.Pass,
            repo.Slug,
            key.IssueNumber);

        switch (key.Pass)
        {
            case NudgePass.IssueListing:
            {
                Result<bool> listing = await poller.RunListingPassAsync(repo, provider, now, cancellationToken);
                LogListingFailure(repo, listing);

                if (listing is Result<bool>.Success { Value: true })
                {
                    // Newly detected issues need their blockers read before they can be dispatched.
                    _scheduler.Add(
                        new Nudge(new NudgeKey(repo.Id, NudgePass.Dependencies), now + DependencyFollowUpDelay),
                        now);
                }

                break;
            }

            case NudgePass.IssueListingAndDependencies:
            {
                Result<bool> listing = await poller.RunListingPassAsync(repo, provider, now, cancellationToken);
                LogListingFailure(repo, listing);

                if (listing is Result<bool>.Success)
                {
                    await poller.RunDependencyPassAsync(repo, provider, cancellationToken);
                }

                break;
            }

            case NudgePass.Dependencies:
                await poller.RunDependencyPassAsync(repo, provider, cancellationToken);
                break;

            case NudgePass.Review:
                await poller.RunReviewPassAsync(repo, provider, key.IssueNumber, cancellationToken);
                break;

            case NudgePass.Checks:
                await poller.RunChecksPassAsync(repo, provider, key.IssueNumber, now, cancellationToken);
                break;

            case NudgePass.BranchRules:
                await poller.RunBranchRulesStepAsync(repo, now, cancellationToken);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(key), key.Pass, "Unhandled nudge pass.");
        }
    }

    private void LogListingFailure(MonitoredRepository repo, Result<bool> listing)
    {
        if (listing is Result<bool>.Failure failure)
        {
            _logger.LogWarning(
                "Nudged listing pass failed for repo '{Slug}': {Error}",
                repo.Slug,
                failure.Error.Message);
        }
    }

    private static async Task<WebhookIngressState?> GetIngressStateAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        // Optional: hosts (and tests) without webhook wiring poll exactly as before.
        WebhookIngress? ingress = services.GetService<WebhookIngress>();
        return ingress is null ? null : await ingress.GetStateAsync(cancellationToken);
    }

    private static async Task<List<MonitoredRepository>> LoadActiveReposAsync(
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<MonitoredRepository>()
            .Where(r => r.IsActive)
            .ToListAsync(cancellationToken);
    }

    private async Task<IIssueProvider?> ResolveProviderAsync(
        MonitoredRepository repo,
        ICredentialResolver credentialResolver,
        IIssueProviderFactory providerFactory,
        CancellationToken cancellationToken)
    {
        Credential? credential = await credentialResolver.ResolveAsync(
            repo.Host,
            repo.Slug,
            cancellationToken);

        if (credential is null)
        {
            _logger.LogDebug(
                "No credential covers repository '{Slug}'; skipping poll.",
                repo.Slug);
            return null;
        }

        if (credential.Token is ProviderToken.Unreadable)
        {
            _logger.LogWarning(
                "Credential '{CredentialName}' (id '{CredentialId}') has an unreadable token; skipping poll for repo '{Slug}'.",
                credential.Name,
                credential.Id,
                repo.Slug);
            return null;
        }

        if (credential.Token is not ProviderToken.Present present)
        {
            _logger.LogDebug(
                "Credential '{CredentialName}' has no token configured; skipping poll for repo '{Slug}'.",
                credential.Name,
                repo.Slug);
            return null;
        }

        return providerFactory.CreateProvider(credential, present.Value);
    }
}
