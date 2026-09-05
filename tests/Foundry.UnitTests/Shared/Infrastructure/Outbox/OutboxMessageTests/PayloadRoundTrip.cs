using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
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
            WorkerRunId.New(),
            7,
            "Implement feature",
            
            "org/repo",
            new Uri("https://github.com/org/repo.git"),
            null,
            BranchName.From("feat/7-implement-feature"),
            repoId,
            new WorkerProvider.GitHub(),
            new DispatchContext.Fresh("feat/7-implement-feature"),
            "https://api.github.com/repos/org/repo/issues/7");
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
            () => deserialized.Dispatch.BranchName.ShouldBe(BranchName.From("feat/7-implement-feature")),
            () => deserialized.Dispatch.Provider.ShouldBeOfType<WorkerProvider.GitHub>(),
            () => deserialized.Dispatch.Context.ShouldBeOfType<DispatchContext.Fresh>());
    }

    [Fact]
    public void WhenDispatchContextIsFresh_RoundTripsContextVariantIntact()
    {
        // Arrange
        IssueId issueId = IssueId.From(Guid.NewGuid());
        MonitoredRepositoryId repoId = MonitoredRepositoryId.From(Guid.NewGuid());
        DispatchContext.Fresh context = new("feat/7-implement-feature");
        ClaimedIssueDispatch dispatch = new(
            issueId,
            WorkerRunId.New(),
            7,
            "Implement feature",
            
            "org/repo",
            new Uri("https://github.com/org/repo.git"),
            null,
            BranchName.From("feat/7-implement-feature"),
            repoId,
            new WorkerProvider.GitHub(),
            context,
            "https://api.github.com/repos/org/repo/issues/7");
        IssueClaimed original = new(dispatch);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        OutboxMessage message = OutboxMessage.Create(original, now);
        IssueClaimed? deserialized = JsonSerializer.Deserialize<IssueClaimed>(
            message.Payload,
            OutboxSerializerOptions.Default);

        // Assert
        deserialized.ShouldNotBeNull();
        DispatchContext.Fresh fresh = deserialized.Dispatch.Context.ShouldBeOfType<DispatchContext.Fresh>();
        fresh.BranchName.ShouldBe("feat/7-implement-feature");
    }

    [Fact]
    public void WhenDispatchContextIsRevision_RoundTripsContextVariantIntact()
    {
        // Arrange
        IssueId issueId = IssueId.From(Guid.NewGuid());
        MonitoredRepositoryId repoId = MonitoredRepositoryId.From(Guid.NewGuid());
        DispatchContext.Revision context = new(
            "feat/7-implement-feature",
            "https://github.com/org/repo/pull/7",
            [new ReviewComment("Please add tests.", "src/Service.cs", Line: 42)]);
        ClaimedIssueDispatch dispatch = new(
            issueId,
            WorkerRunId.New(),
            7,
            "Implement feature",
            
            "org/repo",
            new Uri("https://github.com/org/repo.git"),
            null,
            BranchName.From("feat/7-implement-feature"),
            repoId,
            new WorkerProvider.GitHub(),
            context,
            "https://api.github.com/repos/org/repo/issues/7");
        IssueClaimed original = new(dispatch);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        OutboxMessage message = OutboxMessage.Create(original, now);
        IssueClaimed? deserialized = JsonSerializer.Deserialize<IssueClaimed>(
            message.Payload,
            OutboxSerializerOptions.Default);

        // Assert
        deserialized.ShouldNotBeNull();
        DispatchContext.Revision revision = deserialized.Dispatch.Context.ShouldBeOfType<DispatchContext.Revision>();
        revision.ShouldSatisfyAllConditions(
            () => revision.BranchName.ShouldBe("feat/7-implement-feature"),
            () => revision.PullRequestUrl.ShouldBe("https://github.com/org/repo/pull/7"),
            () => revision.Comments.Count.ShouldBe(1),
            () => revision.Comments[0].Body.ShouldBe("Please add tests."),
            () => revision.Comments[0].FilePath.ShouldBe("src/Service.cs"),
            () => revision.Comments[0].Line.ShouldBe(42));
    }

    [Fact]
    public void WhenDispatchContextIsContinuation_RoundTripsContextVariantIntact()
    {
        // Arrange
        IssueId issueId = IssueId.From(Guid.NewGuid());
        MonitoredRepositoryId repoId = MonitoredRepositoryId.From(Guid.NewGuid());
        DispatchContext.Continuation context = new("feat/7-implement-feature", "Build failed: missing semicolon");
        ClaimedIssueDispatch dispatch = new(
            issueId,
            WorkerRunId.New(),
            7,
            "Implement feature",
            
            "org/repo",
            new Uri("https://github.com/org/repo.git"),
            null,
            BranchName.From("feat/7-implement-feature"),
            repoId,
            new WorkerProvider.GitHub(),
            context,
            "https://api.github.com/repos/org/repo/issues/7");
        IssueClaimed original = new(dispatch);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        OutboxMessage message = OutboxMessage.Create(original, now);
        IssueClaimed? deserialized = JsonSerializer.Deserialize<IssueClaimed>(
            message.Payload,
            OutboxSerializerOptions.Default);

        // Assert
        deserialized.ShouldNotBeNull();
        DispatchContext.Continuation continuation = deserialized.Dispatch.Context.ShouldBeOfType<DispatchContext.Continuation>();
        continuation.ShouldSatisfyAllConditions(
            () => continuation.BranchName.ShouldBe("feat/7-implement-feature"),
            () => continuation.FailureReason.ShouldBe("Build failed: missing semicolon"));
    }

    [Fact]
    public void WhenLegacyIssueDetectedPayloadContainsBodyField_DeserializesWithoutErrorAndSurvivingFieldsIntact()
    {
        // Arrange — simulate a pre-migration outbox_messages row that carried a "Body" field.
        // MonitoredRepositoryId serializes as {"Value":"<guid>"} by STJ (struct with a Guid property).
        // STJ ignores unknown members ("Body") by default; this test asserts that guarantee
        // holds for the exact path the relay uses (OutboxSerializerOptions.Default).
        MonitoredRepositoryId repoId = MonitoredRepositoryId.From(Guid.NewGuid());
        string legacyJson = $$"""
            {
                "MonitoredRepositoryId": {"Value": "{{repoId.Value}}"},
                "IssueNumber": 99,
                "Title": "Fix the bug",
                "Body": "Legacy body text that no longer belongs here",
                "Author": "octocat",
                "Url": "https://github.com/org/repo/issues/99",
                "Labels": ["bug", "claude"],
                "IssueKindLabel": "claude",
                "DetectedAt": "2025-06-01T10:00:00+00:00"
            }
            """;

        // Act
        IssueDetected? deserialized = JsonSerializer.Deserialize<IssueDetected>(
            legacyJson,
            OutboxSerializerOptions.Default);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.ShouldSatisfyAllConditions(
            () => deserialized.MonitoredRepositoryId.ShouldBe(repoId),
            () => deserialized.IssueNumber.ShouldBe(99),
            () => deserialized.Title.ShouldBe("Fix the bug"),
            () => deserialized.Author.ShouldBe("octocat"),
            () => deserialized.Labels.Count.ShouldBe(2));
    }

    [Fact]
    public void WhenLegacyIssueDetailsChangedPayloadContainsBodyField_DeserializesWithoutErrorAndSurvivingFieldsIntact()
    {
        // Arrange — simulate a pre-migration outbox_messages row for IssueDetailsChanged with a "Body" field.
        // MonitoredRepositoryId serializes as {"Value":"<guid>"} by STJ (struct with a Guid property).
        // STJ ignores the unknown "Body" property and deserializes the remaining fields intact.
        MonitoredRepositoryId repoId = MonitoredRepositoryId.From(Guid.NewGuid());
        string legacyJson = $$"""
            {
                "MonitoredRepositoryId": {"Value": "{{repoId.Value}}"},
                "IssueNumber": 42,
                "Title": "Updated title",
                "Body": "Old body text that is no longer stored",
                "Labels": ["foundry", "bug"]
            }
            """;

        // Act
        IssueDetailsChanged? deserialized = JsonSerializer.Deserialize<IssueDetailsChanged>(
            legacyJson,
            OutboxSerializerOptions.Default);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized.ShouldSatisfyAllConditions(
            () => deserialized.MonitoredRepositoryId.ShouldBe(repoId),
            () => deserialized.IssueNumber.ShouldBe(42),
            () => deserialized.Title.ShouldBe("Updated title"),
            () => deserialized.Labels.Count.ShouldBe(2));
    }
}
