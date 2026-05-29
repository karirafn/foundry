using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.WebApi.Modules.Monitoring.Features;

internal sealed class MonitoringSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<MonitoringOptions> optionsAccessor,
    ILogger<MonitoringSeeder> logger) : IHostedLifecycleService
{
    private readonly MonitoringOptions _options = optionsAccessor.Value;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        await SeedAccountsAsync(dbContext, cancellationToken);
        await SeedRepositoriesAsync(dbContext, cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedAccountsAsync(FoundryDbContext dbContext, CancellationToken cancellationToken)
    {
        HashSet<string> existingNames = (await dbContext.Set<Account>()
            .AsNoTracking()
            .Select(a => a.Name)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (AccountOption accountOption in _options.Accounts)
        {
            if (existingNames.Contains(accountOption.Name))
            {
                continue;
            }

            if (string.Equals(accountOption.Type, "GitHub", StringComparison.OrdinalIgnoreCase))
            {
                GitHubAccount account = GitHubAccount.Create(
                    accountOption.Name,
                    accountOption.SecretKeyName,
                    new Uri(accountOption.BaseUrl));

                dbContext.Set<Account>().Add(account);
            }
            else
            {
                logger.LogWarning(
                    "Skipping account '{AccountName}': unknown type '{AccountType}'.",
                    accountOption.Name,
                    accountOption.Type);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedRepositoriesAsync(FoundryDbContext dbContext, CancellationToken cancellationToken)
    {
        HashSet<string> existingSlugs = (await dbContext.Set<MonitoredRepository>()
            .AsNoTracking()
            .Select(r => r.Slug.ToString())
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        Dictionary<string, AccountId> accountsByName = await dbContext.Set<Account>()
            .AsNoTracking()
            .ToDictionaryAsync(a => a.Name, a => a.Id, cancellationToken);

        foreach (RepositoryOption repoOption in _options.Repositories)
        {
            if (existingSlugs.Contains(repoOption.Slug))
            {
                continue;
            }

            if (!accountsByName.TryGetValue(repoOption.AccountName, out AccountId accountId))
            {
                logger.LogWarning(
                    "Skipping repository '{Slug}': account '{AccountName}' was not found.",
                    repoOption.Slug,
                    repoOption.AccountName);
                continue;
            }

            Result<RepositorySlug> slugResult = RepositorySlug.Create(repoOption.Slug);

            if (slugResult is not Result<RepositorySlug>.Success slugSuccess)
            {
                string errorCode = slugResult is Result<RepositorySlug>.Failure f ? f.Error.Code : "unknown";
                logger.LogWarning(
                    "Skipping repository '{Slug}': invalid slug ({ErrorCode}).",
                    repoOption.Slug,
                    errorCode);
                continue;
            }

            TimeSpan? pollInterval = repoOption.PollIntervalSeconds.HasValue
                ? TimeSpan.FromSeconds(repoOption.PollIntervalSeconds.Value)
                : null;

            MonitoredRepository repository = MonitoredRepository.Create(
                slugSuccess.Value,
                accountId,
                pollInterval);

            dbContext.Set<MonitoredRepository>().Add(repository);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
