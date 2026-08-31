using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.RateBudget;

public sealed class InMemoryProviderRateBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenReadingRecorded_TryGetReturnsReading()
    {
        // Arrange
        InMemoryProviderRateBudget store = new();
        RateBudgetReading reading = new RateBudgetReadingBuilder()
            .WithRemaining(1000)
            .WithObservedAt(Now)
            .Build();

        // Act
        store.Record(ProviderBudgetKey.GitHubRest, reading);
        RateBudgetReading? result =
            store.TryGet(ProviderBudgetKey.GitHubRest);

        // Assert
        result.ShouldNotBeNull();
        result.Remaining.ShouldBe(reading.Remaining);
    }

    [Fact]
    public void WhenNothingRecorded_TryGetReturnsNull()
    {
        // Arrange
        InMemoryProviderRateBudget store = new();

        // Act
        RateBudgetReading? result =
            store.TryGet(ProviderBudgetKey.GitHubRest);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenRecordingUnderOneKey_OtherKeyRemainsEmpty()
    {
        // Arrange
        InMemoryProviderRateBudget store = new();
        RateBudgetReading reading = new RateBudgetReadingBuilder()
            .WithRemaining(1000)
            .WithObservedAt(Now)
            .Build();

        // Act
        store.Record(ProviderBudgetKey.GitHubRest, reading);
        RateBudgetReading? graphQlResult =
            store.TryGet(ProviderBudgetKey.GitHubGraphQl);

        // Assert
        graphQlResult.ShouldBeNull();
    }

    [Fact]
    public void WhenMultipleRecordingsForSameKey_LastWriterWins()
    {
        // Arrange
        InMemoryProviderRateBudget store = new();
        RateBudgetReading first = new RateBudgetReadingBuilder()
            .WithRemaining(1000)
            .WithObservedAt(Now)
            .Build();
        RateBudgetReading second = new RateBudgetReadingBuilder()
            .WithRemaining(500)
            .WithObservedAt(Now.AddMinutes(1))
            .Build();

        // Act
        store.Record(ProviderBudgetKey.GitHubRest, first);
        store.Record(ProviderBudgetKey.GitHubRest, second);
        RateBudgetReading? result =
            store.TryGet(ProviderBudgetKey.GitHubRest);

        // Assert
        result.ShouldNotBeNull();
        result.Remaining.ShouldBe(second.Remaining);
    }

    [Fact]
    public void WhenReadingsRecordedForAllKeys_SnapshotContainsAllEntries()
    {
        // Arrange
        InMemoryProviderRateBudget store = new();
        RateBudgetReading restReading = new RateBudgetReadingBuilder()
            .WithRemaining(1000)
            .WithObservedAt(Now)
            .Build();
        RateBudgetReading graphQlReading = new RateBudgetReadingBuilder()
            .WithRemaining(2000)
            .WithObservedAt(Now)
            .Build();

        // Act
        store.Record(ProviderBudgetKey.GitHubRest, restReading);
        store.Record(ProviderBudgetKey.GitHubGraphQl, graphQlReading);
        IReadOnlyDictionary<ProviderBudgetKey, RateBudgetReading> snapshot =
            store.Snapshot();

        // Assert
        snapshot.Count.ShouldBe(2);
        snapshot[ProviderBudgetKey.GitHubRest].Remaining.ShouldBe(restReading.Remaining);
        snapshot[ProviderBudgetKey.GitHubGraphQl].Remaining.ShouldBe(graphQlReading.Remaining);
    }

    [Fact]
    public void WhenNoReadingsRecorded_SnapshotIsEmpty()
    {
        // Arrange
        InMemoryProviderRateBudget store = new();

        // Act
        IReadOnlyDictionary<ProviderBudgetKey, RateBudgetReading> snapshot =
            store.Snapshot();

        // Assert
        snapshot.ShouldBeEmpty();
    }
}
