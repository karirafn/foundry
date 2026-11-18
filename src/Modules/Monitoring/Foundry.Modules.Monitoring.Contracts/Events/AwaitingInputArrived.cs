using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Input arrived for an awaiting-input issue (ADR 0096): after the question's post time, eligible comments Foundry did not
/// post appeared on the question's thread, the issue description was edited (issue-thread waits only), or the run's
/// branch moved, and all of it outlasted the quiet period. <paramref name="Input"/> holds a
/// <see cref="InputPart.Comments"/> part with the comment text joined oldest first when comments arrived, then a
/// <see cref="InputPart.DescriptionEdited"/> part when the description changed, then a
/// <see cref="InputPart.BranchUpdated"/> part when the branch head moved; <paramref name="ArrivedAt"/> is the newest
/// input's time.
/// </summary>
public sealed record AwaitingInputArrived(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    ClarificationInput Input,
    DateTimeOffset ArrivedAt) : IIntegrationEvent;
