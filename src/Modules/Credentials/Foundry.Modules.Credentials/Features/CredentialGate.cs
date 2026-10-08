using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
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

        if (account is null)
        {
            return false;
        }

        return account.CanDispatch;
    }
}
