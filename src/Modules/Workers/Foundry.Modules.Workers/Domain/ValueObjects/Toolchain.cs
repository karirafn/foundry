namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>One tool a worker image carries, as <c>workers/toolchain.sh</c> reports it (ADR 0109).</summary>
public sealed record ToolchainEntry
{
    public const int MaxNameLength = 64;

    public const int MaxVersionLength = 128;

    private ToolchainEntry(string name, string version)
    {
        Name = name;
        Version = version;
    }

    /// <summary>The tool's stable name, such as <c>csharp-ls</c> or <c>docker</c>.</summary>
    public string Name { get; }

    public string Version { get; }

    /// <summary>The entry, or <see langword="null"/> when the name or version is empty or too long.</summary>
    public static ToolchainEntry? TryCreate(string? name, string? version)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return null;
        }

        if (string.IsNullOrEmpty(version) || version.Length > MaxVersionLength)
        {
            return null;
        }

        return new ToolchainEntry(name, version);
    }
}

/// <summary>
/// The toolchain a worker image carries, in the order the image reported it (ADR 0109). Prompt sections that depend on
/// a tool are included only when the image's recorded toolchain has it.
/// </summary>
public sealed class Toolchain
{
    /// <summary>More than the script reports today, so a new tool fits; few, so a runaway report is rejected.</summary>
    public const int MaxEntries = 32;

    private Toolchain(IReadOnlyList<ToolchainEntry> entries)
    {
        Entries = entries;
    }

    /// <summary>
    /// The toolchain of an image with no recorded list: it has no tool, so every toolchain-dependent section is left
    /// out rather than promised on a guess.
    /// </summary>
    public static Toolchain Unrecorded { get; } = new([]);

    public IReadOnlyList<ToolchainEntry> Entries { get; }

    /// <summary>The toolchain, or <see langword="null"/> when it has more than <see cref="MaxEntries"/> entries.</summary>
    public static Toolchain? TryCreate(IReadOnlyCollection<ToolchainEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries.Count > MaxEntries ? null : new Toolchain([.. entries]);
    }

    /// <summary>Whether the image carries the tool named exactly <paramref name="name"/>.</summary>
    public bool Has(string name) => Entries.Any(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));
}
