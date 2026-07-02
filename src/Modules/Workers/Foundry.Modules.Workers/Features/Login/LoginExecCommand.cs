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

    private const string EnvVarName = "C";

    /// <summary>
    /// Creates the exec command that writes <paramref name="code"/> into the container FIFO.
    /// </summary>
    internal static LoginExecCommand ForCode(string code)
    {
        IReadOnlyList<string> argv =
        [
            "sh",
            "-c",
            $"printf '%s\\n' \"${EnvVarName}\" > {FifoPath}",
        ];

        IReadOnlyList<string> env = [$"{EnvVarName}={code}"];

        return new LoginExecCommand(argv, env);
    }
}
