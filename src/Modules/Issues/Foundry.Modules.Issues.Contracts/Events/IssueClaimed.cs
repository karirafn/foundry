using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts.Events;

public sealed record IssueClaimed(ClaimedIssueDispatch Dispatch) : IIntegrationEvent;
