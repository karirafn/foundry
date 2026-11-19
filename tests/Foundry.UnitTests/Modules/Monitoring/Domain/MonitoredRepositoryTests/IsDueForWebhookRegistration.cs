using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class IsDueForWebhookRegistration
{
    private const string CurrentStamp = "current-stamp";
    private const string OldStamp = "old-stamp";
    private static readonly DateTimeOffset FailedAt = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static MonitoredRepository CreateRepository() =>
        MonitoredRepository.Create(RepositorySlug.Create("octocat/hello-world").ValueOrThrow(), "github.com", null)
            .ValueOrThrow();

    [Fact]
    public void WhenNeverRegistered_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        bool due = repository.IsDueForWebhookRegistration(CurrentStamp, FailedAt);

        // Assert
        due.ShouldBeTrue();
    }

    [Fact]
    public void WhenRegisteredWithTheCurrentStamp_ReturnsFalse()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.MarkWebhookRegistered(CurrentStamp);

        // Act
        bool due = repository.IsDueForWebhookRegistration(CurrentStamp, FailedAt);

        // Assert
        due.ShouldBeFalse();
    }

    [Fact]
    public void WhenRegisteredWithAnOldStamp_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.MarkWebhookRegistered(OldStamp);

        // Act
        bool due = repository.IsDueForWebhookRegistration(CurrentStamp, FailedAt);

        // Assert
        due.ShouldBeTrue();
    }

    [Fact]
    public void WhenWriteProbeVerdictIsDenied_ReturnsFalse()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(FailedAt));

        // Act
        bool due = repository.IsDueForWebhookRegistration(CurrentStamp, FailedAt);

        // Assert
        due.ShouldBeFalse();
    }

    [Fact]
    public void WhenWriteProbeVerdictTurnsGranted_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Denied(FailedAt));
        repository.SetWriteProbeVerdict(new WriteProbeVerdict.Granted());

        // Act
        bool due = repository.IsDueForWebhookRegistration(CurrentStamp, FailedAt);

        // Assert
        due.ShouldBeTrue();
    }

    [Fact]
    public void WhenRegistrationFailedHalfAnHourAgo_ReturnsFalse()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.RecordWebhookRegistrationFailed(FailedAt);

        // Act
        bool due = repository.IsDueForWebhookRegistration(CurrentStamp, FailedAt.AddMinutes(30));

        // Assert
        due.ShouldBeFalse();
    }

    [Fact]
    public void WhenRegistrationFailedExactlyAnHourAgo_ReturnsFalse()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.RecordWebhookRegistrationFailed(FailedAt);

        // Act
        bool due = repository.IsDueForWebhookRegistration(CurrentStamp, FailedAt.AddHours(1));

        // Assert
        due.ShouldBeFalse();
    }

    [Fact]
    public void WhenRegistrationFailedMoreThanAnHourAgo_ReturnsTrue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.RecordWebhookRegistrationFailed(FailedAt);

        // Act
        bool due = repository.IsDueForWebhookRegistration(CurrentStamp, FailedAt.AddHours(1).AddSeconds(1));

        // Assert
        due.ShouldBeTrue();
    }

    [Fact]
    public void WhenRegisteredAfterAFailure_StoresTheStampAndClearsTheFailure()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.RecordWebhookRegistrationFailed(FailedAt);

        // Act
        repository.MarkWebhookRegistered(CurrentStamp);

        // Assert
        repository.WebhookSpecStamp.ShouldBe(CurrentStamp);
        repository.WebhookRegistrationFailedAt.ShouldBeNull();
    }

    [Fact]
    public void WhenRegistrationFails_KeepsTheStampAndRecordsTheAttempt()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.MarkWebhookRegistered(OldStamp);

        // Act
        repository.RecordWebhookRegistrationFailed(FailedAt);

        // Assert
        repository.WebhookSpecStamp.ShouldBe(OldStamp);
        repository.WebhookRegistrationFailedAt.ShouldBe(FailedAt);
    }

    [Fact]
    public void WhenMarkedAddedManually_BecomesPendingWithoutAStamp()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        repository.MarkWebhookAddedManually();

        // Assert
        repository.WebhookStatus.ShouldBe(Foundry.Modules.Monitoring.Domain.ValueObjects.WebhookStatus.Pending);
        repository.WebhookSpecStamp.ShouldBeNull();
    }
}
