using Foundry.Shared;

namespace Foundry.Modules.Credentials.Contracts;

/// <summary>
/// Reads the credential workers use so Foundry itself can call the Anthropic API with it. In OAuth mode the access
/// token is read from the shared credential volume through the credential-helper container; an expired token is
/// refreshed by the Claude Code CLI first, exactly as a worker would.
/// </summary>
public interface IClaudeApiCredentialReader
{
    Task<Result<ClaudeApiCredential>> ReadAsync(CancellationToken cancellationToken);
}
