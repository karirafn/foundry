using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Testing;

public sealed class TrackerContextBuilder
{
    private IReadOnlyList<ReferencedIssue> _referencedIssues = [];
    private IReadOnlyList<int> _unreadableIssueNumbers = [];
    private bool _linksUnreadable;
    private IReadOnlyList<OpenIssue>? _openIssues = [];

    public TrackerContextBuilder WithReferencedIssues(IEnumerable<ReferencedIssue> value)
    {
        _referencedIssues = [.. value];
        return this;
    }

    public TrackerContextBuilder WithReferencedIssue(int number, bool isOpen)
    {
        _referencedIssues =
        [
            .. _referencedIssues,
            new ReferencedIssue(
                Number: number,
                Relation: IssueRelation.RelatesTo,
                Title: $"Referenced issue {number}",
                IsOpen: isOpen,
                Labels: [],
                ClosingMergeRequest: ClosingMergeRequestState.None,
                Body: string.Empty),
        ];
        return this;
    }

    public TrackerContextBuilder WithUnreadableIssueNumbers(IEnumerable<int> value)
    {
        _unreadableIssueNumbers = [.. value];
        return this;
    }

    public TrackerContextBuilder WithLinksUnreadable(bool value)
    {
        _linksUnreadable = value;
        return this;
    }

    public TrackerContextBuilder WithOpenIssues(IEnumerable<OpenIssue>? value)
    {
        _openIssues = value is null ? null : [.. value];
        return this;
    }

    public TrackerContextBuilder WithOpenIssueNumbers(params int[] numbers)
    {
        _openIssues = [.. numbers.Select(number => new OpenIssue(number, $"Open issue {number}"))];
        return this;
    }

    public TrackerContextBuilder WithUnreadableIndex()
    {
        _openIssues = null;
        return this;
    }

    public TrackerContext Build() => new(_referencedIssues, _unreadableIssueNumbers, _linksUnreadable, _openIssues);
}
