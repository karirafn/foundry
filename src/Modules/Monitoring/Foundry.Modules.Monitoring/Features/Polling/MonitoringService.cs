using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.AccountTokenChecks;
using Foundry.Modules.Monitoring.Features.AwaitingInput;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Merge;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Rebase;
using Foundry.Modules.Monitoring.Features.SpecRewrite;
using Foundry.Modules.Monitoring.Features.TriageFailed;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Monitoring.Features.Webhooks.Ingress;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;
using Foundry.Modules.Monitoring.Features.Webhooks.Registration;
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
    /// Floor on scheduler-driven waits, so a nudge pass that keeps failing cannot spin the loop.
    /// A new nudge in the channel still wakes the loop immediately.
    /// </summary>
    private static readonly TimeSpan MinimumWait = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WebhookNudgeQueue? _nudgeQueue;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MonitoringService> _logger;
    private readonly NudgeScheduler _scheduler = new();

    // In-memory on purpose: any process start (clean restart or crash) re-arms the one-time catch-up,
    // because webhook deliveries that arrived while Foundry was down are lost (the same pattern as
    // WorkerDispatchService._reconciled).
    private bool _startupCatchUpPending = true;

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

    /// <summary>
    /// Whether the next scheduled tick is the one-time startup catch-up, which polls every active
    /// repository regardless of <see cref="MonitoredRepository.IsDueForPoll"/> and never records a
    /// missed change. Settable so tests can start from the steady state.
    /// </summary>
    internal bool StartupCatchUpPending
    {
        get => _startupCatchUpPending;
        set => _startupCatchUpPending = value;
    }

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

        if (delay == TimeSpan.Zero && wakeAt == nextHeartbeat)
        {
            return false;
        }

        if (delay < MinimumWait)
        {
            delay = MinimumWait;
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

        // Optional: hosts (and tests) without the rebase step poll exactly as before.
        RebaseStep? rebaseStep = scope.ServiceProvider.GetService<RebaseStep>();
        MergeStep? mergeStep = scope.ServiceProvider.GetService<MergeStep>();
        AwaitingInputStep? awaitingInputStep = scope.ServiceProvider.GetService<AwaitingInputStep>();
        TriageFailedEditStep? triageFailedEditStep = scope.ServiceProvider.GetService<TriageFailedEditStep>();
        SpecRewriteStep? specRewriteStep = scope.ServiceProvider.GetService<SpecRewriteStep>();

        int pollIntervalSeconds = await settingsQueries.GetPollIntervalSecondsAsync(cancellationToken);
        TimeSpan pollInterval = TimeSpan.FromSeconds(pollIntervalSeconds);

        // The backstop applies only while webhook ingress is configured (port + public base URL).
        WebhookIngressState? ingress = await GetIngressStateAsync(scope.ServiceProvider, cancellationToken);
        TimeSpan? backstopInterval = ingress is { IsConfigured: true } ? ingress.BackstopInterval : null;

        // Package sources first, so the polls below read fresh verdicts.
        await ProbePackageSourcesAsync(now, cancellationToken);

        List<MonitoredRepository> repos = await LoadActiveReposAsync(dbContext, cancellationToken);

        // The catch-up is consumed once the repositories are loaded: a per-repository failure returns
        // that repository to its normal schedule rather than retrying on every tick.
        bool catchUp = _startupCatchUpPending;
        _startupCatchUpPending = false;

        if (catchUp && repos.Count > 0)
        {
            _logger.LogInformation(
                "Startup catch-up: polling all {Count} active repositories once to recover changes missed while down.",
                repos.Count);
        }

        List<MonitoredRepositoryId> polledRepoIds = [];

        foreach (MonitoredRepository repo in repos)
        {
            if (!catchUp && !repo.IsDueForPoll(pollInterval, now, backstopInterval))
            {
                continue;
            }

            polledRepoIds.Add(repo.Id);

            ResolvedProvider? resolved = await ResolveProviderAsync(
                repo,
                scope.ServiceProvider,
                credentialResolver,
                providerFactory,
                now,
                cancellationToken);
            if (resolved is not { } resolvedProvider)
            {
                continue;
            }

            Result<PollChanges> pollResult = await poller.PollDetectingChangesAsync(
                repo,
                resolvedProvider.Provider,
                now,
                cancellationToken);

            // A listing refused with 401 is confirmed with a token check. Once the account is marked
            // Invalid, no further call is made with its token this cycle.
            if (pollResult is Result<PollChanges>.Failure { Error.Kind: ErrorKind.Unauthorized } unauthorized
                && await ConfirmTokenRejectedAsync(scope.ServiceProvider, poller, repo, resolvedProvider, now, cancellationToken))
            {
                _logger.LogWarning(
                    "Poll failed for repo '{Slug}': {Error}",
                    repo.Slug,
                    unauthorized.Error.Message);
                continue;
            }

            // The scheduled poll is the rebase step's backstop (ADR 0087): it resumes rebases whose nudges
            // were lost, e.g. across a host restart.
            if (rebaseStep is not null)
            {
                await rebaseStep.RunAsync(repo, resolvedProvider.Provider, issueNumber: null, now, cancellationToken);
            }

            // The scheduled poll is also the merge step's backstop (ADR 0105): it runs merges whose nudges were lost.
            if (mergeStep is not null)
            {
                await mergeStep.RunAsync(repo, resolvedProvider.Provider, issueNumber: null, now, cancellationToken);
            }

            // The scheduled poll is also the awaiting-input step's backstop (ADR 0096): it posts questions whose
            // nudge was lost and reads replies the webhooks do not announce.
            if (awaitingInputStep is not null)
            {
                await awaitingInputStep.RunAsync(repo, resolvedProvider.Provider, issueNumber: null, now, cancellationToken);
            }

            // The scheduled poll is also the failed-triage edit step's backstop (issue #773): it reads the edits whose
            // TriageFailedEdit nudge was lost and settles the ones whose quiet period ended.
            if (triageFailedEditStep is not null)
            {
                await triageFailedEditStep.RunAsync(repo, resolvedProvider.Provider, issueNumber: null, now, cancellationToken);
            }

            // The scheduled poll is also the spec-rewrite step's backstop (ADR 0125): it reads the
            // rewrites whose SpecRewrite nudge was lost and settles the ones whose quiet period ended.
            if (specRewriteStep is not null)
            {
                await specRewriteStep.RunAsync(repo, resolvedProvider.Provider, issueNumber: null, now, cancellationToken);
            }

            if (pollResult is Result<PollChanges>.Failure pollFailure)
            {
                _logger.LogWarning(
                    "Poll failed for repo '{Slug}': {Error}",
                    repo.Slug,
                    pollFailure.Error.Message);
                continue;
            }

            // Missed change: a scheduled poll of an Active hook found a change the provider has a webhook
            // event for, yet no delivery announced — fall back to the normal interval until the next
            // verified delivery. Changes the provider has no event for (GitLab issue links and protected
            // branches) never count. A category with a nudge still waiting for this repository was announced
            // by a delivery, so it does not count either. The startup catch-up is exempt: a change it finds was lost to
            // downtime, not to a silent hook.
            if (!catchUp
                && backstopInterval is not null
                && pollResult is Result<PollChanges>.Success pollSuccess
                && IsMissedChange(pollSuccess.Value & ~_scheduler.PendingCategories(repo.Id), resolvedProvider.Credential)
                && repo.RecordMissedChange())
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogWarning(
                    "Webhook for repository '{Slug}' missed a change; polling at the normal interval until the next delivery.",
                    repo.Slug);

                await ReRegisterSilentWebhookAsync(scope.ServiceProvider, dbContext, repo, resolvedProvider.Credential, cancellationToken);
            }
        }

        await RegisterStaleWebhooksAsync(polledRepoIds, now, cancellationToken);
    }

    /// <summary>
    /// Runs every nudged pass that is due at <paramref name="now"/>. Each key is rate-capped at
    /// <see cref="NudgeScheduler.RateFloor"/>, independent of the repository's poll interval.
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
        RebaseStep? rebaseStep = scope.ServiceProvider.GetService<RebaseStep>();
        MergeStep? mergeStep = scope.ServiceProvider.GetService<MergeStep>();
        AwaitingInputStep? awaitingInputStep = scope.ServiceProvider.GetService<AwaitingInputStep>();
        TriageFailedEditStep? triageFailedEditStep = scope.ServiceProvider.GetService<TriageFailedEditStep>();
        SpecRewriteStep? specRewriteStep = scope.ServiceProvider.GetService<SpecRewriteStep>();

        Dictionary<MonitoredRepositoryId, MonitoredRepository> repos =
            (await LoadActiveReposAsync(dbContext, cancellationToken)).ToDictionary(r => r.Id);

        IReadOnlyList<NudgeKey> due = _scheduler.TakeDue(now, repos.ContainsKey);

        foreach (IGrouping<MonitoredRepositoryId, NudgeKey> group in due.GroupBy(k => k.RepositoryId))
        {
            MonitoredRepository repo = repos[group.Key];

            ResolvedProvider? resolved = await ResolveProviderAsync(
                repo,
                scope.ServiceProvider,
                credentialResolver,
                providerFactory,
                now,
                cancellationToken);
            if (resolved is not { } resolvedProvider)
            {
                continue;
            }

            foreach (NudgeKey key in group)
            {
                bool tokenRejected = await RunNudgedPassAsync(
                    scope.ServiceProvider,
                    poller,
                    rebaseStep,
                    mergeStep,
                    awaitingInputStep,
                    triageFailedEditStep,
                    specRewriteStep,
                    repo,
                    resolvedProvider,
                    key,
                    now,
                    cancellationToken);

                // The account is now Invalid: its token is not used for the remaining passes.
                if (tokenRejected)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Runs one nudged pass. Returns <see langword="true"/> when its listing was refused with 401 and the
    /// token check confirmed it, so the account is now Invalid and its token must not be used again.
    /// </summary>
    private async Task<bool> RunNudgedPassAsync(
        IServiceProvider services,
        RepositoryPoller poller,
        RebaseStep? rebaseStep,
        MergeStep? mergeStep,
        AwaitingInputStep? awaitingInputStep,
        TriageFailedEditStep? triageFailedEditStep,
        SpecRewriteStep? specRewriteStep,
        MonitoredRepository repo,
        ResolvedProvider resolved,
        NudgeKey key,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IIssueProvider provider = resolved.Provider;

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

                if (listing is Result<bool>.Failure { Error.Kind: ErrorKind.Unauthorized }
                    && await ConfirmTokenRejectedAsync(services, poller, repo, resolved, now, cancellationToken))
                {
                    return true;
                }

                break;
            }

            case NudgePass.IssueListingAndDependencies:
            {
                Result<bool> listing = await poller.RunListingPassAsync(repo, provider, now, cancellationToken);
                LogListingFailure(repo, listing);

                if (listing is Result<bool>.Failure { Error.Kind: ErrorKind.Unauthorized }
                    && await ConfirmTokenRejectedAsync(services, poller, repo, resolved, now, cancellationToken))
                {
                    return true;
                }

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

            case NudgePass.Rebase:
                if (rebaseStep is not null)
                {
                    await rebaseStep.RunAsync(repo, provider, key.IssueNumber, now, cancellationToken);
                }

                break;

            case NudgePass.Merge:
                if (mergeStep is not null)
                {
                    await mergeStep.RunAsync(repo, provider, key.IssueNumber, now, cancellationToken);
                }

                break;

            case NudgePass.AwaitingInput:
                if (awaitingInputStep is not null)
                {
                    await awaitingInputStep.RunAsync(repo, provider, key.IssueNumber, now, cancellationToken);
                }

                break;

            case NudgePass.TriageFailedEdit:
                if (triageFailedEditStep is not null)
                {
                    await triageFailedEditStep.RunAsync(repo, provider, key.IssueNumber, now, cancellationToken);
                }

                break;

            case NudgePass.SpecRewrite:
                if (specRewriteStep is not null)
                {
                    await specRewriteStep.RunAsync(repo, provider, key.IssueNumber, now, cancellationToken);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(key), key.Pass, "Unhandled nudge pass.");
        }

        return false;
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

    /// <summary>
    /// Runs one create-or-update of a hook that just went <c>Silent</c> — a drifted or deleted hook is
    /// repaired with the account token. A hook still <c>Silent</c> afterwards is not retried until its
    /// next transition into <c>Silent</c>, because this runs only on that transition.
    /// </summary>
    private async Task ReRegisterSilentWebhookAsync(
        IServiceProvider services,
        DbContext dbContext,
        MonitoredRepository repo,
        Credential credential,
        CancellationToken cancellationToken)
    {
        // Optional: hosts (and tests) without webhook wiring skip re-registration.
        WebhookRegistrar? registrar = services.GetService<WebhookRegistrar>();
        if (registrar is null)
        {
            return;
        }

        WebhookRegistrationOutcome outcome = await registrar.RegisterAsync(repo, credential, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Re-registering the silent webhook for repository '{Slug}': {Kind} ({Reason}).",
            repo.Slug,
            outcome.Kind,
            outcome.Reason);
    }

    /// <summary>
    /// The webhook registration step of the scheduled poll (ADR 0116): registers each polled repository
    /// whose <see cref="MonitoredRepository.WebhookSpecStamp"/> is not the current
    /// <see cref="WebhookSpecStamp"/> (<see cref="MonitoredRepository.IsDueForWebhookRegistration"/>).
    /// Each repository runs in its own scope, outside <see cref="RepositoryPoller.PollDetectingChangesAsync"/>,
    /// so a current stamp costs no provider call — a stale one costs the registration plus its test delivery —
    /// and one repository's failure never reaches the others.
    /// </summary>
    private async Task RegisterStaleWebhooksAsync(
        IReadOnlyList<MonitoredRepositoryId> repoIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (MonitoredRepositoryId repoId in repoIds)
        {
            try
            {
                await RegisterIfStaleAsync(repoId, now, cancellationToken);
            }
#pragma warning disable CA1031 // One repository's registration must never stop the tick for the rest; the error log surfaces it.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Registering the stale webhook for repository '{RepositoryId}' threw.", repoId);
            }
        }
    }

    private async Task RegisterIfStaleAsync(
        MonitoredRepositoryId repoId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

        // Optional: hosts (and tests) without webhook wiring skip the step.
        WebhookRegistrar? registrar = scope.ServiceProvider.GetService<WebhookRegistrar>();
        WebhookIngressState? ingress = await GetIngressStateAsync(scope.ServiceProvider, cancellationToken);
        if (registrar is null || ingress is not { IsConfigured: true, PublicBaseUrl: { } publicBaseUrl })
        {
            return;
        }

        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        MonitoredRepository? repo = await dbContext.Set<MonitoredRepository>()
            .SingleOrDefaultAsync(r => r.Id == repoId && r.IsActive, cancellationToken);
        if (repo is null)
        {
            return;
        }

        ICredentialResolver credentialResolver = scope.ServiceProvider.GetRequiredService<ICredentialResolver>();
        Credential? credential = await credentialResolver.ResolveAsync(repo.Host, repo.Slug, cancellationToken);
        if (credential is null
            || WebhookProviders.From(credential) is not { } provider
            || !repo.IsDueForWebhookRegistration(WebhookSpecStamp.For(provider, publicBaseUrl), now))
        {
            return;
        }

        WebhookRegistrationOutcome outcome = await registrar.RegisterAsync(repo, credential, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Registering the stale webhook for repository '{Slug}': {Kind} ({Reason}).",
            repo.Slug,
            outcome.Kind,
            outcome.Reason);
    }

    private static async Task<WebhookIngressState?> GetIngressStateAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        // Optional: hosts (and tests) without webhook wiring poll exactly as before.
        WebhookIngress? ingress = services.GetService<WebhookIngress>();
        return ingress is null ? null : await ingress.GetStateAsync(cancellationToken);
    }

    /// <summary>
    /// Probes the Package Sources active repositories declare, once per source and credential, before the polls read
    /// the verdicts. Runs in its own scope so a failed save never reaches the polls, and outside
    /// <see cref="RepositoryPoller.PollAsync"/>, so probes never count toward its per-cycle call budget.
    /// </summary>
    private async Task ProbePackageSourcesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

        // Optional: hosts (and tests) without package source probing poll exactly as before.
        PackageSourceProbing? probing = scope.ServiceProvider.GetService<PackageSourceProbing>();
        if (probing is null)
        {
            return;
        }

        try
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            List<MonitoredRepository> repos = await LoadActiveReposAsync(dbContext, cancellationToken);
            await probing.ProbeDueAsync(repos, now, cancellationToken);
        }
