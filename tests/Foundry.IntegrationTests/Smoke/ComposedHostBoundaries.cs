using Docker.DotNet;

using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Foundry.IntegrationTests.Smoke;

/// <summary>
/// The external boundaries of the composed host, faked at the seams production uses: the Docker daemon
/// (image operations, worker containers, credential helper containers, daemon ping) and the Git provider.
/// Everything inside the host stays real.
/// </summary>
internal sealed class ComposedHostBoundaries : IDisposable
{
    public CapturedLogs Logs { get; } = new();

    public HeldImageBuild ImageBuild { get; } = new();

    public FakeWorkerContainers Containers { get; } = new();

    public FakeGitHub GitHub { get; } = new();

    public FoundryWebAppFactory CreateHost() =>
        FoundryWebAppFactory.WithHostedServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(Logs);
            // Dispatch reports the gate that held a tick only at Debug; the test waits on that signal.
            services.AddLogging(logging => logging.AddFilter<CapturedLogs>(typeof(WorkerDispatchService).FullName, LogLevel.Debug));

            services.RemoveAll<IImageOperations>();
            services.AddSingleton<IImageOperations>(ImageBuild);
            services.RemoveAll<ISystemOperations>();
            services.AddSingleton<ISystemOperations>(new ReachableDockerDaemon());
            services.RemoveAll<IWorkerOrchestrator>();
            services.AddSingleton<IWorkerOrchestrator>(Containers);
            services.RemoveAll<ICredentialsOrchestrator>();
            services.AddSingleton<ICredentialsOrchestrator>(new IdleCredentialsOrchestrator());

            services.RemoveAll<IIssueProviderFactory>();
            services.AddSingleton<IIssueProviderFactory>(GitHub);
            services.RemoveAll<IGitHubWriteProber>();
            services.AddSingleton<IGitHubWriteProber>(GitHub);
        });

    public void Dispose() => Logs.Dispose();
}
