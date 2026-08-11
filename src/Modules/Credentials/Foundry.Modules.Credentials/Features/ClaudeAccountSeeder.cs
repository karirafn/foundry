using System.Reflection;

using Foundry.Modules.Credentials.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Foundry.Modules.Credentials.Features;

internal sealed class ClaudeAccountSeeder(IServiceScopeFactory scopeFactory) : IHostedLifecycleService
{
    private const string DocGenerationEntryAssemblyName = "GetDocument.Insider";

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        // Skip DB seeding when the build-time OpenAPI doc generation tool runs the app entry point.
        if (Assembly.GetEntryAssembly()?.GetName().Name == DocGenerationEntryAssemblyName)
        {
            return;
        }

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
