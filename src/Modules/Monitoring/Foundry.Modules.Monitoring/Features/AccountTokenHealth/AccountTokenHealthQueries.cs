using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.AccountTokenHealth;

internal sealed class AccountTokenHealthQueries(DbContext dbContext) : IAccountTokenHealthQueries
{
    public Task<int> CountInvalidAsync(CancellationToken cancellationToken) =>
        dbContext.Set<Credential>()
            .AsNoTracking()
            .CountAsync(
                c => EF.Property<DateTimeOffset?>(c, CredentialConfiguration.TokenInvalidSinceField) != null,
                cancellationToken);
}
