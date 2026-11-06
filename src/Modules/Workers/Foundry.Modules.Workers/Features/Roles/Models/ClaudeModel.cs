namespace Foundry.Modules.Workers.Features.Roles.Models;

/// <summary>A model listed by the Anthropic Models API (<c>GET /v1/models</c>).</summary>
/// <param name="Id">The model ID, as passed to the CLI.</param>
/// <param name="DisplayName">The human-readable name.</param>
/// <param name="Lifecycle">The model's lifecycle state (e.g. <c>active</c>, <c>deprecated</c>), when reported.</param>
internal sealed record ClaudeModel(string Id, string DisplayName, string? Lifecycle)
{
    internal const string DeprecatedLifecycle = "deprecated";

    public bool Deprecated => string.Equals(Lifecycle, DeprecatedLifecycle, StringComparison.OrdinalIgnoreCase);
}
