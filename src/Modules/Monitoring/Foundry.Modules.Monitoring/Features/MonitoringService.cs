using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.Modules.Monitoring.Features;

internal sealed class MonitoringService(
    IServiceScopeFactory scopeFactory,
    IOptions<MonitoringOptions> optionsAccessor,
    ILogger<MonitoringService> logger) : PeriodicBackgroundService(logger)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _defaultPollInterval =
        TimeSpan.FromSeconds(optionsAccessor.Value.DefaultPollIntervalSeconds);

    protected override TimeSpan TickInterval => Interval;

    protected override Task TickAsync(CancellationToken cancellationToken)
        => ExecuteTickAsync(DateTimeOffset.UtcNow, cancellationToken);

    internal async Task ExecuteTickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        IIssueProviderFactory providerFactory = scope.ServiceProvider.GetRequiredService<IIssueProviderFactory>();
        RepositoryPoller poller = scope.ServiceProvider.GetRequiredService<RepositoryPoller>();

        ILookup<AccountId, MonitoredRepository> reposByAccount = await LoadActiveReposAsync(
            dbContext,
            cancellationToken);

        foreach (IGrouping<AccountId, MonitoredRepository> accountGroup in reposByAccount)
        {
            await ProcessAccountGroupAsync(
                accountGroup,
                dbContext,
                providerFactory,
                poller,
                now,
                cancellationToken);
        }
    }

    private static async Task<ILookup<AccountId, MonitoredRepository>> LoadActiveReposAsync(
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        List<MonitoredRepository> repos = await dbContext.Set<MonitoredRepository>()
            .Where(r => r.IsActive)
            .ToListAsync(cancellationToken);

        return repos.ToLookup(r => r.AccountId);
    }

    private async Task ProcessAccountGroupAsync(
        IGrouping<AccountId, MonitoredRepository> accountGroup,
        DbContext dbContext,
        IIssueProviderFactory providerFactory,
        RepositoryPoller poller,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AccountId accountId = accountGroup.Key;
        Account? account = await dbContext.Set<Account>()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account is null)
        {
            logger.LogWarning(
                "Account with id {AccountId} not found; skipping {Count} repo(s).",
                accountGroup.Key,
                accountGroup.Count());
            return;
        }

        if (string.IsNullOrEmpty(account.Token))
        {
            logger.LogWarning(
                "Account '{AccountName}' has no token configured; skipping {Count} repo(s).",
                account.Name,
                accountGroup.Count());
            return;
        }

        IIssueProvider provider = providerFactory.CreateProvider(account, account.Token);

        foreach (MonitoredRepository repo in accountGroup)
        {
            if (!repo.IsDueForPoll(_defaultPollInterval, now))
            {
                continue;
            }

            Result pollResult = await poller.PollAsync(repo, provider, now, cancellationToken);

            if (pollResult is Result.Failure pollFailure)
            {
                logger.LogWarning(
                    "Poll failed for repo '{Slug}': {Error}",
                    repo.Slug,
                    pollFailure.Error.Message);
            }
        }
    }
}
