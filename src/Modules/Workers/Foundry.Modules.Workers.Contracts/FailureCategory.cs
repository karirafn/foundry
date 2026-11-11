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
    public const string TransientApiErrorToken = "transient_api_error";
    public const string CreditsExhaustedToken = "credits_exhausted";
    public const string ModelUnavailableToken = "model_unavailable";
    public const string PrClosedToken = "pr_closed";
    public const string CancelledToken = "cancelled";
    public const string TurnLimitToken = "turn_limit";
    public const string VerdictInvalidToken = "verdict_invalid";
    public const string PublishFailedToken = "publish_failed";
    public const string PublishExpiredToken = "publish_expired";

    public static readonly FailureCategory NonZeroExit = new(NonZeroExitToken);
    public static readonly FailureCategory TimedOut = new(TimedOutToken);
    public static readonly FailureCategory ContainerError = new(ContainerErrorToken);
    public static readonly FailureCategory UsageLimited = new(UsageLimitedToken);
    public static readonly FailureCategory WorkerBootstrapFailed = new(WorkerBootstrapFailedToken);
    public static readonly FailureCategory AuthInvalid = new(AuthInvalidToken);
    public static readonly FailureCategory ProviderError = new(ProviderErrorToken);
    public static readonly FailureCategory TransientApiError = new(TransientApiErrorToken);
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
            { TransientApiErrorToken, TransientApiError },
            { CreditsExhaustedToken, CreditsExhausted },
            { ModelUnavailableToken, ModelUnavailable },
            { PrClosedToken, PrClosed },
            { CancelledToken, Cancelled },
            { TurnLimitToken, TurnLimit },
            { VerdictInvalidToken, VerdictInvalid },
            { PublishFailedToken, PublishFailed },
            { PublishExpiredToken, PublishExpired },
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public string Value { get; }

    private FailureCategory(string value) => Value = value;

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
