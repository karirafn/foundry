using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

/// <summary>What <c>GET /personal_access_tokens/self</c> told about a GitLab token.</summary>
internal abstract record GitLabTokenSelf
{
    private GitLabTokenSelf() { }

    /// <summary>A fine-grained token, which reported its grant.</summary>
    internal sealed record FineGrained(GitLabTokenGrant Grant, TokenExpiry Expiry) : GitLabTokenSelf;

    /// <summary>A token of a kind Foundry refuses.</summary>
    internal sealed record OfWrongKind(WrongTokenKind Kind, TokenExpiry Expiry) : GitLabTokenSelf;

    /// <summary>A fine-grained token lacking the user permission to read its own record.</summary>
    internal sealed record CannotReadItself : GitLabTokenSelf;
}