#pragma warning disable CA1031 // A failed probe pass must not stop the polls; the error log surfaces it.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Probing package sources failed; their stored verdicts are left as they were.");
        }
    }

    private static async Task<List<MonitoredRepository>> LoadActiveReposAsync(
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<MonitoredRepository>()
            .Where(r => r.IsActive)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Whether <paramref name="changes"/> include a category the repository's provider has a webhook
    /// event for (<see cref="WebhookProviders.MissedChangeCategories"/>).
    /// </summary>
    internal static bool IsMissedChange(PollChanges changes, Credential credential) =>
        WebhookProviders.From(credential) is { } provider
        && (changes & WebhookProviders.MissedChangeCategories(provider)) != PollChanges.None;

    private async Task<ResolvedProvider?> ResolveProviderAsync(
        MonitoredRepository repo,
        IServiceProvider services,
        ICredentialResolver credentialResolver,
        IIssueProviderFactory providerFactory,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Credential? credential = await credentialResolver.ResolveAsync(
            repo.Host,
            repo.Slug,
            cancellationToken);

        if (credential is null)
        {
            if (RepositoryEligibilityEvaluator.StoreNoCredential(repo))
            {
                await services.GetRequiredService<DbContext>().SaveChangesAsync(cancellationToken);
            }

            _logger.LogDebug(
                "No credential covers repository '{Slug}'; recorded it as ineligible and skipping poll.",
                repo.Slug);
            return null;
        }

        TokenUsability usability = credential.TokenUsability;
        switch (usability)
        {
            case TokenUsability.Usable usable:
                return new ResolvedProvider(
                    providerFactory.CreateProvider(credential, usable.Token),
                    credential,
                    usable.Token);
            case TokenUsability.Invalid invalid:
                return await RecheckInvalidTokenAsync(repo, services, providerFactory, credential, invalid.Token, now, cancellationToken);
            case TokenUsability.Unreadable:
                _logger.LogWarning(
                    "Credential '{CredentialName}' (id '{CredentialId}') has an unreadable token; skipping poll for repo '{Slug}'.",
                    credential.Name.Value,
                    credential.Id,
                    repo.Slug);
                return null;
            case TokenUsability.Absent:
                _logger.LogDebug(
                    "Credential '{CredentialName}' has no token configured; skipping poll for repo '{Slug}'.",
                    credential.Name.Value,
                    repo.Slug);
                return null;
            default:
                throw new UnreachableException($"Unhandled TokenUsability variant: {usability.GetType().Name}");
        }
    }

    /// <summary>
    /// The automatic exit from an Invalid account token: once per <see cref="Credential.TokenRecheckCooldown"/>
    /// the stored token is checked again. The poll runs only when the recheck saved the account as Valid.
    /// </summary>
    private async Task<ResolvedProvider?> RecheckInvalidTokenAsync(
        MonitoredRepository repo,
        IServiceProvider services,
        IIssueProviderFactory providerFactory,
        Credential credential,
        string token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!credential.IsDueForTokenRecheck(now))
        {
            _logger.LogDebug(
                "Credential '{CredentialName}' (id '{CredentialId}') has an invalid token; skipping poll for repo '{Slug}'.",
                credential.Name.Value,
                credential.Id,
                repo.Slug);
            return null;
        }

        AccountTokenVerdicts verdicts = services.GetRequiredService<AccountTokenVerdicts>();
        if (!await verdicts.RecheckAsync(credential, token, now, cancellationToken))
        {
            return null;
        }

        return new ResolvedProvider(providerFactory.CreateProvider(credential, token), credential, token);
    }

    /// <summary>
    /// Confirms a listing refused with 401 with a token check. When the account is marked Invalid, the
    /// repository's eligibility is evaluated again at once, so it shows the invalid-token violation this
    /// cycle; that evaluation makes no provider call for an Invalid account. Returns whether it was marked.
    /// </summary>
    private static async Task<bool> ConfirmTokenRejectedAsync(
        IServiceProvider services,
        RepositoryPoller poller,
        MonitoredRepository repo,
        ResolvedProvider resolved,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AccountTokenVerdicts verdicts = services.GetRequiredService<AccountTokenVerdicts>();
        if (!await verdicts.ConfirmRejectionAsync(resolved.Credential, resolved.Token, now, cancellationToken))
        {
            return false;
        }

        await poller.RunBranchRulesStepAsync(repo, now, cancellationToken);
        return true;
    }

    /// <summary>A provider for one repository, with the account and the token it was created with.</summary>
    private readonly record struct ResolvedProvider(IIssueProvider Provider, Credential Credential, string Token);
}
