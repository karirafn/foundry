using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features;

internal interface IWorkerOrchestrator
{
    Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken);

    Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken);

    Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken);

    Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken);

    IAsyncEnumerable<string> StreamLogsAsync(string containerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(CancellationToken cancellationToken);

    Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken);

    Task StopContainerAsync(string containerId, CancellationToken cancellationToken);

    Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken);

    Task<Result<ContainerId>> StartLoginContainerAsync(
        LoginContainerSpec spec,
        CancellationToken cancellationToken);

    Task DeliverLoginCodeAsync(string containerId, string code, CancellationToken cancellationToken);

    Task<Result<AccountIdentity>> GetAuthStatusAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads account identity from the credential volume by starting a short-lived helper container
    /// that mounts the volume and runs <c>claude auth status --json</c> via exec.
    /// This is lifecycle-independent from the login container — the CLI writes credentials to the volume
    /// before exiting, so this method can be called after the login container has already stopped.
    /// Integration coverage of the underlying exec+parse primitive lives in
    /// <c>LoginFlowIntegrationTests.GetAuthStatusAsync_ParsesRealCliOutputFraming</c>.
    /// </summary>
    Task<Result<AccountIdentity>> GetCredentialVolumeAuthStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ContainerId>> ListLoginContainersByLabelAsync(CancellationToken cancellationToken);

    Task SeedOnboardingAsync(CancellationToken cancellationToken);
}
