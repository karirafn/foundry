using Foundry.Modules.Settings.Domain;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Infrastructure;

internal interface IOAuthCredentialScanner
{
    Task<Result<OAuthCredentials>> ScanAsync(CancellationToken cancellationToken);
}
