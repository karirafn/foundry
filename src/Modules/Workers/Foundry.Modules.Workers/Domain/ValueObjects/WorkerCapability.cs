namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The capability profile a Worker Role runs under (ADR 0098). The container spec, the entrypoint and the system
/// prompt branch on the profile, never on the role's name.
/// </summary>
public enum WorkerCapability
{
    /// <summary>
    /// A judgment role: no command tools (<c>claude --restricted --strict-mcp-config</c>, no permission skip), no
    /// provider token, no branch, and the issue content supplied by Foundry's code. It returns a verdict only.
    /// </summary>
    Restricted,

    /// <summary>An implementing role: command tools, the account token and its own branch on the provider.</summary>
    Full,
}

public static class WorkerCapabilityNames
{
    public const string Restricted = "restricted";
    public const string Full = "full";

    /// <summary>
    /// The lower-case name of <paramref name="capability"/>: the value of the entrypoint's <c>WORKER_CAPABILITY</c>
    /// and of the role card's <c>capability</c>.
    /// </summary>
    public static string ToName(this WorkerCapability capability) =>
        capability switch
        {
            WorkerCapability.Restricted => Restricted,
            WorkerCapability.Full => Full,
            _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown worker capability."),
        };
}
