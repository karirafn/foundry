using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Input arrived for an awaiting-input issue (ADR 0096): after the question's post time, an eligible comment Foundry did
/// not post appeared on the question's thread and outlasted the quiet period. <paramref name="Input"/> is the comment
/// text, the comments joined oldest first when several arrived together.
/// </summary>
public sealed record AwaitingInputArrived(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    string Input,
    DateTimeOffset ArrivedAt) : IIntegrationEvent;
