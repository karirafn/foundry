using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.MergeRequestSelectionTests;

public sealed class Select
{
    private static readonly CommitSha PublishedHead = CommitSha.From(new string('a', 40));
    private static readonly CommitSha OtherHead = CommitSha.From(new string('b', 40));
    private static readonly DateTimeOffset Earlier = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddHours(1);

    private static MergeRequestCandidate Candidate(
        MergeRequestPresence presence,
        string url,
        CommitSha? head,
        DateTimeOffset updatedAt) => new(presence, url, updatedAt, head);

    [Fact]
    public void WhenNoCandidates_ReturnsNone()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select([], PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.None, null));
    }

    [Fact]
    public void WhenOnlyClosedCandidateHeadDiffers_ReturnsNone()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [Candidate(MergeRequestPresence.Closed, "closed", OtherHead, Earlier)],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.None, null));
    }

    [Fact]
    public void WhenOnlyMergedCandidateHeadDiffers_ReturnsNone()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [Candidate(MergeRequestPresence.Merged, "merged", OtherHead, Earlier)],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.None, null));
    }

    [Fact]
    public void WhenClosedCandidateHeadMatches_ReturnsClosed()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [Candidate(MergeRequestPresence.Closed, "closed", PublishedHead, Earlier)],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Closed, "closed"));
    }

    [Fact]
    public void WhenMergedCandidateHeadMatches_ReturnsMerged()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [Candidate(MergeRequestPresence.Merged, "merged", PublishedHead, Earlier)],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Merged, "merged"));
    }

    [Fact]
    public void WhenOpenCandidateHeadDiffers_ReturnsOpen()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [Candidate(MergeRequestPresence.Open, "open", OtherHead, Earlier)],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Open, "open"));
    }

    [Fact]
    public void WhenOpenCandidateHasNoHead_ReturnsOpen()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [Candidate(MergeRequestPresence.Open, "open", null, Earlier)],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Open, "open"));
    }

    [Fact]
    public void WhenClosedCandidateHasNoHead_ReturnsNone()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [Candidate(MergeRequestPresence.Closed, "closed", null, Earlier)],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.None, null));
    }

    [Fact]
    public void WhenOpenAndStaleMerged_ReturnsOpen()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [
                Candidate(MergeRequestPresence.Merged, "merged", OtherHead, Earlier),
                Candidate(MergeRequestPresence.Open, "open", OtherHead, Later),
            ],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Open, "open"));
    }

    [Fact]
    public void WhenSeveralOpen_ReturnsMostRecentlyUpdatedOpen()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [
                Candidate(MergeRequestPresence.Open, "older", OtherHead, Earlier),
                Candidate(MergeRequestPresence.Open, "newer", OtherHead, Later),
            ],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Open, "newer"));
    }

    [Fact]
    public void WhenMatchingMergedAndOpen_ReturnsMerged()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [
                Candidate(MergeRequestPresence.Open, "open", OtherHead, Later),
                Candidate(MergeRequestPresence.Merged, "merged", PublishedHead, Earlier),
            ],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Merged, "merged"));
    }

    [Fact]
    public void WhenOpenAndMatchingClosed_ReturnsOpen()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [
                Candidate(MergeRequestPresence.Closed, "closed", PublishedHead, Later),
                Candidate(MergeRequestPresence.Open, "open", OtherHead, Earlier),
            ],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Open, "open"));
    }

    [Fact]
    public void WhenSeveralClosedAndOneMatches_ReturnsMatchingClosed()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [
                Candidate(MergeRequestPresence.Closed, "stale", OtherHead, Later),
                Candidate(MergeRequestPresence.Closed, "matching", PublishedHead, Earlier),
            ],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Closed, "matching"));
    }

    [Fact]
    public void WhenStaleMergedListedBeforeMatchingClosed_ReturnsMatchingClosed()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [
                Candidate(MergeRequestPresence.Merged, "stale-merged", OtherHead, Later),
                Candidate(MergeRequestPresence.Closed, "matching-closed", PublishedHead, Earlier),
            ],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Closed, "matching-closed"));
    }

    [Fact]
    public void WhenMatchingClosedListedBeforeStaleMerged_ReturnsMatchingClosed()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [
                Candidate(MergeRequestPresence.Closed, "matching-closed", PublishedHead, Later),
                Candidate(MergeRequestPresence.Merged, "stale-merged", OtherHead, Earlier),
            ],
            PublishedHead);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Closed, "matching-closed"));
    }

    [Fact]
    public void WhenPublishedHeadIsNullAndClosedHeadDiffers_ReturnsClosed()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [Candidate(MergeRequestPresence.Closed, "closed", OtherHead, Earlier)],
            publishedHead: null);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Closed, "closed"));
    }

    [Fact]
    public void WhenPublishedHeadIsNull_PrefersMergedOverNewerOpen()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [
                Candidate(MergeRequestPresence.Open, "open", OtherHead, Later),
                Candidate(MergeRequestPresence.Merged, "merged", OtherHead, Earlier),
            ],
            publishedHead: null);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Merged, "merged"));
    }

    [Fact]
    public void WhenPublishedHeadIsNullAndNoMerged_ReturnsMostRecentlyUpdated()
    {
        MergeRequestByBranch result = MergeRequestSelection.Select(
            [
                Candidate(MergeRequestPresence.Open, "open", OtherHead, Earlier),
                Candidate(MergeRequestPresence.Closed, "closed", OtherHead, Later),
            ],
            publishedHead: null);

        result.ShouldBe(new MergeRequestByBranch(MergeRequestPresence.Closed, "closed"));
    }
}
