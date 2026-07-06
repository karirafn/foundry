using Foundry.Modules.Credentials.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Foundry.Modules.Credentials.Features;

internal sealed class ClaudeAccountSeeder(IServiceScopeFactory scopeFactory) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        bool exists = await dbContext.Set<ClaudeAccount>()
            .AnyAsync(cancellationToken);

        if (exists)
        {
            return;
        }

        ClaudeAccount account = ClaudeAccount.Create();
        dbContext.Set<ClaudeAccount>().Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
