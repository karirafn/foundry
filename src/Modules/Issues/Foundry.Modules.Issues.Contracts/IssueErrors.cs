using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public static class IssueErrors
{
    public const string NotFoundCode = "Issue.NotFound";
    public const string WrongStateCode = "Issue.WrongState";
    public const string InvalidStatesCode = "Issue.InvalidStates";
    public const string InvalidCursorCode = "Issue.InvalidCursor";
    public const string MixedStatesCode = "Issue.MixedStates";

    public static Error NotFound(IssueId id) =>
        new(NotFoundCode, $"Issue with ID '{id.Value}' was not found.");

    public static Error WrongState(IssueId id, string expectedState) =>
        new(WrongStateCode, $"Issue with ID '{id.Value}' is not in the '{expectedState}' state.");

    public static Error InvalidStates(IEnumerable<string> unknownNames)
    {
        const int MaxEchoed = 5;
        List<string> names = unknownNames.Take(MaxEchoed + 1).ToList();
        bool truncated = names.Count > MaxEchoed;
        IEnumerable<string> echoed = truncated ? names.Take(MaxEchoed) : names;
        string suffix = truncated ? $" (and more)" : ".";
        return new(InvalidStatesCode, $"Unknown state value(s): {string.Join(", ", echoed)}{suffix}");
    }

    public static Error InvalidCursor() =>
        new(InvalidCursorCode, "The provided cursor is malformed or expired.");

    public static Error MixedStates() =>
        new(MixedStatesCode, "States must all be active or all be resolved — mixing active and resolved states is not supported.");
}
