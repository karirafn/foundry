using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features.Login;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Credentials.Features;

internal sealed class CredentialGate(DbContext dbContext, ILoginSessionState loginSessionState) : ICredentialGate
{
    public async Task<bool> CanDispatchAsync(CancellationToken cancellationToken)
    {
        if (loginSessionState.IsLoginActive)
        {
            return false;
        }

        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return account?.Validity is CredentialValidity.Valid;
    }
}
