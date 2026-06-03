using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public static class IssueErrors
{
    public static Error NotFound(IssueId id) =>
        new("Issue.NotFound", $"Issue with ID '{id.Value}' was not found.");

    public static Error WrongState(IssueId id, string expectedState) =>
        new("Issue.WrongState", $"Issue with ID '{id.Value}' is not in the '{expectedState}' state.");
}
