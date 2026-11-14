using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.AccountTokenChecks;

/// <summary>
/// Checks an account token with the provider and records the verdict on the account
/// (<see cref="Credential.TokenValidity"/>). A listing refused with HTTP 401 is confirmed before the
/// account is marked Invalid, and an Invalid account is rechecked once per
/// <see cref="Credential.TokenRecheckCooldown"/>. A verdict is saved only while the account still holds
/// the token that was tested: a token set in the meantime (<see cref="Credential.TokenVersion"/>) drops it.
/// </summary>
internal sealed class AccountTokenVerdicts(
    DbContext dbContext,
    IAccountTokenValidator validator,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<AccountTokenVerdicts> logger)
{
    /// <summary>
    /// Confirms a provider's 401 on a runtime call with a token check, and marks the account Invalid
    /// when the check also fails to authenticate. Returns whether the account was marked Invalid.
    /// </summary>
    public async Task<bool> ConfirmRejectionAsync(
        Credential credential,
        string token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Result<TokenValidationOutcome> check = await validator.ValidateAsync(credential, token, cancellationToken);

        switch (Classify(check))
        {
            case CheckVerdict.Rejected:
                bool marked = await RecordAsync(credential, c => c.MarkTokenInvalid(now), cancellationToken);
                if (marked)
                {
                    logger.LogWarning(
                        "The provider rejected the token of account '{CredentialName}' (id '{CredentialId}'); the account is marked as having an invalid token and its repositories are paused.",
                        credential.Name,
                        credential.Id);
                }

                return marked;

            case CheckVerdict.Authenticated:
                logger.LogInformation(
                    "A call with the token of account '{CredentialName}' (id '{CredentialId}') was refused with 401, but the token still authenticates; the account stays valid.",
                    credential.Name,
                    credential.Id);
                return false;

            default:
                logger.LogWarning(
                    "Could not confirm the 401 for account '{CredentialName}' (id '{CredentialId}'): the token check gave no verdict. It runs again next cycle.",
                    credential.Name,
                    credential.Id);
                return false;
        }
    }

    /// <summary>
    /// Rechecks the token of an Invalid account. Returns <see langword="true"/> only when the token
    /// authenticates again and the account was saved as Valid, so the caller may use it this cycle.
    /// </summary>
    public async Task<bool> RecheckAsync(
        Credential credential,
        string token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Result<TokenValidationOutcome> check = await validator.ValidateAsync(credential, token, cancellationToken);

        switch (Classify(check))
        {
            case CheckVerdict.Authenticated:
                bool revalidated = await RecordAsync(credential, c => c.MarkTokenValid(), cancellationToken);
                if (revalidated)
                {
                    logger.LogInformation(
                        "The token of account '{CredentialName}' (id '{CredentialId}') authenticates again; monitoring resumes.",
                        credential.Name,
                        credential.Id);
                }

                return revalidated;

            case CheckVerdict.Rejected:
                await RecordAsync(credential, c => c.MarkTokenInvalid(now), cancellationToken);
                logger.LogDebug(
                    "The token of account '{CredentialName}' (id '{CredentialId}') is still rejected.",
                    credential.Name,
                    credential.Id);
                return false;

            default:
                // No verdict: LastCheckedAt stays unstamped, so the recheck runs again next cycle.
                logger.LogWarning(
                    "Could not recheck the invalid token of account '{CredentialName}' (id '{CredentialId}'): the token check gave no verdict. It runs again next cycle.",
                    credential.Name,
                    credential.Id);
                return false;
        }
    }

    private static CheckVerdict Classify(Result<TokenValidationOutcome> check) =>
        check switch
        {
            Result<TokenValidationOutcome>.Success { Value: TokenValidationOutcome.AuthenticationFailedOutcome } =>
                CheckVerdict.Rejected,
            Result<TokenValidationOutcome>.Success
            {
                Value: TokenValidationOutcome.AuthenticatedOutcome or TokenValidationOutcome.ScopesUnverifiableOutcome,
            } => CheckVerdict.Authenticated,
            _ => CheckVerdict.None,
        };

    /// <summary>
    /// Applies <paramref name="verdict"/> to the stored account and saves it, unless the account no longer
    /// holds the token <paramref name="tested"/> was loaded with. Returns whether the verdict was saved.
    /// </summary>
    private async Task<bool> RecordAsync(
        Credential tested,
        Action<Credential> verdict,
        CancellationToken cancellationToken)
    {
        // The resolver reads accounts untracked, so the verdict write loads the account tracked.
        Credential? stored = await dbContext.Set<Credential>()
            .FirstOrDefaultAsync(c => c.Id == tested.Id, cancellationToken);

        if (stored is null)
        {
            return false;
        }

        // The verdict is about the tested token: pin the concurrency check to its version, so a token
        // set at any point since the account was resolved rejects the save.
        dbContext.Entry(stored).Property(c => c.TokenVersion).OriginalValue = tested.TokenVersion;
        verdict(stored);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            stored.ClearDomainEvents();

            // Not retried: the tested token is no longer the account's, and the next cycle tests the new one.
            foreach (EntityEntry entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            logger.LogDebug(
                "Dropped the token verdict for account '{CredentialId}': its token changed while it was being checked.",
                tested.Id);
            return false;
        }

        // Dispatched only once the verdict is committed, so a dropped save never announces a transition.
        IDomainEvent[] events = [.. stored.DomainEvents];
        stored.ClearDomainEvents();
        await domainEventDispatcher.DispatchAsync(events, cancellationToken);

        return true;
    }

    private enum CheckVerdict
    {
        None,
        Authenticated,
        Rejected,
    }
}
