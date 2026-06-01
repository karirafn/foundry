using Microsoft.AspNetCore.SignalR;

namespace Foundry.WebApi.Hubs;

public sealed class WorkerLogHub : Hub
{
    public Task JoinIssueLog(Guid issueId)
        => Groups.AddToGroupAsync(Context.ConnectionId, GroupName(issueId));

    public Task LeaveIssueLog(Guid issueId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(issueId));

    internal static string GroupName(Guid issueId) => $"issue-{issueId}";
}
