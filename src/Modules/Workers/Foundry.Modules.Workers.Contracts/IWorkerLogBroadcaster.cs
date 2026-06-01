namespace Foundry.Modules.Workers.Contracts;

public interface IWorkerLogBroadcaster
{
    Task PushAsync(Guid issueId, WorkerReportSummary report, CancellationToken cancellationToken);
}
