using System.Collections.Frozen;
using System.Text.Json.Serialization;

using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

[JsonConverter(typeof(FailureCategoryJsonConverter))]
public sealed record FailureCategory
{
    public const string NonZeroExitToken = "non_zero_exit";
    public const string TimedOutToken = "timed_out";
    public const string ContainerErrorToken = "container_error";
    public const string UsageLimitedToken = "usage_limited";
    public const string WorkerBootstrapFailedToken = "worker_bootstrap_failed";
    public const string AuthInvalidToken = "auth_invalid";
    public const string ProviderErrorToken = "provider_error";
    public const string ProviderUnreachableToken = "provider_unreachable";
    public const string FetchFailedToken = "fetch_failed";
    public const string AccessRefusedToken = "access_refused";
    public const string PackageSourceUnreachableToken = "package_source_unreachable";
    public const string PackageRestoreFailedToken = "package_restore_failed";
    public const string TransientApiErrorToken = "transient_api_error";
    public const string CreditsExhaustedToken = "credits_exhausted";
    public const string ModelUnavailableToken = "model_unavailable";
    public const string PrClosedToken = "pr_closed";
    public const string CancelledToken = "cancelled";
    public const string TurnLimitToken = "turn_limit";
    public const string VerdictInvalidToken = "verdict_invalid";
    public const string PublishFailedToken = "publish_failed";
    public const string PublishExpiredToken = "publish_expired";
    public const string RunDiscardedToken = "run_discarded";
    public const string BlockerCycleToken = "blocker_cycle";
    public const string BlockerLinkFailedToken = "blocker_link_failed";
    public const string IssueClosedToken = "issue_closed";
    public const string RepositoryRemovedToken = "repository_removed";

    public static readonly FailureCategory NonZeroExit = new(NonZeroExitToken);
    public static readonly FailureCategory TimedOut = new(TimedOutToken);
    public static readonly FailureCategory ContainerError = new(ContainerErrorToken);
    public static readonly FailureCategory UsageLimited = new(UsageLimitedToken);
    public static readonly FailureCategory WorkerBootstrapFailed = new(WorkerBootstrapFailedToken);
    public static readonly FailureCategory AuthInvalid = new(AuthInvalidToken);
    public static readonly FailureCategory ProviderError = new(ProviderErrorToken);

    /// <summary>The Publisher could not reach the git provider (DNS, connection, HTTP 429 or 5xx) after every retry.</summary>
    /// <remarks>Its schedule stretches over about an hour, long enough to outlast a typical provider outage.</remarks>
    public static readonly FailureCategory ProviderUnreachable = new(
        ProviderUnreachableToken,
        AutomaticRetrySchedule.Create(
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(16),
            TimeSpan.FromMinutes(32)));

    /// <summary>
    /// The Publisher reached the git provider, but its ls-remote or fetch kept failing for a reason it reads as transient
    /// (a truncated pack, the remote hanging up) through every retry.
    /// </summary>
    /// <remarks>
    /// It shares the provider outage's schedule, but pauses no host: ls-remote has just reached it.
    /// </remarks>
    public static readonly FailureCategory FetchFailed = new(
        FetchFailedToken,
        AutomaticRetrySchedule.Create(
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(16),
            TimeSpan.FromMinutes(32)));

    /// <summary>
    /// The git provider refused the account token access to the repository during Prepare (HTTP 403, or a 404 hiding
    /// the repository), so the run did no work.
    /// </summary>
    /// <remarks>
    /// It shares the fetch failure's schedule: the refusal has the repository's write access probed again, so a token
    /// the operator fixes, or a refusal the probe overturns, lets a later retry pass without anyone pressing Retry.
    /// </remarks>
    public static readonly FailureCategory AccessRefused = new(
        AccessRefusedToken,
        AutomaticRetrySchedule.Create(
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(16),
            TimeSpan.FromMinutes(32)));

    /// <summary>The worker could not reach the Package Source Proxy before its agent started, so it did no work.</summary>
    /// <remarks>It shares the provider outage's schedule: both are infrastructure outages a person need not act on.</remarks>
    public static readonly FailureCategory PackageSourceUnreachable = new(
        PackageSourceUnreachableToken,
        AutomaticRetrySchedule.Create(
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(16),
            TimeSpan.FromMinutes(32)));

    /// <summary>
    /// The worker's pre-restore failed on a run whose repository declares Package Sources, before its agent started.
    /// </summary>
    /// <remarks>
    /// It shares the package proxy outage's schedule: Foundry owns that restore path, so the cause is a proxy or a
    /// credential a person need not act on for the run itself.
    /// </remarks>
    public static readonly FailureCategory PackageRestoreFailed = new(
        PackageRestoreFailedToken,
        AutomaticRetrySchedule.Create(
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(16),
            TimeSpan.FromMinutes(32)));

