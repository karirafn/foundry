using Foundry.Shared;

namespace Foundry.Modules.Issues.Domain;

public sealed record IssueAuthor
{
    public string Value { get; }

    private IssueAuthor(string value) => Value = value;

    public static Result<IssueAuthor> Create(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return Result<IssueAuthor>.Fail(IssueAuthorErrors.UsernameEmpty);
        }

        return new IssueAuthor(username);
    }

    public override string ToString() => Value;
}

public static class IssueAuthorErrors
{
    public static readonly Error UsernameEmpty = new("IssueAuthor.UsernameEmpty", "Username must not be empty.");
}
