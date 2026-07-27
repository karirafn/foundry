using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.Modules.Monitoring.Features.Polling;

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
        ICredentialResolver credentialResolver = scope.ServiceProvider.GetRequiredService<ICredentialResolver>();
        RepositoryPoller poller = scope.ServiceProvider.GetRequiredService<RepositoryPoller>();

        List<MonitoredRepository> repos = await LoadActiveReposAsync(dbContext, cancellationToken);

        foreach (MonitoredRepository repo in repos)
        {
            if (!repo.IsDueForPoll(_defaultPollInterval, now))
            {
                continue;
            }

            await PollRepoAsync(
                repo,
                dbContext,
                providerFactory,
                credentialResolver,
                poller,
                now,
                cancellationToken);
        }
    }

    private static async Task<List<MonitoredRepository>> LoadActiveReposAsync(
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<MonitoredRepository>()
            .Where(r => r.IsActive)
            .ToListAsync(cancellationToken);
    }

    private async Task PollRepoAsync(
        MonitoredRepository repo,
        DbContext dbContext,
        IIssueProviderFactory providerFactory,
        ICredentialResolver credentialResolver,
        RepositoryPoller poller,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Credential? credential = await credentialResolver.ResolveAsync(
            repo.Host,
            repo.Slug,
            cancellationToken);

        if (credential is null)
        {
            logger.LogDebug(
                "No credential covers repository '{Slug}'; skipping poll.",
                repo.Slug);
            return;
        }

        if (string.IsNullOrEmpty(credential.Token))
        {
            logger.LogWarning(
                "Credential '{CredentialName}' has no token configured; skipping repo '{Slug}'.",
                credential.Name,
                repo.Slug);
            return;
        }

        IIssueProvider provider = providerFactory.CreateProvider(credential, credential.Token);

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
