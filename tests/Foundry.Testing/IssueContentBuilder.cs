using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Testing;

public sealed class IssueContentBuilder
{
    private static readonly DateTimeOffset FirstCommentAt = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private string _title = "Add retry";
    private string _body = string.Empty;
    private List<IssueContentComment> _comments = [];
    private SpecHash _specHash = SpecHashFixtures.Any;

    public IssueContentBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public IssueContentBuilder WithBody(string body)
    {
        _body = body;
        return this;
    }

    public IssueContentBuilder WithComments(IEnumerable<IssueContentComment> comments)
    {
        _comments = [.. comments];
        return this;
    }

    public IssueContentBuilder WithComment(string body)
    {
        _comments.Add(new IssueContentComment(
            CommentId: _comments.Count + 1,
            Author: "octocat",
            Body: body,
            CreatedAt: FirstCommentAt.AddMinutes(_comments.Count)));
        return this;
    }

    public IssueContentBuilder WithSpecHash(SpecHash specHash)
    {
        _specHash = specHash;
        return this;
    }

    public IssueContent Build() => new(_title, _body, [.. _comments], _specHash);
}
