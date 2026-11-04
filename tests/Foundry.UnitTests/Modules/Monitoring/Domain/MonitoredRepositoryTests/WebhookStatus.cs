using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

using WebhookStatusValue = Foundry.Modules.Monitoring.Domain.ValueObjects.WebhookStatus;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class WebhookStatus
{
    private static readonly TimeSpan NormalInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Backstop = TimeSpan.FromMinutes(15);
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static MonitoredRepository CreateRepository(TimeSpan? pollInterval = null) =>
        MonitoredRepository.Create(RepositorySlug.Create("octocat/hello-world").ValueOrThrow(), "github.com", pollInterval)
            .ValueOrThrow();

    private static MonitoredRepository CreateActiveRepository()
    {
        MonitoredRepository repository = CreateRepository();
        repository.MarkWebhookRegistered();
        repository.RecordWebhookDelivery();
        return repository;
    }

    [Fact]
    public void WhenCreated_HasNotRegisteredStatusRandomHookIdAndSecret()
    {
        // Act
        MonitoredRepository first = CreateRepository();
        MonitoredRepository second = CreateRepository();

        // Assert
        first.WebhookStatus.ShouldBe(WebhookStatusValue.NotRegistered);
        first.WebhookHookId.Length.ShouldBe(MonitoredRepository.WebhookHookIdLength);
        first.WebhookHookId.ShouldNotBe(second.WebhookHookId);
        first.WebhookSecret.ShouldNotBeNullOrEmpty();
        first.WebhookSecret.ShouldNotBe(second.WebhookSecret);
    }

    [Fact]
    public void WhenMarkedRegistered_BecomesPending()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        repository.MarkWebhookRegistered();

        // Assert
        repository.WebhookStatus.ShouldBe(WebhookStatusValue.Pending);
    }

    [Fact]
    public void WhenDeliveryArrivesWhilePending_BecomesActive()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.MarkWebhookRegistered();

        // Act
        bool changed = repository.RecordWebhookDelivery();

        // Assert
        changed.ShouldBeTrue();
        repository.WebhookStatus.ShouldBe(WebhookStatusValue.Active);
    }

    [Fact]
    public void WhenDeliveryArrivesWhileSilent_BecomesActive()
    {
        // Arrange
        MonitoredRepository repository = CreateActiveRepository();
        repository.RecordMissedChange();

        // Act
        bool changed = repository.RecordWebhookDelivery();

        // Assert
        changed.ShouldBeTrue();
        repository.WebhookStatus.ShouldBe(WebhookStatusValue.Active);
    }

    [Fact]
    public void WhenDeliveryArrivesWhileNotRegistered_StatusUnchanged()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        bool changed = repository.RecordWebhookDelivery();

        // Assert
        changed.ShouldBeFalse();
        repository.WebhookStatus.ShouldBe(WebhookStatusValue.NotRegistered);
    }

    [Fact]
    public void WhenMissedChangeWhileActive_BecomesSilent()
    {
        // Arrange
        MonitoredRepository repository = CreateActiveRepository();

        // Act
        bool changed = repository.RecordMissedChange();

        // Assert
        changed.ShouldBeTrue();
        repository.WebhookStatus.ShouldBe(WebhookStatusValue.Silent);
    }

    [Fact]
    public void WhenMissedChangeWhilePending_StatusUnchanged()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.MarkWebhookRegistered();

        // Act
        bool changed = repository.RecordMissedChange();

        // Assert
        changed.ShouldBeFalse();
        repository.WebhookStatus.ShouldBe(WebhookStatusValue.Pending);
    }

    [Fact]
    public void WhenMarkedRegisteredWhileActive_StaysActive()
    {
        // Arrange
        MonitoredRepository repository = CreateActiveRepository();

        // Act
        repository.MarkWebhookRegistered();

        // Assert
        repository.WebhookStatus.ShouldBe(WebhookStatusValue.Active);
    }

    [Fact]
    public void WhenIngressNotConfigured_EffectiveStatusIsNotConfigured()
    {
        // Arrange
        MonitoredRepository repository = CreateActiveRepository();

        // Act & Assert
        repository.EffectiveWebhookStatus(ingressConfigured: false).ShouldBe(WebhookStatusValue.NotConfigured);
        repository.EffectiveWebhookStatus(ingressConfigured: true).ShouldBe(WebhookStatusValue.Active);
    }

    [Fact]
    public void WhenActive_PollsAtBackstopInterval()
    {
        // Arrange
        MonitoredRepository repository = CreateActiveRepository();
        repository.MarkPolled(Now);

        // Act & Assert
        repository.IsDueForPoll(NormalInterval, Now + TimeSpan.FromMinutes(14), Backstop).ShouldBeFalse();
        repository.IsDueForPoll(NormalInterval, Now + TimeSpan.FromMinutes(16), Backstop).ShouldBeTrue();
    }

    [Theory]
    [InlineData(WebhookStatusValue.NotRegistered)]
    [InlineData(WebhookStatusValue.Pending)]
    [InlineData(WebhookStatusValue.Silent)]
    public void WhenNotActive_PollsAtNormalInterval(WebhookStatusValue status)
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        if (status is WebhookStatusValue.Pending or WebhookStatusValue.Silent)
        {
            repository.MarkWebhookRegistered();
        }

        if (status is WebhookStatusValue.Silent)
        {
            repository.RecordWebhookDelivery();
            repository.RecordMissedChange();
        }

        repository.MarkPolled(Now);

        // Act
        bool due = repository.IsDueForPoll(NormalInterval, Now + TimeSpan.FromSeconds(31), Backstop);

        // Assert
        repository.WebhookStatus.ShouldBe(status);
        due.ShouldBeTrue();
    }

    [Fact]
    public void WhenActiveButIngressNotConfigured_PollsAtNormalInterval()
    {
        // Arrange
        MonitoredRepository repository = CreateActiveRepository();
        repository.MarkPolled(Now);

        // Act
        bool due = repository.IsDueForPoll(NormalInterval, Now + TimeSpan.FromSeconds(31), backstopInterval: null);

        // Assert
        due.ShouldBeTrue();
    }

    [Fact]
    public void WhenActiveAndOwnIntervalExceedsBackstop_KeepsOwnInterval()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository(TimeSpan.FromHours(1));
        repository.MarkWebhookRegistered();
        repository.RecordWebhookDelivery();

        // Act
        TimeSpan interval = repository.ScheduledPollInterval(NormalInterval, Backstop);

        // Assert
        interval.ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void WhenSecretMissing_EnsureWebhookSecretGeneratesOnce()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        string first = repository.EnsureWebhookSecret();
        string second = repository.EnsureWebhookSecret();

        // Assert
        first.ShouldBe(second);
        first.Length.ShouldBe(64);
    }
}
