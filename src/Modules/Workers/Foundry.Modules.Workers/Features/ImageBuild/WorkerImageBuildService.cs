using Microsoft.Extensions.Hosting;

namespace Foundry.Modules.Workers.Features.ImageBuild;

internal sealed class WorkerImageBuildService : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        // Startup image build is handled by WorkerImageRebuildService.StartingAsync,
        // which reads persisted flags from the database and enqueues via the rebuild queue.
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
