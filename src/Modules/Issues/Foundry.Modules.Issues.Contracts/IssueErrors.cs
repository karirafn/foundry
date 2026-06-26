using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public static class IssueErrors
{
    public static readonly string NotFoundCode = "Issue.NotFound";
    public static readonly string WrongStateCode = "Issue.WrongState";
    public static readonly string InvalidStatesCode = "Issue.InvalidStates";
    public static readonly string InvalidCursorCode = "Issue.InvalidCursor";

    public static Error NotFound(IssueId id) =>
        new(NotFoundCode, $"Issue with ID '{id.Value}' was not found.");

    public static Error WrongState(IssueId id, string expectedState) =>
        new(WrongStateCode, $"Issue with ID '{id.Value}' is not in the '{expectedState}' state.");

    public static Error InvalidStates(IEnumerable<string> unknownNames) =>
        new(InvalidStatesCode, $"Unknown state value(s): {string.Join(", ", unknownNames)}.");

    public static Error InvalidCursor() =>
        new(InvalidCursorCode, "The provided cursor is malformed or expired.");
}
