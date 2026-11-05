using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.SessionCheck;

/// <summary>
/// Process-wide singleton that checks the stored Claude OAuth session at most once concurrently
/// (fail-fast single flight) and invalidates the account when the session is gone. Scoped
/// dependencies are resolved per check via <see cref="IServiceScopeFactory"/>.
/// <para>
/// Only a status that proves the session is gone (<see cref="OAuthSessionStatus.RequiresSignIn"/>)
/// invalidates. An expired token whose refresh failed transiently, an unreadable file, or a Docker
/// error leaves the account untouched, and the next tick retries.
/// </para>
/// </summary>
internal sealed class OAuthSessionCheckCoordinator(
    IServiceScopeFactory scopeFactory,
    ICredentialsOrchestrator orchestrator,
    ILoginSessionState loginSessionState,
    ILogger<OAuthSessionCheckCoordinator> logger) : IOAuthSessionCheckCoordinator
{
    internal const string RefreshRejectedReason =
        "Claude OAuth session expired: the refresh token was rejected. Sign in again to resume workers.";

    internal const string NotRefreshableReason =
        "Claude OAuth session expired and no refresh token is stored. Sign in again to resume workers.";

    internal const string SignedOutReason =
        "No Claude OAuth session is stored on the credential volume. Sign in again to resume workers.";

    // 0 = idle, 1 = a check is in flight.
    private int _inFlight;

    public async Task<OAuthSessionStatus?> CheckAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            logger.LogDebug("OAuth session check skipped — a check is already in flight.");
            return null;
        }

        try
        {
            return await CheckCoreAsync(cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }
    }

    private async Task<OAuthSessionStatus?> CheckCoreAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset? observedUpdatedAt = await LoadCheckableAccountUpdatedAtAsync(cancellationToken);

        if (observedUpdatedAt is null)
        {
            return null;
        }

        Result<OAuthSessionStatus> result = await orchestrator.CheckOAuthSessionAsync(cancellationToken);

        if (result is Result<OAuthSessionStatus>.Failure failure)
        {
            logger.LogWarning(
                "OAuth session check could not run: {Error}. Account left unchanged; retrying on the next interval.",
                failure.Error.Message);
            return null;
        }

        OAuthSessionStatus status = ((Result<OAuthSessionStatus>.Success)result).Value;

        switch (status)
        {
            case OAuthSessionStatus.Active:
                logger.LogDebug("OAuth session check: session is active.");
                break;

            case OAuthSessionStatus.Expired:
                logger.LogWarning(
                    "OAuth session check: the access token has expired and the refresh did not complete. " +
                    "Treating it as transient; retrying on the next interval.");
                break;

            case OAuthSessionStatus.Unreadable:
                logger.LogWarning(
                    "OAuth session check: the stored credential file could not be parsed. " +
                    "Account left unchanged; retrying on the next interval.");
                break;

            default:
                await InvalidateAsync(status, observedUpdatedAt.Value, cancellationToken);
                break;
        }

        return status;
    }

    /// <summary>
    /// Returns the account's <see cref="ClaudeAccount.UpdatedAt"/> when a check should run, or
    /// <c>null</c> to skip: no account, not an OAuth account with a committed login, already
    /// invalid (re-validation comes from signing in again), or a login in progress.
    /// </summary>
    private async Task<DateTimeOffset?> LoadCheckableAccountUpdatedAtAsync(CancellationToken cancellationToken)
    {
        if (loginSessionState.IsLoginActive)
        {
            logger.LogDebug("OAuth session check skipped — login session is active.");
            return null;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null
            || account.AuthMode is not AuthMode.OAuth
            || account.OAuthAccountEmail is null
            || account.Validity is CredentialValidity.Invalid)
        {
            logger.LogDebug("OAuth session check skipped — no valid, signed-in OAuth account.");
            return null;
        }

        return account.UpdatedAt;
    }

    private async Task InvalidateAsync(
        OAuthSessionStatus status,
        DateTimeOffset observedUpdatedAt,
        CancellationToken cancellationToken)
    {
        string reason = status switch
        {
            OAuthSessionStatus.RefreshRejected => RefreshRejectedReason,
            OAuthSessionStatus.NotRefreshable => NotRefreshableReason,
            OAuthSessionStatus.SignedOut => SignedOutReason,
            _ => throw new System.Diagnostics.UnreachableException(
                $"Unexpected terminal OAuthSessionStatus: {status.GetType().Name}"),
        };

        // A sign-in that started or committed while the helper container ran wrote fresh
        // credentials after the file was read; invalidating now would undo it.
        if (loginSessionState.IsLoginActive)
        {
            logger.LogInformation("OAuth session check result discarded — a login started during the check.");
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        IIntegrationEventDispatcher dispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null || account.UpdatedAt != observedUpdatedAt)
        {
            logger.LogInformation(
                "OAuth session check result discarded — the account changed during the check.");
            return;
        }

        if (!account.Invalidate(reason))
        {
            return;
        }

        // Enqueue before saving so the outbox interceptor harvests CredentialsInvalidated in the
        // same transaction as the validity change (pauses dispatch, notifies the dashboard).
        await dispatcher.DispatchAsync([new CredentialsInvalidated(reason)], cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning("OAuth session check invalidated the Claude account: {Reason}", reason);
    }
}
