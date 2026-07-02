using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsMapperTests;

public sealed class ToSummary
{
    private static GlobalSettings CreateDefaultSettings()
    {
        return GlobalSettings.Create();
    }

    [Fact]
    public void WhenDefaultSettings_WorkerImageFlagsAreAllFalse()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.InstallDotnet.ShouldBeFalse(),
            () => result.InstallAngular.ShouldBeFalse(),
            () => result.InstallGlab.ShouldBeFalse(),
            () => result.InstallGh.ShouldBeFalse(),
            () => result.InstallChromium.ShouldBeFalse(),
            () => result.InstallDocker.ShouldBeFalse());
    }

    [Fact]
    public void WhenInstallDotnetIsTrue_SummaryReflectsIt()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.UpdateWorkerImageConfiguration(new WorkerImageConfiguration(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false));

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.InstallDotnet.ShouldBeTrue();
    }

    [Fact]
    public void WhenAllFlagsEnabled_SummaryReflectsAllFlags()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.UpdateWorkerImageConfiguration(new WorkerImageConfiguration(
            InstallDotnet: true,
            InstallAngular: true,
            InstallGlab: true,
            InstallGh: true,
            InstallChromium: true,
            InstallDocker: true));

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.InstallDotnet.ShouldBeTrue(),
            () => result.InstallAngular.ShouldBeTrue(),
            () => result.InstallGlab.ShouldBeTrue(),
            () => result.InstallGh.ShouldBeTrue(),
            () => result.InstallChromium.ShouldBeTrue(),
            () => result.InstallDocker.ShouldBeTrue());
    }

    [Fact]
    public void WhenDefaultSettings_ImageBuildStatusIsIdle()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

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
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.ImageBuildStatus.ShouldBe(ImageBuildStatus.Building);
    }

    [Fact]
    public void WhenBuildFailed_ImageBuildStatusIsFailedAndErrorIsSet()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();
        settings.FailImageBuild("Something went wrong");

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

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
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.LastImageBuildError.ShouldBeNull();
    }

    [Fact]
    public void WhenBuildCompleted_ImageBuildStatusIsIdleAndErrorIsNull()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.BeginImageBuild();
        settings.FailImageBuild("transient error");
        settings.BeginImageBuild();
        settings.CompleteImageBuild();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

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
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

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
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

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
        settings.FailImageBuild("new error");

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.HasUsableImage.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyMode_OAuthStatusIsNotConfigured()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.SetAuthMode(new AuthMode.ApiKey("my-key"));

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.OAuthStatus.ShouldBe(GlobalSettingsMapper.OAuthStatusNotConfigured);
    }

    [Fact]
    public void WhenOAuthModeAndCredentialPresent_OAuthStatusIsPresent()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.SetAuthMode(new AuthMode.OAuth("pro"));
        CredentialVolumeStatus credentialStatus = new(Present: true, ExpiresAt: null, SubscriptionType: "pro");

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus);

        // Assert
        result.OAuthStatus.ShouldBe(GlobalSettingsMapper.OAuthStatusPresent);
    }

    [Fact]
    public void WhenOAuthModeAndCredentialAbsent_OAuthStatusIsReLoginNeeded()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.SetAuthMode(new AuthMode.OAuth("pro"));
        CredentialVolumeStatus credentialStatus = new(Present: false, ExpiresAt: null, SubscriptionType: null);

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus);

        // Assert
        result.OAuthStatus.ShouldBe(GlobalSettingsMapper.OAuthStatusReLoginNeeded);
    }

    [Fact]
    public void WhenOAuthModeAndAuthInvalidPause_OAuthStatusIsReLoginNeeded()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.SetAuthMode(new AuthMode.OAuth("pro"));
        settings.PauseForAuthInvalid();
        CredentialVolumeStatus credentialStatus = new(Present: true, ExpiresAt: null, SubscriptionType: "pro");

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus);

        // Assert
        result.OAuthStatus.ShouldBe(GlobalSettingsMapper.OAuthStatusReLoginNeeded);
    }

    [Fact]
    public void WhenOAuthModeAndCredentialNull_OAuthStatusIsReLoginNeeded()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.SetAuthMode(new AuthMode.OAuth("pro"));

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.OAuthStatus.ShouldBe(GlobalSettingsMapper.OAuthStatusReLoginNeeded);
    }

    [Fact]
    public void WhenCredentialPresentWithExpiry_ExpiresAtIsSet()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.SetAuthMode(new AuthMode.OAuth("pro"));
        DateTimeOffset expiry = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        CredentialVolumeStatus credentialStatus = new(Present: true, ExpiresAt: expiry, SubscriptionType: "pro");

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus);

        // Assert
        result.ExpiresAt.ShouldBe(expiry);
    }

    [Fact]
    public void WhenCredentialPresentWithSubscriptionType_SubscriptionTypeIsFromVolume()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.SetAuthMode(new AuthMode.OAuth("pro"));
        CredentialVolumeStatus credentialStatus = new(Present: true, ExpiresAt: null, SubscriptionType: "max");

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus);

        // Assert
        result.SubscriptionType.ShouldBe("max");
    }

    [Fact]
    public void WhenCredentialAbsent_SubscriptionTypeFallsBackToStored()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.SetAuthMode(new AuthMode.OAuth("pro"));

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.SubscriptionType.ShouldBe("pro");
    }

    [Fact]
    public void WhenAccountIdentityNotSet_OAuthAccountEmailIsNull()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.OAuthAccountEmail.ShouldBeNull();
    }

    [Fact]
    public void WhenAccountIdentityNotSet_OAuthAccountOrgNameIsNull()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.OAuthAccountOrgName.ShouldBeNull();
    }

    [Fact]
    public void WhenAccountIdentitySet_OAuthAccountEmailIsMapped()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.SetOAuthAccountIdentity("user@example.com", "MyOrg", "pro");

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.OAuthAccountEmail.ShouldBe("user@example.com");
    }

    [Fact]
    public void WhenAccountIdentitySet_OAuthAccountOrgNameIsMapped()
    {
        // Arrange
        GlobalSettings settings = CreateDefaultSettings();
        settings.SetOAuthAccountIdentity("user@example.com", "MyOrg", "pro");

        // Act
        GlobalSettingsSummary result = GlobalSettingsMapper.ToSummary(settings, credentialStatus: null);

        // Assert
        result.OAuthAccountOrgName.ShouldBe("MyOrg");
    }
}
