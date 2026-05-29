using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.Shared;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.WebApi.Modules.Monitoring.Features;

internal sealed class MonitoringService(
    IServiceScopeFactory scopeFactory,
    IOptions<MonitoringOptions> optionsAccessor,
    ILogger<MonitoringService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _defaultPollInterval =
        TimeSpan.FromSeconds(optionsAccessor.Value.DefaultPollIntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TickInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExecuteTickAsync(DateTimeOffset.UtcNow, stoppingToken);
        }
    }

    internal async Task ExecuteTickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IProviderAuth providerAuth = scope.ServiceProvider.GetRequiredService<IProviderAuth>();
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
                providerAuth,
                providerFactory,
                poller,
                now,
                cancellationToken);
        }
    }

    private static async Task<ILookup<AccountId, MonitoredRepository>> LoadActiveReposAsync(
        FoundryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        List<MonitoredRepository> repos = await dbContext.Set<MonitoredRepository>()
            .Where(r => r.IsActive)
            .ToListAsync(cancellationToken);

        return repos.ToLookup(r => r.AccountId);
    }

    private async Task ProcessAccountGroupAsync(
        IGrouping<AccountId, MonitoredRepository> accountGroup,
        FoundryDbContext dbContext,
        IProviderAuth providerAuth,
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

        Result<string> tokenResult = await providerAuth.GetTokenAsync(
            account.SecretKeyName,
            cancellationToken);

        if (tokenResult is not Result<string>.Success tokenSuccess)
        {
            logger.LogWarning(
                "Could not retrieve token for account '{AccountName}' ({ErrorCode}); skipping {Count} repo(s).",
                account.Name,
                tokenResult is Result<string>.Failure f ? f.Error.Code : "unknown",
                accountGroup.Count());
            return;
        }

        IIssueProvider provider = providerFactory.CreateProvider(account, tokenSuccess.Value);

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
