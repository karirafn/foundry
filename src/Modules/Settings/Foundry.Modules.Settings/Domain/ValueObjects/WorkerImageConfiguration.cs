namespace Foundry.Modules.Settings.Domain.ValueObjects;

public sealed record WorkerImageConfiguration(
    bool InstallDotnet,
    bool InstallAngular,
    bool InstallChromium,
    bool InstallDocker)
{
    private const string TrueValue = "true";
    private const string FalseValue = "false";

    public static readonly WorkerImageConfiguration Default = new(
        InstallDotnet: false,
        InstallAngular: false,
        InstallChromium: false,
        InstallDocker: false);

    public IReadOnlyDictionary<string, string> ToBuildArgs() =>
        new Dictionary<string, string>
        {
            ["INSTALL_DOTNET"] = InstallDotnet ? TrueValue : FalseValue,
            ["INSTALL_ANGULAR"] = InstallAngular ? TrueValue : FalseValue,
            ["INSTALL_CHROMIUM"] = InstallChromium ? TrueValue : FalseValue,
            ["INSTALL_DOCKER"] = InstallDocker ? TrueValue : FalseValue,
        };
}
