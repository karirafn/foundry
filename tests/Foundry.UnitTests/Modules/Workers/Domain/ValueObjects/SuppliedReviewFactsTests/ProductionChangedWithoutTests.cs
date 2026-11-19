using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.SuppliedReviewFactsTests;

public sealed class ProductionChangedWithoutTests
{
    private static readonly PullRequestUrl Url = PullRequestUrl.From("https://github.com/acme/app/pull/7");
    private static readonly CommitSha Commit = CommitSha.From("0123456789abcdef0123456789abcdef01234567");

    private static SuppliedReviewFacts FactsFor(params (ChangeStatus Status, string Path)[] paths) =>
        SuppliedReviewFacts.Compute(
            Url,
            Commit,
            new ReviewPacketFacts([.. paths.Select(path => new ChangedPath(path.Status, path.Path))], patchId: null),
            issueBody: null);

    [Theory]
    [InlineData(ChangeStatus.Added)]
    [InlineData(ChangeStatus.Modified)]
    public void WhenProductionIsAddedOrModifiedAndNoTestIs_IsSet(ChangeStatus status)
    {
        // Act / Assert
        FactsFor((status, "src/a.cs"), (ChangeStatus.Modified, "README.md")).ProductionChangedWithoutTests.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ChangeStatus.Added)]
    [InlineData(ChangeStatus.Modified)]
    public void WhenATestIsAddedOrModified_IsNotSet(ChangeStatus status)
    {
        // Act / Assert
        FactsFor((ChangeStatus.Modified, "src/a.cs"), (status, "tests/a_test.go")).ProductionChangedWithoutTests.ShouldBeFalse();
    }

    [Fact]
    public void WhenTheOnlyTestChangeIsADeletion_IsSet()
    {
        // Act / Assert
        FactsFor((ChangeStatus.Modified, "src/a.cs"), (ChangeStatus.Deleted, "tests/a.spec.ts"))
            .ProductionChangedWithoutTests.ShouldBeTrue();
    }

    [Fact]
    public void WhenProductionIsOnlyDeleted_IsNotSet()
    {
        // Act / Assert
        FactsFor((ChangeStatus.Deleted, "src/a.cs"), (ChangeStatus.Modified, "docs/a.md")).ProductionChangedWithoutTests.ShouldBeFalse();
    }

    [Fact]
    public void WhenNoPathChanged_IsNotSet()
    {
        // Act / Assert
        FactsFor().ProductionChangedWithoutTests.ShouldBeFalse();
    }

    [Fact]
    public void WhenComputed_ClassesEveryPathAndExtractsTheCriteria()
    {
        // Act
        SuppliedReviewFacts facts = SuppliedReviewFacts.Compute(
            Url,
            Commit,
            new ReviewPacketFacts([new ChangedPath(ChangeStatus.Added, "src/a.cs"), new ChangedPath(ChangeStatus.Added, "a.test.ts")], patchId: null),
            "## Acceptance Criteria\n\n- One.\n");

        // Assert
        facts.ShouldSatisfyAllConditions(
            () => facts.ClassifiedPaths.Select(path => path.Class).ShouldBe([ChangedFileClass.Production, ChangedFileClass.Test]),
            () => facts.Criteria.ShouldBe([new AcceptanceCriterion(1, "One.")]));
    }

    [Fact]
    public void WhenCriteriaAreNotNumberedInOrder_Throws()
    {
        // Act / Assert
        Should.Throw<ArgumentException>(() => new SuppliedReviewFacts(Url, Commit, null, [], [new AcceptanceCriterion(2, "Two.")]));
    }
}
