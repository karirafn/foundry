namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// Builds the <c>docker exec</c> argv and environment for delivering the OAuth code
/// into the login container's FIFO via <c>printf</c>.
/// </summary>
/// <remarks>
/// The code is passed as environment variable <c>C</c> — never interpolated into the
/// command string — so shell metacharacters in the code cannot cause injection.
/// </remarks>
internal sealed record LoginExecCommand(IReadOnlyList<string> Argv, IReadOnlyList<string> Env)
{
    /// <summary>Path to the FIFO inside the login container that receives the OAuth code.</summary>
    internal const string FifoPath = "/tmp/ci";

    /// <summary>
    /// Path to the file that holds the PID of the sleep process keeping the FIFO writer open.
    /// Written by the bootstrap command; read here to kill the process after delivering the code,
    /// so the CLI receives EOF on stdin and can proceed to token exchange without waiting.
    /// </summary>
    internal const string SleepPidPath = "/tmp/ci.pid";

    private const string EnvVarName = "C";

    /// <summary>
    /// Creates the exec command that writes <paramref name="code"/> into the container FIFO,
    /// then kills the bootstrap sleep process so the CLI receives EOF on stdin and can proceed.
    /// </summary>
    internal static LoginExecCommand ForCode(string code)
    {
        IReadOnlyList<string> argv =
        [
            "sh",
            "-c",
            $"printf '%s\\n' \"${EnvVarName}\" > {FifoPath}; kill $(cat {SleepPidPath} 2>/dev/null) 2>/dev/null || true",
        ];

        IReadOnlyList<string> env = [$"{EnvVarName}={code}"];

        return new LoginExecCommand(argv, env);
    }
}
