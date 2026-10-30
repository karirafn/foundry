using System.Text.Json.Serialization;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.ValueObjects;

public sealed record WorkerImageConfiguration
{
    private const string TrueValue = "true";
    private const string FalseValue = "false";
    private const string LatestTag = ":latest";
    private const string DefaultImageTag = "foundry-worker:local";

    [JsonConstructor]
    private WorkerImageConfiguration(
        string imageTag,
        bool buildEnabled,
        bool installDotnet,
        bool installAngular,
        bool installChromium,
        bool installDocker)
    {
        ImageTag = imageTag;
        BuildEnabled = buildEnabled;
        InstallDotnet = installDotnet;
        InstallAngular = installAngular;
        InstallChromium = installChromium;
        InstallDocker = installDocker;
    }

    public static readonly WorkerImageConfiguration Default = Create(
        imageTag: DefaultImageTag,
        buildEnabled: true,
        installDotnet: false,
        installAngular: false,
        installChromium: false,
        installDocker: false)
        .Match(
            onSuccess: v => v,
            onFailure: e => throw new InvalidOperationException($"WorkerImageConfiguration.Default is invalid: {e.Message}"));

    public string ImageTag { get; }

    public bool BuildEnabled { get; }

    public bool InstallDotnet { get; }

    public bool InstallAngular { get; }

    public bool InstallChromium { get; }

    public bool InstallDocker { get; }

    public static Result<WorkerImageConfiguration> Create(
        string imageTag,
        bool buildEnabled,
        bool installDotnet,
        bool installAngular,
        bool installChromium,
        bool installDocker)
    {
        if (string.IsNullOrWhiteSpace(imageTag))
        {
            return Result<WorkerImageConfiguration>.Fail(SettingsErrors.EmptyImageTag);
        }

        if (imageTag.Equals(LatestTag, StringComparison.OrdinalIgnoreCase))
        {
            return Result<WorkerImageConfiguration>.Fail(SettingsErrors.LatestImageTag);
        }

        return Result<WorkerImageConfiguration>.Ok(new WorkerImageConfiguration(
            imageTag,
            buildEnabled,
            installDotnet,
            installAngular,
            installChromium,
            installDocker));
    }

    public IReadOnlyDictionary<string, string> ToBuildArgs() =>
        new Dictionary<string, string>
        {
            ["INSTALL_DOTNET"] = InstallDotnet ? TrueValue : FalseValue,
            ["INSTALL_ANGULAR"] = InstallAngular ? TrueValue : FalseValue,
            ["INSTALL_CHROMIUM"] = InstallChromium ? TrueValue : FalseValue,
            ["INSTALL_DOCKER"] = InstallDocker ? TrueValue : FalseValue,
        };
}
