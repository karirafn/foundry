using Foundry.Modules.Settings.Domain;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Infrastructure;

public interface IOAuthCredentialScanner
{
    Task<Result<OAuthCredentials>> ScanAsync(CancellationToken cancellationToken);
}
