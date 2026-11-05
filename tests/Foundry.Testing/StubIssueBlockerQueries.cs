using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// <see cref="IIssueBlockerQueries"/> stub that reports no open blockers unless a per-issue result
/// is configured, and records each issue number it was asked about.
/// </summary>
public sealed class StubIssueBlockerQueries : IIssueBlockerQueries
{
    private readonly Dictionary<int, Result<IReadOnlyList<int>>> _resultsByIssueNumber = [];
    private readonly List<int> _requestedIssueNumbers = [];

    public IReadOnlyList<int> RequestedIssueNumbers => _requestedIssueNumbers;

    public StubIssueBlockerQueries WithBlockers(int issueNumber, params int[] blockers)
    {
        _resultsByIssueNumber[issueNumber] = Result<IReadOnlyList<int>>.Ok(blockers);
        return this;
    }

    public StubIssueBlockerQueries WithFailure(int issueNumber)
    {
        _resultsByIssueNumber[issueNumber] = Result<IReadOnlyList<int>>.Fail(
            new Error("Stub.BlockerReadFailed", $"Blocker read failed for issue {issueNumber}."));
        return this;
    }

    public Task<Result<IReadOnlyList<int>>> GetOpenBlockersAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        _requestedIssueNumbers.Add(issueNumber);

        Result<IReadOnlyList<int>> result = _resultsByIssueNumber.TryGetValue(
            issueNumber,
            out Result<IReadOnlyList<int>>? configured)
            ? configured
            : Result<IReadOnlyList<int>>.Ok([]);

        return Task.FromResult(result);
    }
}
