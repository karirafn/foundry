using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public sealed record IssueClaimed(ClaimedIssueDispatch Dispatch) : IIntegrationEvent;
