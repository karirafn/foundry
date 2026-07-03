namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// Builds the <c>docker exec</c> argv for delivering the OAuth code
/// into the login container's FIFO.
/// </summary>
/// <remarks>
/// The code is written to the exec's STDIN stream — it never appears in the exec
/// process's argv or environment variables and is therefore not visible via
/// <c>docker inspect</c> or the Docker API's <c>GET /exec/{id}/json</c> endpoint.
/// </remarks>
internal sealed record LoginExecCommand
{
    /// <summary>Path to the FIFO inside the login container that receives the OAuth code.</summary>
    internal const string FifoPath = "/tmp/ci";

    /// <summary>
    /// Path to the file that holds the PID of the sleep process keeping the FIFO writer open.
    /// Written by the bootstrap command; read here to kill the process after delivering the code,
    /// so the CLI receives EOF on stdin and can proceed to token exchange without waiting.
    /// </summary>
    internal const string SleepPidPath = "/tmp/ci.pid";

    private LoginExecCommand(IReadOnlyList<string> argv)
    {
        Argv = argv;
    }

    internal IReadOnlyList<string> Argv { get; }

    /// <summary>
    /// Creates the exec command argv that reads from STDIN, writes into the container FIFO,
    /// then kills the bootstrap sleep process so the CLI receives EOF and can proceed.
    /// The code must be written to the exec's STDIN by the caller.
    /// </summary>
    internal static LoginExecCommand ForStdin() =>
        new(
        [
            "sh",
            "-c",
            $"cat > {FifoPath}; kill $(cat {SleepPidPath} 2>/dev/null) 2>/dev/null || true",
        ]);
}
