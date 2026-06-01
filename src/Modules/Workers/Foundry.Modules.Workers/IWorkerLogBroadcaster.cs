using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers;

public interface IWorkerLogBroadcaster
{
    Task PushAsync(Guid issueId, WorkerReportSummary report, CancellationToken cancellationToken);
}
