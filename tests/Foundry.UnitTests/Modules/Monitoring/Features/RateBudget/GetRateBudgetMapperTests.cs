using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.RateBudget;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.RateBudget;

public sealed class GetRateBudgetMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenAllBudgetsHaveFreshReadings_ReturnsMappedEntriesWithCorrectHealth()
    {
        // Arrange
        InMemoryProviderRateBudget store = new();
        RateBudgetReading restReading = new RateBudgetReadingBuilder()
            .WithRemaining(1000)
            .WithLimit(5000)
            .WithObservedAt(Now)
            .Build();
        RateBudgetReading graphQlReading = new RateBudgetReadingBuilder()
            .WithRemaining(400)
            .WithLimit(5000)
            .WithObservedAt(Now)
            .Build();
        RateBudgetReading gitLabReading = new RateBudgetReadingBuilder()
            .WithRemaining(200)
            .WithLimit(1000)
            .WithObservedAt(Now)
            .Build();

        store.Record(ProviderBudgetKey.GitHubRest, restReading);
        store.Record(ProviderBudgetKey.GitHubGraphQl, graphQlReading);
        store.Record(ProviderBudgetKey.GitLabRest, gitLabReading);

        // Act
        RateBudgetSnapshot snapshot = GetRateBudgetMapper.Map(store, Now);

        // Assert
        snapshot.Budgets.Count.ShouldBe(3);

        ProviderBudgetHeadroom restEntry = snapshot.Budgets.Single(b => b.Budget == "GitHubRest");
        restEntry.ShouldSatisfyAllConditions(
            () => restEntry.DisplayName.ShouldBe("GitHub REST"),
            () => restEntry.Remaining.ShouldBe(1000),
            () => restEntry.Limit.ShouldBe(5000),
            () => restEntry.Health.ShouldBe("Healthy"),
            () => restEntry.Floor.ShouldBe(ProviderBudgetPolicy.DefaultFloor));

        ProviderBudgetHeadroom graphQlEntry = snapshot.Budgets.Single(b => b.Budget == "GitHubGraphQl");
        graphQlEntry.ShouldSatisfyAllConditions(
            () => graphQlEntry.DisplayName.ShouldBe("GitHub GraphQL"),
            () => graphQlEntry.Remaining.ShouldBe(400),
            () => graphQlEntry.Health.ShouldBe("Low"),
            () => graphQlEntry.Floor.ShouldBe(ProviderBudgetPolicy.DefaultFloor));

        ProviderBudgetHeadroom gitLabEntry = snapshot.Budgets.Single(b => b.Budget == "GitLabRest");
        gitLabEntry.ShouldSatisfyAllConditions(
            () => gitLabEntry.DisplayName.ShouldBe("GitLab REST"),
            () => gitLabEntry.Remaining.ShouldBe(200),
            () => gitLabEntry.Health.ShouldBeNull(),
            () => gitLabEntry.Floor.ShouldBeNull());
    }

    [Fact]
    public void WhenGitHubKeyHasNoReading_EmitsHealthUnknownAndDefaultFloor()
    {
        // Arrange
        InMemoryProviderRateBudget store = new();

        // Act
        RateBudgetSnapshot snapshot = GetRateBudgetMapper.Map(store, Now);

        // Assert
        ProviderBudgetHeadroom restEntry = snapshot.Budgets.Single(b => b.Budget == "GitHubRest");
        restEntry.ShouldSatisfyAllConditions(
            () => restEntry.Health.ShouldBe("Unknown"),
            () => restEntry.Remaining.ShouldBeNull(),
            () => restEntry.Limit.ShouldBeNull(),
            () => restEntry.ResetAt.ShouldBeNull(),
            () => restEntry.ObservedAt.ShouldBeNull(),
            () => restEntry.Floor.ShouldBe(ProviderBudgetPolicy.DefaultFloor));
    }

    [Fact]
    public void WhenGitHubReadingIsStale_EmitsHealthUnknown()
    {
        // Arrange
        InMemoryProviderRateBudget store = new();
        DateTimeOffset staleObservation = Now - RateBudgetReading.StalenessWindow - TimeSpan.FromSeconds(1);
        RateBudgetReading staleReading = new RateBudgetReadingBuilder()
            .WithRemaining(0)
            .WithObservedAt(staleObservation)
            .Build();
        store.Record(ProviderBudgetKey.GitHubRest, staleReading);

        // Act
        RateBudgetSnapshot snapshot = GetRateBudgetMapper.Map(store, Now);

        // Assert
        ProviderBudgetHeadroom restEntry = snapshot.Budgets.Single(b => b.Budget == "GitHubRest");
        restEntry.Health.ShouldBe("Unknown");
    }

    [Fact]
    public void WhenGitHubReadingIsBelowFloor_EmitsHealthLow()
    {
        // Arrange
        InMemoryProviderRateBudget store = new();
        RateBudgetReading lowReading = new RateBudgetReadingBuilder()
            .WithRemaining(ProviderBudgetPolicy.DefaultFloor - 1)
            .WithObservedAt(Now)
            .Build();
        store.Record(ProviderBudgetKey.GitHubRest, lowReading);

        // Act
        RateBudgetSnapshot snapshot = GetRateBudgetMapper.Map(store, Now);

        // Assert
        ProviderBudgetHeadroom restEntry = snapshot.Budgets.Single(b => b.Budget == "GitHubRest");
        restEntry.Health.ShouldBe("Low");
    }

    [Fact]
    public void WhenGitHubRestIsLow_DoesNotAffectGitHubGraphQlHealth()
    {
        // Arrange
        InMemoryProviderRateBudget store = new();
        RateBudgetReading lowRestReading = new RateBudgetReadingBuilder()
            .WithRemaining(ProviderBudgetPolicy.DefaultFloor - 1)
            .WithObservedAt(Now)
            .Build();
        RateBudgetReading healthyGraphQlReading = new RateBudgetReadingBuilder()
            .WithRemaining(ProviderBudgetPolicy.DefaultFloor + 100)
            .WithObservedAt(Now)
            .Build();
        store.Record(ProviderBudgetKey.GitHubRest, lowRestReading);
        store.Record(ProviderBudgetKey.GitHubGraphQl, healthyGraphQlReading);

        // Act
        RateBudgetSnapshot snapshot = GetRateBudgetMapper.Map(store, Now);

        // Assert
        ProviderBudgetHeadroom restEntry = snapshot.Budgets.Single(b => b.Budget == "GitHubRest");
        ProviderBudgetHeadroom graphQlEntry = snapshot.Budgets.Single(b => b.Budget == "GitHubGraphQl");
        restEntry.Health.ShouldBe("Low");
        graphQlEntry.Health.ShouldBe("Healthy");
    }

    [Fact]
    public void WhenMapped_AlwaysEmitsThreeBudgetsInDeterministicOrder()
    {
        // Arrange
        InMemoryProviderRateBudget store = new();

        // Act
        RateBudgetSnapshot snapshot = GetRateBudgetMapper.Map(store, Now);

        // Assert
        snapshot.Budgets.Count.ShouldBe(3);
        snapshot.Budgets[0].Budget.ShouldBe("GitHubRest");
        snapshot.Budgets[1].Budget.ShouldBe("GitHubGraphQl");
        snapshot.Budgets[2].Budget.ShouldBe("GitLabRest");
    }
}
