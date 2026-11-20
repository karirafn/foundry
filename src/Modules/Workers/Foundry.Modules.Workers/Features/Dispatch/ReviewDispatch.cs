using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>
/// What a claimed review (ADR 0114) asks of its dispatch: the reviewed commit Prepare bundles and diffs, and the pull
/// request whose facts the run records, and whether the checks gate passed on that commit. Read from <see cref="DispatchContext.Review"/>, never from the role's name.
/// </summary>
internal sealed record ReviewDispatch(ReviewTarget Target, PullRequestUrl PullRequestUrl, bool ChecksPassed)
{
    /// <summary>The variable that tells the entrypoint which commit to check out read-only.</summary>
    public const string ReviewCommitVariable = "REVIEW_COMMIT";

    /// <summary>A malformed branch, commit or pull request URL fails here, before anything is prepared.</summary>
    public static Result<ReviewDispatch> From(DispatchContext.Review review)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (string.IsNullOrWhiteSpace(review.BranchName))
        {
            return Invalid("its branch is empty");
        }

        if (!CommitSha.TryFrom(review.ReviewedCommit, out CommitSha reviewedCommit))
        {
            return Invalid($"its reviewed commit '{review.ReviewedCommit}' is not a commit SHA");
        }

        if (!Uri.TryCreate(review.PullRequestUrl, UriKind.Absolute, out _))
        {
            return Invalid($"its pull request URL '{review.PullRequestUrl}' is not an absolute URL");
        }

        return new ReviewDispatch(
            new ReviewTarget(BranchName.From(review.BranchName), reviewedCommit),
            PullRequestUrl.From(review.PullRequestUrl),
            review.ChecksPassed);
    }

    /// <summary>
    /// The facts the run records and its prompt shows, from the packet Prepare computed and the issue body read at
    /// dispatch. A prepared review without a packet fails: its verdict could not be checked against anything.
    /// </summary>
    public Result<SuppliedReviewFacts> FactsFrom(PrepareOutcome.Prepared prepared, string? issueBody)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        return prepared.ReviewPacket is { } packet
            ? SuppliedReviewFacts.Compute(PullRequestUrl, Target.Commit, packet, issueBody, ChecksPassed)
            : Result<SuppliedReviewFacts>.Fail(new Error(
                "Worker.MissingReviewPacket",
                "The Publisher prepared the review's base without its review packet."));
    }

    private static Result<ReviewDispatch> Invalid(string detail) =>
        Result<ReviewDispatch>.Fail(new Error("Worker.InvalidReviewTarget", $"The review was claimed with an invalid target: {detail}."));
}