    public static readonly FailureCategory TransientApiError = new(
        TransientApiErrorToken,
        AutomaticRetrySchedule.Create(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)));
    public static readonly FailureCategory CreditsExhausted = new(CreditsExhaustedToken);
    public static readonly FailureCategory ModelUnavailable = new(ModelUnavailableToken);
    public static readonly FailureCategory PrClosed = new(PrClosedToken);
    public static readonly FailureCategory Cancelled = new(CancelledToken);
    public static readonly FailureCategory TurnLimit = new(TurnLimitToken);
    public static readonly FailureCategory VerdictInvalid = new(VerdictInvalidToken);

    /// <summary>Publishing the run's commits failed after every retry (ADR 0100); Retry publish reruns it.</summary>
    public static readonly FailureCategory PublishFailed = new(PublishFailedToken);

    /// <summary>The run's unpublished work was discarded when its Hand-off volume reached its time to live.</summary>
    public static readonly FailureCategory PublishExpired = new(PublishExpiredToken);

    /// <summary>An operator discarded a run holding unpublished work (#788); Retry Issue starts a new run.</summary>
    public static readonly FailureCategory RunDiscarded = new(RunDiscardedToken);

    /// <summary>Triage's blocker would close a dependency cycle through the issue, so nothing was linked.</summary>
    public static readonly FailureCategory BlockerCycle = new(BlockerCycleToken);

    /// <summary>The provider refused to link a blocker triage named.</summary>
    public static readonly FailureCategory BlockerLinkFailed = new(BlockerLinkFailedToken);

    /// <summary>The issue closed on the provider before the run's unpublished work was published (#909), so the run was discarded.</summary>
    public static readonly FailureCategory IssueClosed = new(IssueClosedToken);

    /// <summary>The run's repository was removed from Foundry before its unpublished work was published, so the run was discarded.</summary>
    public static readonly FailureCategory RepositoryRemoved = new(RepositoryRemovedToken);

    private static readonly FrozenDictionary<string, FailureCategory> KnownCategories =
        new Dictionary<string, FailureCategory>(StringComparer.Ordinal)
        {
            { NonZeroExitToken, NonZeroExit },
            { TimedOutToken, TimedOut },
            { ContainerErrorToken, ContainerError },
            { UsageLimitedToken, UsageLimited },
            { WorkerBootstrapFailedToken, WorkerBootstrapFailed },
            { AuthInvalidToken, AuthInvalid },
            { ProviderErrorToken, ProviderError },
            { ProviderUnreachableToken, ProviderUnreachable },
            { FetchFailedToken, FetchFailed },
            { AccessRefusedToken, AccessRefused },
            { PackageSourceUnreachableToken, PackageSourceUnreachable },
            { PackageRestoreFailedToken, PackageRestoreFailed },
            { TransientApiErrorToken, TransientApiError },
            { CreditsExhaustedToken, CreditsExhausted },
            { ModelUnavailableToken, ModelUnavailable },
            { PrClosedToken, PrClosed },
            { CancelledToken, Cancelled },
            { TurnLimitToken, TurnLimit },
            { VerdictInvalidToken, VerdictInvalid },
            { PublishFailedToken, PublishFailed },
            { PublishExpiredToken, PublishExpired },
            { RunDiscardedToken, RunDiscarded },
            { BlockerCycleToken, BlockerCycle },
            { BlockerLinkFailedToken, BlockerLinkFailed },
            { IssueClosedToken, IssueClosed },
            { RepositoryRemovedToken, RepositoryRemoved },
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Every category that retries automatically: exactly those with an <see cref="AutomaticRetry"/> schedule.</summary>
    public static readonly IReadOnlyCollection<FailureCategory> AutoRetryable =
        [.. KnownCategories.Values.Where(category => category.AutomaticRetry is not null)];

    /// <summary>The soonest any auto-retryable failure can become due for its first automatic retry.</summary>
    public static readonly TimeSpan ShortestFirstAutomaticRetryBackoff =
        AutoRetryable.Min(category => category.AutomaticRetry!.BackoffAfter(1));

    public string Value { get; }

    /// <summary>
    /// How this category retries automatically, or <see langword="null"/> when it never does and waits for an operator.
    /// </summary>
    public AutomaticRetrySchedule? AutomaticRetry { get; }

    private FailureCategory(string value, AutomaticRetrySchedule? automaticRetry = null)
    {
        Value = value;
        AutomaticRetry = automaticRetry;
    }

    /// <summary>Whether <paramref name="token"/> names a category that retries automatically.</summary>
    public static bool IsAutoRetryableToken(string? token) =>
        token is not null
        && KnownCategories.TryGetValue(token, out FailureCategory? category)
        && category.AutomaticRetry is not null;

    public static Result<FailureCategory> Create(string token) =>
        KnownCategories.TryGetValue(token, out FailureCategory? category)
            ? category
            : Result<FailureCategory>.Fail(
                new Error("FailureCategory.Unknown", $"Unknown failure category token '{token}'."));

    public static FailureCategory FromToken(string token)
    {
        Result<FailureCategory> result = Create(token);

        if (result is Result<FailureCategory>.Success success)
        {
            return success.Value;
        }

        Result<FailureCategory>.Failure failure = (Result<FailureCategory>.Failure)result;
        throw new InvalidOperationException(failure.Error.Message);
    }

    public override string ToString() => Value;
}
