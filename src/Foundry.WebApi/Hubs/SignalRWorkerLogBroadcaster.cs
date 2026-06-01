using Foundry.Modules.Workers;
using Foundry.Modules.Workers.Contracts;

using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

internal sealed class SignalRWorkerLogBroadcaster(IHubContext<WorkerLogHub> hubContext) : IWorkerLogBroadcaster
{
    private const string ReportReceivedMethod = "ReportReceived";

    public Task PushAsync(Guid issueId, WorkerReportSummary report, CancellationToken cancellationToken)
        => hubContext.Clients.Group(WorkerLogHub.GroupName(issueId)).SendAsync(
            ReportReceivedMethod,
            report,
            cancellationToken);
}
