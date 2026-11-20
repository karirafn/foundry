using System.Reflection;

using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Repositories;

/// <summary>
/// Gives an Unclaimed Repository an automatic exit: every hour, re-derives the Namespace Claims on each host
/// that has one, so a token whose repository access was edited in place claims it without an operator action.
/// </summary>
internal sealed class UnclaimedRepositoryReclaimService : PeriodicBackgroundService
{
    private const string DocGenerationEntryAssemblyName = "GetDocument.Insider";

    private static readonly TimeSpan ReclaimInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UnclaimedRepositoryReclaimService> _logger;

    public UnclaimedRepositoryReclaimService(
        IServiceScopeFactory scopeFactory,
        ILogger<UnclaimedRepositoryReclaimService> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override TimeSpan TickInterval => ReclaimInterval;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // The build-time OpenAPI doc generation tool runs the app with no database to read repositories from.
        Assembly.GetEntryAssembly()?.GetName().Name == DocGenerationEntryAssemblyName
            ? Task.CompletedTask
            : base.ExecuteAsync(stoppingToken);

    protected override Task TickAsync(CancellationToken cancellationToken) => ReclaimAsync(cancellationToken);

    internal async Task ReclaimAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> hosts;

        await using (AsyncServiceScope scope = _scopeFactory.CreateAsyncScope())
        {
            hosts = await scope.ServiceProvider.GetRequiredService<ClaimRecheck>()
                .FindUnclaimedHostsAsync(cancellationToken);
        }

        foreach (string host in hosts)
        {
            await ReclaimHostAsync(host, cancellationToken);
        }
    }

    private async Task ReclaimHostAsync(string host, CancellationToken cancellationToken)
    {
        // A fresh scope per host keeps one host's failed save from leaking tracked changes into the next.
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

        try
        {
            int reclaimed = await scope.ServiceProvider.GetRequiredService<ClaimRecheck>()
                .RecheckHostAsync(host, cancellationToken);

            _logger.LogInformation(
                "Claim recheck on {Host} claimed {ReclaimedRepositoryCount} unclaimed repositories again.",
                host,
                reclaimed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // One host's failure must not stop the sweep of the remaining hosts.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Claim recheck on {Host} failed.", host);
        }
    }
}
