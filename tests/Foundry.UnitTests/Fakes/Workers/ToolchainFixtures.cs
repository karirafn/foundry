using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>Recorded toolchains for prompt tests, holding the tools that gate prompt sections.</summary>
internal static class ToolchainFixtures
{
    /// <summary>
    /// A toolchain holding any of <c>docker</c>, <c>csharp-ls</c>, <c>chromium</c> and <c>playwright-core</c>, beside
    /// tools that gate nothing.
    /// </summary>
    public static Toolchain With(
        bool docker = false,
        bool csharpLs = false,
        bool chromium = false,
        bool playwrightCore = false)
    {
        List<ToolchainEntry> entries = [Entry("node", "22.22.3"), Entry("git", "2.39.5")];

        if (csharpLs)
        {
            entries.Add(Entry("dotnet-sdk", "10.0.404"));
            entries.Add(Entry("csharp-ls", "0.28.0"));
        }

        if (docker)
        {
            entries.Add(Entry("docker", "29.9.3"));
        }

        if (chromium)
        {
            entries.Add(Entry("chromium", "156.0.8078.79"));
        }

        if (playwrightCore)
        {
            entries.Add(Entry("playwright-core", "1.65.1"));
        }

        return Toolchain.TryCreate(entries)
            ?? throw new InvalidOperationException("The fixture toolchain must be valid.");
    }

    private static ToolchainEntry Entry(string name, string version) =>
        ToolchainEntry.TryCreate(name, version)
            ?? throw new InvalidOperationException($"The fixture entry {name} must be valid.");
}
