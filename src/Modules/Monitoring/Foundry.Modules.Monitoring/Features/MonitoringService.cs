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

        ILookup<CredentialId, MonitoredRepository> reposByCredential = await LoadActiveReposAsync(
            dbContext,
            cancellationToken);

        foreach (IGrouping<CredentialId, MonitoredRepository> credentialGroup in reposByCredential)
        {
            await ProcessCredentialGroupAsync(
                credentialGroup,
                dbContext,
                providerFactory,
                poller,
                now,
                cancellationToken);
        }
    }

    private static async Task<ILookup<CredentialId, MonitoredRepository>> LoadActiveReposAsync(
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        List<MonitoredRepository> repos = await dbContext.Set<MonitoredRepository>()
            .Where(r => r.IsActive)
            .ToListAsync(cancellationToken);

        return repos.ToLookup(r => r.CredentialId);
    }

    private async Task ProcessCredentialGroupAsync(
        IGrouping<CredentialId, MonitoredRepository> credentialGroup,
        DbContext dbContext,
        IIssueProviderFactory providerFactory,
        RepositoryPoller poller,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        CredentialId credentialId = credentialGroup.Key;
        Credential? credential = await dbContext.Set<Credential>()
            .FirstOrDefaultAsync(a => a.Id == credentialId, cancellationToken);

        if (credential is null)
        {
            logger.LogWarning(
                "Credential with id {CredentialId} not found; skipping {Count} repo(s).",
                credentialGroup.Key,
                credentialGroup.Count());
            return;
        }

        if (string.IsNullOrEmpty(credential.Token))
        {
            logger.LogWarning(
                "Credential '{CredentialName}' has no token configured; skipping {Count} repo(s).",
                credential.Name,
                credentialGroup.Count());
            return;
        }

        IIssueProvider provider = providerFactory.CreateProvider(credential, credential.Token);

        foreach (MonitoredRepository repo in credentialGroup)
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
