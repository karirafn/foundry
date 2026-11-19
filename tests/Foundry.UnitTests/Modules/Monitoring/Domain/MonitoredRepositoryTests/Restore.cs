using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class Restore
{
    private const string CurrentStamp = "current-stamp";
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static MonitoredRepository CreateRepository() =>
        MonitoredRepository.Create(
                RepositorySlug.Create("octocat/hello-world").ValueOrThrow(),
                "github.com",
                TimeSpan.FromSeconds(60),
                position: 3,
                maxConcurrentWorkers: 2,
                applyBranchRules: false,
                rebaseAfterRun: true,
                mergeWhenChecksPass: false)
            .ValueOrThrow();

    private static MonitoredRepository CreateRemovedRepositoryWithOperationalState()
    {
        MonitoredRepository repository = CreateRepository();
        repository.SetEligibility(new RepositoryEligibility.Eligible());
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(Now));
        repository.MarkWebhookRegistered(CurrentStamp);
        repository.RecordWebhookDelivery();
        repository.RecordWebhookRegistrationFailed(Now);
        repository.MarkPolled(Now);
        repository.SuppressUntracking(Now);
        repository.RecordRootListing(new RootNuGetConfig.Invalid("sha", "<config", "not xml"), "etag");
        repository.Update(TimeSpan.FromSeconds(60), isActive: false, maxConcurrentWorkers: 2);
        repository.SetReviewPullRequests(false);
        repository.Remove(Now);
        return repository;
    }

    private static Result RestoreWithDefaults(MonitoredRepository repository, int position = 7) =>
        repository.Restore(
            TimeSpan.FromSeconds(300),
            position,
            maxConcurrentWorkers: 4,
            applyBranchRules: true,
            rebaseAfterRun: false,
            mergeWhenChecksPass: true,
            reviewPullRequests: true);

    [Fact]
    public void ClearsRemovedAt()
    {
        // Arrange
        MonitoredRepository repository = CreateRemovedRepositoryWithOperationalState();

        // Act
        Result result = RestoreWithDefaults(repository);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        repository.RemovedAt.ShouldBeNull();
    }

    [Fact]
    public void AppliesTheAddRequestsSettings()
    {
        // Arrange
        MonitoredRepository repository = CreateRemovedRepositoryWithOperationalState();

        // Act
        RestoreWithDefaults(repository, position: 7);

        // Assert
        repository.PollInterval.ShouldBe(TimeSpan.FromSeconds(300));
        repository.Position.ShouldBe(7);
        repository.MaxConcurrentWorkers.ShouldBe(4);
        repository.ApplyBranchRules.ShouldBeTrue();
        repository.RebaseAfterRun.ShouldBeFalse();
        repository.MergeWhenChecksPass.ShouldBeTrue();
        repository.ReviewPullRequests.ShouldBeTrue();
        repository.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void ResetsOperationalStateToWhatCreateProduces()
    {
        // Arrange
        MonitoredRepository repository = CreateRemovedRepositoryWithOperationalState();

        // Act
        RestoreWithDefaults(repository);

        // Assert
        repository.Eligibility.ShouldBeOfType<RepositoryEligibility.Unreachable>();
        repository.EligibilityStatus.ShouldBe("unreachable");
        repository.WriteProbeVerdict.ShouldBeOfType<WriteProbeVerdict.Unknown>();
        repository.WebhookStatus.ShouldBe(Foundry.Modules.Monitoring.Domain.ValueObjects.WebhookStatus.NotRegistered);
        repository.WebhookSpecStamp.ShouldBeNull();
        repository.WebhookRegistrationFailedAt.ShouldBeNull();
        repository.LastPolledAt.ShouldBeNull();
        repository.UntrackSuppressedSince.ShouldBeNull();
        repository.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Absent>();
        repository.DeclaredPackageSources.ShouldBeEmpty();
        repository.RootListingETag.ShouldBeNull();
    }

    [Fact]
    public void MakesTheRepositoryDueForWebhookRegistration()
    {
        // Arrange
        MonitoredRepository repository = CreateRemovedRepositoryWithOperationalState();

        // Act
        RestoreWithDefaults(repository);

        // Assert
        repository.IsDueForWebhookRegistration(CurrentStamp, Now).ShouldBeTrue();
    }

    [Fact]
    public void KeepsIdentityAndWebhookCredentials()
    {
        // Arrange
        MonitoredRepository repository = CreateRemovedRepositoryWithOperationalState();
        MonitoredRepositoryId id = repository.Id;
        string hookId = repository.WebhookHookId;
        string secret = repository.EnsureWebhookSecret();

        // Act
        RestoreWithDefaults(repository);

        // Assert
        repository.Id.ShouldBe(id);
        repository.Slug.ToString().ShouldBe("octocat/hello-world");
        repository.Host.ShouldBe("github.com");
        repository.WebhookHookId.ShouldBe(hookId);
        repository.EnsureWebhookSecret().ShouldBe(secret);
    }

    [Fact]
    public void WhenNotRemoved_Fails()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        Result result = RestoreWithDefaults(repository);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(MonitoredRepositoryErrors.NotRemovedCode);
        repository.PollInterval.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void WhenMaxConcurrentWorkersIsOutOfRange_FailsAndStaysRemoved()
    {
        // Arrange
        MonitoredRepository repository = CreateRemovedRepositoryWithOperationalState();

        // Act
        Result result = repository.Restore(null, 0, maxConcurrentWorkers: 0, true, true, false, true);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code
            .ShouldBe(MonitoredRepositoryErrors.InvalidMaxConcurrentWorkersCode);
        repository.RemovedAt.ShouldBe(Now);
    }

    [Fact]
    public void WhenPollIntervalIsNotPositive_FailsAndStaysRemoved()
    {
        // Arrange
        MonitoredRepository repository = CreateRemovedRepositoryWithOperationalState();

        // Act
        Result result = repository.Restore(TimeSpan.Zero, 0, 1, true, true, false, true);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(MonitoredRepositoryErrors.PollIntervalNotPositiveCode);
        repository.RemovedAt.ShouldBe(Now);
    }
}
