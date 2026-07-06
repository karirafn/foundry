using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Contracts.Queries;
using Foundry.Modules.Credentials.Domain;
using Foundry.Modules.Credentials.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Credentials.Features;

internal sealed class CredentialQueries(DbContext dbContext) : ICredentialQueries
{
    internal const string OAuthStatusNotConfigured = "NotConfigured";
    internal const string OAuthStatusPresent = "Present";
    internal const string OAuthStatusReLoginNeeded = "ReLoginNeeded";

    public async Task<string?> GetAuthModeAsync(CancellationToken cancellationToken)
    {
        // AuthMode uses a ValueConverter (decrypt + JSON) that cannot be projected into SQL.
        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        return account.AuthMode switch
        {
            AuthMode.ApiKey => "ApiKey",
            AuthMode.OAuth => "OAuth",
            _ => null,
        };
    }

    public async Task<(string Key, string Value)?> GetAuthEnvironmentVariableAsync(
        CancellationToken cancellationToken)
    {
        // Full entity load is required here: AuthMode uses a ValueConverter that
        // applies decrypt + JSON deserialization, which cannot be expressed as a
        // SQL projection. EF Core cannot translate the converter into a SELECT.
        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        return account.AuthMode switch
        {
            AuthMode.ApiKey apiKey => ("ANTHROPIC_API_KEY", apiKey.Key),
            AuthMode.OAuth => null,
            _ => null,
        };
    }

    public async Task<bool> IsValidAsync(CancellationToken cancellationToken)
    {
        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return account?.Validity is CredentialValidity.Valid;
    }

    public async Task<ClaudeAccountSummary?> GetSummaryAsync(CancellationToken cancellationToken)
    {
        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return account is null ? null : ToSummary(account);
    }

    internal static ClaudeAccountSummary ToSummary(ClaudeAccount account)
    {
        string authModeName = account.AuthMode switch
        {
            AuthMode.ApiKey => "ApiKey",
            AuthMode.OAuth => "OAuth",
            _ => "Unknown",
        };

        string oauthStatus = ComputeOAuthStatus(account);
        string? subscriptionType = account.AuthMode is AuthMode.OAuth oauth ? oauth.SubscriptionType : null;

        return new ClaudeAccountSummary(
            account.Id.Value,
            authModeName,
            oauthStatus,
            subscriptionType,
            account.OAuthAccountEmail,
            account.OAuthAccountOrgName);
    }

    private static string ComputeOAuthStatus(ClaudeAccount account)
    {
        if (account.AuthMode is not AuthMode.OAuth)
        {
            return OAuthStatusNotConfigured;
        }

        if (account.Validity is CredentialValidity.Invalid)
        {
            return OAuthStatusReLoginNeeded;
        }

        return string.IsNullOrEmpty(account.OAuthAccountEmail)
            ? OAuthStatusReLoginNeeded
            : OAuthStatusPresent;
    }
}
