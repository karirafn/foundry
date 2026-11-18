using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ReviewFactsTests;

public sealed class ProductionChangedWithoutTests
{
    private const string Production = "src/App/Service.cs";
    private const string Test = "tests/App.Tests/ServiceTests.cs";
    private const string Other = "docs/guide.md";

    public static TheoryData<ChangedPath[], bool> Cases => new()
    {
        { [new(ChangeStatus.Modified, Production)], true },
        { [new(ChangeStatus.Added, Production)], true },
        { [new(ChangeStatus.Deleted, Production)], false },
        { [new(ChangeStatus.Modified, Production), new(ChangeStatus.Modified, Test)], false },
        { [new(ChangeStatus.Added, Production), new(ChangeStatus.Added, Test)], false },
        { [new(ChangeStatus.Modified, Production), new(ChangeStatus.Deleted, Test)], true },
        { [new(ChangeStatus.Modified, Test)], false },
        { [new(ChangeStatus.Modified, Other)], false },
        { [new(ChangeStatus.Modified, Production), new(ChangeStatus.Modified, Other)], true },
        { [], false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void IsSetExactlyWhenProductionIsAddedOrModifiedAndNoTestIs(ChangedPath[] paths, bool expected)
    {
        // Act
        ReviewFacts facts = ReviewFactsBuilder.With(paths);

        // Assert
        facts.ProductionChangedWithoutTests.ShouldBe(expected);
    }

    [Fact]
    public void ClassifiesEveryPath()
    {
        // Act
        ReviewFacts facts = ReviewFactsBuilder.With(
            [new(ChangeStatus.Modified, Production), new(ChangeStatus.Added, Test), new(ChangeStatus.Deleted, Other)]);

        // Assert
        facts.ClassifiedPaths.Select(path => path.Class).ShouldBe(
            [ChangedFileClass.Production, ChangedFileClass.Test, ChangedFileClass.Other]);
    }
}

internal static class ReviewFactsBuilder
{
    internal const string Commit = "0123456789abcdef0123456789abcdef01234567";

    public static ReviewFacts With(IReadOnlyList<ChangedPath> paths, AcceptanceCriteria? criteria = null) =>
        ReviewFacts.Compute(
            PullRequestUrl.From("https://github.com/acme/app/pull/7"),
            CommitSha.From(Commit),
            new ReviewPacketFacts(paths, paths.Count > 0 ? PatchId.From("abcdefabcdefabcdefabcdefabcdefabcdefabcd") : null),
            criteria ?? AcceptanceCriteria.Extract(string.Empty));
}
