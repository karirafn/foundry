using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxMessageTests;

public sealed class PayloadRoundTrip
{
    [Fact]
    public void WhenEventCarriesStronglyTypedId_RoundTripsPayloadIntact()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.From(Guid.NewGuid());
        IssueDetected original = new(
            repoId,
            42,
            "Fix the bug",
            "Some body text",
            "octocat",
            "https://github.com/org/repo/issues/42",
            ["bug", "claude"],
            "claude",
            new DateTimeOffset(2025, 6, 1, 10, 0, 0, TimeSpan.Zero));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        OutboxMessage message = OutboxMessage.Create(original, now);
        IssueDetected? deserialized = JsonSerializer.Deserialize<IssueDetected>(
            message.Payload,
            OutboxSerializerOptions.Default);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.ShouldSatisfyAllConditions(
            () => deserialized.MonitoredRepositoryId.ShouldBe(repoId),
            () => deserialized.IssueNumber.ShouldBe(42),
            () => deserialized.Title.ShouldBe("Fix the bug"),
            () => deserialized.Author.ShouldBe("octocat"),
            () => deserialized.Labels.Count.ShouldBe(2));
    }

    [Fact]
    public void WhenEventCarriesNestedStronglyTypedIds_RoundTripsPayloadIntact()
    {
        // Arrange
        IssueId issueId = IssueId.From(Guid.NewGuid());
        MonitoredRepositoryId repoId = MonitoredRepositoryId.From(Guid.NewGuid());
        ClaimedIssueDispatch dispatch = new(
            issueId,
            Guid.NewGuid(),
            7,
            "Implement feature",
            "Add support for X",
            "org/repo",
            new Uri("https://github.com/org/repo.git"),
            null,
            "feat/7-implement-feature",
            repoId,
            new WorkerProvider.GitHub());
        IssueClaimed original = new(dispatch);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        OutboxMessage message = OutboxMessage.Create(original, now);
        IssueClaimed? deserialized = JsonSerializer.Deserialize<IssueClaimed>(
            message.Payload,
            OutboxSerializerOptions.Default);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.ShouldSatisfyAllConditions(
            () => deserialized.Dispatch.IssueId.ShouldBe(issueId),
            () => deserialized.Dispatch.MonitoredRepositoryId.ShouldBe(repoId),
            () => deserialized.Dispatch.IssueNumber.ShouldBe(7),
            () => deserialized.Dispatch.BranchName.ShouldBe("feat/7-implement-feature"));
    }
}
