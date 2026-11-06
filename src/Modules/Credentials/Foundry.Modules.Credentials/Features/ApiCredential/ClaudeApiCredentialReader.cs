using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Credentials.Features.ApiCredential;

/// <summary>
/// Resolves the credential workers use: the API key in API-key mode, or the OAuth access token stored on the
/// shared credential volume, read through the credential-helper container.
/// </summary>
internal sealed class ClaudeApiCredentialReader(
    DbContext dbContext,
    ICredentialsOrchestrator orchestrator) : IClaudeApiCredentialReader
{
    internal static readonly Error NotConfigured = new(
        "Credentials.NotConfigured",
        "No Claude credential is configured. Configure an API key or sign in with OAuth in Settings.");

    public async Task<Result<ClaudeApiCredential>> ReadAsync(CancellationToken cancellationToken)
    {
        // AuthMode uses a ValueConverter (decrypt + JSON) that cannot be projected into SQL.
        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        switch (account?.AuthMode)
        {
            case AuthMode.ApiKey { Credential: ApiKeyCredential.Present present }:
                return new ClaudeApiCredential.ApiKey(present.Value);
            case AuthMode.OAuth:
                Result<string> token = await orchestrator.ReadOAuthAccessTokenAsync(cancellationToken);
                return token.Match<Result<ClaudeApiCredential>>(
                    value => new ClaudeApiCredential.OAuthToken(value),
                    Result<ClaudeApiCredential>.Fail);
            default:
                return Result<ClaudeApiCredential>.Fail(NotConfigured);
        }
    }
}
