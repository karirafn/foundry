using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
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

    public async Task<AccountTokenExpiryCounts> CountExpiryAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The status is asked of each loaded aggregate, so the warning window lives only on TokenExpiry and
        // no date is compared in SQL, where EF's SQLite provider stores DateTimeOffset as text.
        List<Credential> accounts = await dbContext.Set<Credential>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        List<TokenExpiryStatus> statuses = accounts
            .Where(a => a.TokenValidity is TokenValidity.Valid)
            .Select(a => a.TokenExpiry.StatusAt(now))
            .ToList();

        return new AccountTokenExpiryCounts(
            ExpiringSoon: statuses.Count(s => s == TokenExpiryStatus.ExpiringSoon),
            Expired: statuses.Count(s => s == TokenExpiryStatus.Expired));
    }
}
