using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public static class IssueErrors
{
    public static readonly string NotFoundCode = "Issue.NotFound";
    public static readonly string WrongStateCode = "Issue.WrongState";

    public static Error NotFound(IssueId id) =>
        new(NotFoundCode, $"Issue with ID '{id.Value}' was not found.");

    public static Error WrongState(IssueId id, string expectedState) =>
        new(WrongStateCode, $"Issue with ID '{id.Value}' is not in the '{expectedState}' state.");
}
