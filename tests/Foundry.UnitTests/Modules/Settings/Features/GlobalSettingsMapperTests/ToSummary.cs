using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Testing;

using Shouldly;

using Xunit;

using Foundry.UnitTests.Modules.Settings.Features;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsMapperTests;

public sealed class ToSummary
{
    private static readonly DispatchPauseState Unpaused = new(null, false, []);

    private static GlobalSettings CreateDefaultSettings()
    {
        return GlobalSettings.Create();
    }

    [Fact]
    public void WhenAHostIsPaused_SummaryCarriesItsPausedHost()
    {
        // Arrange
        DateTimeOffset pausedAt = new(2026, 11, 13, 7, 39, 0, TimeSpan.Zero);
        PausedHost pausedHost = new("gitlab.com", pausedAt, 0, pausedAt.AddMinutes(1));
        DispatchPauseState pauseState = new(null, false, [pausedHost]);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(CreateDefaultSettings(), pauseState, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.PausedHosts.ShouldBe([new PausedHost("gitlab.com", pausedAt, 0, pausedAt.AddMinutes(1))]);
    }

    [Fact]
    public void WhenNoHostIsPaused_SummaryHasNoPausedHosts()
    {
        GlobalSettingsMapper.ToSummary(CreateDefaultSettings(), Unpaused, StubWebhookIngressProbe.DefaultPort).PausedHosts.ShouldBeEmpty();
    }

    [Fact]
    public void WhenThePackageProxyIsPaused_SummaryCarriesThePause()
    {
        // Arrange
        DateTimeOffset pausedAt = new(2026, 11, 19, 7, 39, 0, TimeSpan.Zero);
        DispatchPauseState pauseState = new(null, false, [], new PausedPackageProxy(pausedAt, 0, pausedAt.AddMinutes(1)));

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(CreateDefaultSettings(), pauseState, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.PackageProxyPause.ShouldBe(new PausedPackageProxy(pausedAt, 0, pausedAt.AddMinutes(1)));
    }

    [Fact]
    public void WhenThePackageProxyIsNotPaused_SummaryHasNoPackageProxyPause()
    {
        GlobalSettingsMapper.ToSummary(CreateDefaultSettings(), Unpaused, StubWebhookIngressProbe.DefaultPort).PackageProxyPause.ShouldBeNull();
    }

    [Fact]
    public void WhenDispatchIsPausedForAUsageLimit_SummaryCarriesBothPauseReasons()
    {
        // Arrange
        DateTimeOffset resetsAt = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
        DispatchPauseState pauseState = new(resetsAt, true, []);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(CreateDefaultSettings(), pauseState, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsDispatchPaused.ShouldBeTrue(),
            () => result.UsageLimitResetsAt.ShouldBe(resetsAt));
    }

    [Fact]
    public void WhenDefaultSettings_NestedDockerIsOff()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.NestedDockerEnabled.ShouldBeFalse();
    }

    [Fact]
    public void WhenNestedDockerIsOn_SummaryReflectsIt()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.UpdateNestedDocker(true);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.NestedDockerEnabled.ShouldBeTrue();
    }

