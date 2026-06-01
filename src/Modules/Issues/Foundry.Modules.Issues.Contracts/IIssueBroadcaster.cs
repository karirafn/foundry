namespace Foundry.Modules.Issues.Contracts;

public interface IIssueBroadcaster
{
    Task BroadcastAsync(IssueSummary summary, CancellationToken cancellationToken);
}
