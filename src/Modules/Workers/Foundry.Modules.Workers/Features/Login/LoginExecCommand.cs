namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// Builds the <c>docker exec</c> argv for delivering the OAuth code
/// into the login container's FIFO.
/// </summary>
/// <remarks>
/// The code is delivered via the exec's <c>C</c> environment variable — it never appears
/// in the exec process's argv and is therefore not interpolated into the shell command string.
/// The code is briefly visible via <c>docker inspect</c> / <c>GET /exec/{id}/json</c> during
/// the ~millisecond delivery window; this is an accepted residual risk bounded by the
/// Docker-socket trust boundary (an attacker able to inspect the exec already controls the
/// daemon and can read the credential volume directly). Cross-platform stream-stdin delivery
/// is not viable: Docker.DotNet's <c>MultiplexedStream.WriteAsync</c> does not land on the
/// Windows npipe transport.
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
    /// Creates the exec command argv that writes the code (from the <c>C</c> env var) into
    /// the container FIFO, then kills the bootstrap sleep process so the CLI receives EOF
    /// and can proceed. The code is passed via <c>Env = ["C=&lt;code&gt;"]</c> on the exec
    /// parameters — it is never interpolated into the shell command string.
    /// </summary>
    internal static LoginExecCommand ForCode(string code) =>
        new(
        [
            "sh",
            "-c",
            $"printf '%s\\n' \"$C\" > {FifoPath}; kill $(cat {SleepPidPath} 2>/dev/null) 2>/dev/null || true",
        ]);
}
