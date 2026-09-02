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
    public const string PrClosedToken = "pr_closed";

    private static readonly FrozenSet<string> KnownTokens = new[]
    {
        NonZeroExitToken,
        TimedOutToken,
        ContainerErrorToken,
        UsageLimitedToken,
        WorkerBootstrapFailedToken,
        AuthInvalidToken,
        ProviderErrorToken,
        TransientApiErrorToken,
        CreditsExhaustedToken,
        PrClosedToken,
    }.ToFrozenSet(StringComparer.Ordinal);

    public static readonly FailureCategory NonZeroExit = new(NonZeroExitToken);
    public static readonly FailureCategory TimedOut = new(TimedOutToken);
    public static readonly FailureCategory ContainerError = new(ContainerErrorToken);
    public static readonly FailureCategory UsageLimited = new(UsageLimitedToken);
    public static readonly FailureCategory WorkerBootstrapFailed = new(WorkerBootstrapFailedToken);
    public static readonly FailureCategory AuthInvalid = new(AuthInvalidToken);
    public static readonly FailureCategory ProviderError = new(ProviderErrorToken);
    public static readonly FailureCategory TransientApiError = new(TransientApiErrorToken);
    public static readonly FailureCategory CreditsExhausted = new(CreditsExhaustedToken);
    public static readonly FailureCategory PrClosed = new(PrClosedToken);

    public string Value { get; }

    private FailureCategory(string value) => Value = value;

    public static Result<FailureCategory> Create(string token) =>
        KnownTokens.Contains(token)
            ? new FailureCategory(token)
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
