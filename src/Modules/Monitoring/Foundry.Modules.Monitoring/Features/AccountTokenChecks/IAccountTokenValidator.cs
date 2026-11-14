using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.AccountTokenChecks;

/// <summary>
/// Asks the account's provider whether <c>token</c> authenticates — the same test as the account
/// form's live token check. A failure is a check with no verdict (transport error, 5xx).
/// </summary>
internal interface IAccountTokenValidator
{
    Task<Result<TokenValidationOutcome>> ValidateAsync(
        Credential credential,
        string token,
        CancellationToken cancellationToken);
}
