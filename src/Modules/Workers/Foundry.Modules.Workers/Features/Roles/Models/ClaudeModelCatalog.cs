using Foundry.Modules.Credentials.Contracts;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Roles.Models;

/// <summary>The cached model list and the outcome of the last refresh.</summary>
/// <param name="Models">The last successfully fetched list; empty until a refresh succeeds.</param>
/// <param name="RefreshedAt">When <paramref name="Models"/> was fetched; <see langword="null"/> before the first success.</param>
/// <param name="RefreshError">Why the last refresh failed; <see langword="null"/> when it succeeded or none ran yet.</param>
internal sealed record ClaudeModelList(
    IReadOnlyList<ClaudeModel> Models,
    DateTimeOffset? RefreshedAt,
    string? RefreshError)
{
    public static readonly ClaudeModelList Empty = new([], null, null);

    public bool Lists(string modelId) => Models.Any(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));
}

/// <summary>
/// Caches the models the workers' credential can use. The list is fetched with the same credential workers use and
/// refreshed on demand (the role view refreshes it when it opens). A failed refresh keeps the previous list and
/// records the error.
/// </summary>
internal interface IClaudeModelCatalog
{
    /// <summary>The cached list, without fetching.</summary>
    ClaudeModelList Current { get; }

    /// <summary>The cached list, fetching it first when no refresh has run yet.</summary>
    Task<ClaudeModelList> GetAsync(CancellationToken cancellationToken);

    /// <summary>Fetches the list now. Concurrent callers join the refresh already in flight.</summary>
    Task<ClaudeModelList> RefreshAsync(CancellationToken cancellationToken);
}

internal sealed partial class ClaudeModelCatalog(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ClaudeModelCatalog> logger) : IClaudeModelCatalog
{
    private readonly Lock _lock = new();
    private ClaudeModelList _current = ClaudeModelList.Empty;
    private bool _attempted;
    private Task<ClaudeModelList>? _inFlight;

    public ClaudeModelList Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public Task<ClaudeModelList> GetAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_attempted)
            {
                return Task.FromResult(_current);
            }
        }

        return RefreshAsync(cancellationToken);
    }

    public Task<ClaudeModelList> RefreshAsync(CancellationToken cancellationToken)
    {
        Task<ClaudeModelList> refresh;

        lock (_lock)
        {
            // The fetch is shared by every caller that joins it, so it does not observe any one caller's token.
            _inFlight ??= FetchAsync();
            refresh = _inFlight;
        }

        return refresh.WaitAsync(cancellationToken);
    }

    private async Task<ClaudeModelList> FetchAsync()
    {
        await Task.Yield();

        try
        {
            Result<IReadOnlyList<ClaudeModel>> result = await FetchModelsAsync();

            lock (_lock)
            {
                _attempted = true;
                _current = result switch
                {
                    Result<IReadOnlyList<ClaudeModel>>.Success success =>
                        new ClaudeModelList(success.Value, timeProvider.GetUtcNow(), null),
                    Result<IReadOnlyList<ClaudeModel>>.Failure failure =>
                        _current with { RefreshError = failure.Error.Message },
                    _ => _current,
                };

                return _current;
            }
        }
        finally
        {
            lock (_lock)
            {
                _inFlight = null;
            }
        }
    }

    private async Task<Result<IReadOnlyList<ClaudeModel>>> FetchModelsAsync()
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IClaudeApiCredentialReader credentialReader = scope.ServiceProvider.GetRequiredService<IClaudeApiCredentialReader>();

            // The typed HttpClient is resolved per fetch so this singleton never pins one handler.
            IClaudeModelsApi modelsApi = scope.ServiceProvider.GetRequiredService<IClaudeModelsApi>();

            Result<ClaudeApiCredential> credential = await credentialReader.ReadAsync(CancellationToken.None);

            if (credential is not Result<ClaudeApiCredential>.Success credentialSuccess)
            {
                Error error = ((Result<ClaudeApiCredential>.Failure)credential).Error;
                LogRefreshFailed(error.Code, error.Message);
                return Result<IReadOnlyList<ClaudeModel>>.Fail(error);
            }

            Result<IReadOnlyList<ClaudeModel>> models = await modelsApi.ListAsync(credentialSuccess.Value, CancellationToken.None);

            if (models is Result<IReadOnlyList<ClaudeModel>>.Failure modelsFailure)
            {
                LogRefreshFailed(modelsFailure.Error.Code, modelsFailure.Error.Message);
            }

            return models;
        }
#pragma warning disable CA1031 // A refresh failure must surface as the list's error, never fault the role view.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRefreshThrew(ex);
            return Result<IReadOnlyList<ClaudeModel>>.Fail(new Error(AnthropicModelsClient.FailedCode, "The model list could not be loaded."));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model list refresh failed ({Code}): {Message}")]
    private partial void LogRefreshFailed(string code, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Model list refresh threw")]
    private partial void LogRefreshThrew(Exception exception);
}
