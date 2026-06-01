using Foundry.Modules.Issues.Contracts;

using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

internal sealed class SignalRIssueBroadcaster(IHubContext<IssueHub> hubContext) : IIssueBroadcaster
{
    private const string IssueUpdatedMethod = "IssueUpdated";

    public Task BroadcastAsync(IssueSummary summary, CancellationToken cancellationToken)
        => hubContext.Clients.All.SendAsync(IssueUpdatedMethod, summary, cancellationToken);
}
