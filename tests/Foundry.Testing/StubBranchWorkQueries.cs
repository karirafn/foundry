using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;

namespace Foundry.Testing;

/// <summary>
/// <see cref="IPostExitProviderQueries"/> stub for the claim-time branch check: every branch reads as absent (a
/// <see cref="ErrorKind.NotFound"/> failure, as a provider compare answers 404 for a missing branch) unless a result
/// is configured for it. Records each branch it was asked about. The other reads, which the check never makes, fail.
/// </summary>
public sealed class StubBranchWorkQueries : IPostExitProviderQueries
{
    private readonly Dictionary<string, Result<BranchCommitSummary>> _resultsByBranch = [];
    private readonly List<string> _requestedBranches = [];

    public IReadOnlyList<string> RequestedBranches => _requestedBranches;

    public StubBranchWorkQueries WithCommitsAhead(string branchName, int commitCount)
    {
        _resultsByBranch[branchName] = Result<BranchCommitSummary>.Ok(
            new BranchCommitSummary(commitCount, commitCount > 0 ? "0123456789abcdef0123456789abcdef01234567" : null));
        return this;
    }

    public StubBranchWorkQueries WithFailure(string branchName)
    {
        _resultsByBranch[branchName] = Result<BranchCommitSummary>.Fail(
            new Error("Stub.BranchReadFailed", $"Branch read failed for {branchName}."));
        return this;
    }

    public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken)
    {
        _requestedBranches.Add(branchName);

        Result<BranchCommitSummary> result = _resultsByBranch.TryGetValue(
            branchName,
            out Result<BranchCommitSummary>? configured)
            ? configured
            : Result<BranchCommitSummary>.Fail(
                new Error("Stub.BranchNotFound", $"Branch {branchName} does not exist.") { Kind = ErrorKind.NotFound });

        return Task.FromResult(result);
    }

    public Task<Result<bool>> CreateBranchAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            Result<bool>.Fail(new Error("Stub.NotConfigured", "This stub only answers branch commit reads.")));

    public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            Result<MergeRequestByBranch>.Fail(new Error("Stub.NotConfigured", "This stub only answers branch commit reads.")));
}
