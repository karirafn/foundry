using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.AccountTokenChecks;

internal sealed class AccountTokenValidator(
    GitHubHttpClient gitHubHttpClient,
    GitLabHttpClient gitLabHttpClient) : IAccountTokenValidator
{
    internal const string TransportFailedCode = "AccountTokenCheck.TransportFailed";

    public async Task<Result<TokenValidationOutcome>> ValidateAsync(
        Credential credential,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            return credential is GitLabCredential
                ? await gitLabHttpClient.ValidateTokenAsync(credential.ApiBaseUrl, token, cancellationToken)
                : await gitHubHttpClient.ValidateTokenAsync(credential.ApiBaseUrl, token, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // An unreachable provider gives no verdict on the token; the caller retries next cycle.
            return Result<TokenValidationOutcome>.Fail(new Error(TransportFailedCode, ex.Message));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation; it is a transport failure, not a shutdown.
            return Result<TokenValidationOutcome>.Fail(new Error(TransportFailedCode, ex.Message));
        }
    }
}