    [Fact]
    public void WhenDefaultSettings_ImageBuildStatusIsIdle()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.ImageBuildStatus.ShouldBe(ImageBuildStatus.Idle);
    }

    [Fact]
    public void WhenBuildStarted_ImageBuildStatusIsBuilding()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.ImageBuildStatus.ShouldBe(ImageBuildStatus.Building);
    }

    [Fact]
    public void WhenBuildFailed_ImageBuildStatusIsFailedAndErrorIsSet()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();
        settings.FailImageBuild("Something went wrong", nextRetryAt: null, attempt: 0);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ImageBuildStatus.ShouldBe(ImageBuildStatus.Failed),
            () => result.LastImageBuildError.ShouldBe("Something went wrong"));
    }

    [Fact]
    public void WhenDefaultSettings_LastImageBuildErrorIsNull()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.LastImageBuildError.ShouldBeNull();
    }

    [Fact]
    public void WhenBuildCompleted_ImageBuildStatusIsIdleAndErrorIsNull()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();
        settings.FailImageBuild("transient error", nextRetryAt: null, attempt: 0);
        settings.BeginImageBuild();
        settings.CompleteImageBuild();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ImageBuildStatus.ShouldBe(ImageBuildStatus.Idle),
            () => result.LastImageBuildError.ShouldBeNull());
    }

    [Fact]
    public void WhenNeverBuilt_HasUsableImageIsFalse()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.HasUsableImage.ShouldBeFalse();
    }

    [Fact]
    public void WhenBuildCompletedSuccessfully_HasUsableImageIsTrue()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();
        settings.CompleteImageBuild();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.HasUsableImage.ShouldBeTrue();
    }

    [Fact]
    public void WhenBuildFailedAfterPriorSuccess_HasUsableImageRemainsTrue()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();
        settings.CompleteImageBuild();
        settings.BeginImageBuild();
        settings.FailImageBuild("new error", nextRetryAt: null, attempt: 0);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.HasUsableImage.ShouldBeTrue();
    }

    [Fact]
    public void WhenBuildFailedWithNextRetryAt_SummaryExposesNextRetryAt()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();
        DateTimeOffset nextRetryAt = DateTimeOffset.UtcNow.AddSeconds(30);
        settings.FailImageBuild("error", nextRetryAt, attempt: 1);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.NextRetryAt.ShouldBe(nextRetryAt);
    }

    [Fact]
    public void WhenBuildFailedWithAttempt_SummaryExposesAttempt()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();
        settings.FailImageBuild("error", nextRetryAt: null, attempt: 3);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.Attempt.ShouldBe(3);
    }

    [Fact]
    public void WhenStatusIsIdle_NextRetryAtIsNull()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.NextRetryAt.ShouldBeNull();
    }

    [Fact]
    public void WhenStatusIsIdle_AttemptIsZero()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.Attempt.ShouldBe(0);
    }

    [Fact]
    public void WhenStatusIsBuilding_NextRetryAtIsNull()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.NextRetryAt.ShouldBeNull();
    }

    [Fact]
    public void WhenStatusIsBuilding_AttemptIsZero()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.Attempt.ShouldBe(0);
    }

    [Fact]
    public void WhenDefaultSettings_AllowedProviderHostsIsEmpty()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.AllowedProviderHosts.ShouldBeEmpty();
    }

    [Fact]
    public void WhenAllowedProviderHostsSet_SummaryReflectsHosts()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.UpdateAllowedProviderHosts(["github.com", "gitlab.com"]);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.AllowedProviderHosts.ShouldBe(["github.com", "gitlab.com"]);
    }

    [Fact]
    public void WhenDefaultSettings_ProbeIntervalMinutesIsDefault()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.ProbeIntervalMinutes.ShouldBe(GlobalSettings.DefaultProbeIntervalMinutes);
    }

    [Fact]
    public void WhenProbeIntervalUpdated_SummaryReflectsNewValue()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.UpdateProbeInterval(15);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.ProbeIntervalMinutes.ShouldBe(15);
    }

    [Fact]
    public void WhenDefaultSettings_PollIntervalSecondsIsDefault()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.PollIntervalSeconds.ShouldBe(GlobalSettings.DefaultPollIntervalSeconds);
    }

    [Fact]
    public void WhenPollIntervalUpdated_SummaryReflectsNewValue()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.UpdatePollInterval(120);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.PollIntervalSeconds.ShouldBe(120);
    }

    [Fact]
    public void WhenWorkerEnvironmentUpdated_SummaryCarriesLimits()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        WorkerResourceLimits limits = WorkerResourceLimits.Create(4096, 1.5, 256)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));
        settings.UpdateWorkerEnvironment(limits);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.MemoryLimitMb.ShouldBe(4096),
            () => result.CpuLimit.ShouldBe(1.5),
            () => result.PidsLimit.ShouldBe(256));
    }

    [Fact]
    public void WhenDefaultSettings_SummaryCarriesDefaultLimits()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.MemoryLimitMb.ShouldBe(WorkerResourceLimits.Default.MemoryLimitMb),
            () => result.CpuLimit.ShouldBe(WorkerResourceLimits.Default.CpuLimit),
            () => result.PidsLimit.ShouldBe(WorkerResourceLimits.Default.PidsLimit));
    }

    [Fact]
    public void WhenClaudeCodeSettingsUpdated_SummaryCarriesThem()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        ClaudeCodeSettings claudeCode = new ClaudeCodeSettingsBuilder()
            .WithAdditionalDenyRules(["Bash(rm:*)"])
            .Build()
            .Match(v => v, e => throw new InvalidOperationException(e.Message));
        settings.UpdateClaudeCodeSettings(claudeCode);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, Unpaused, StubWebhookIngressProbe.DefaultPort);

        // Assert
        result.AdditionalDenyRules.ShouldBe(settings.ClaudeCodeSettings.AdditionalDenyRules);
    }
}
