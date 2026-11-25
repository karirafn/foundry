namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Why an account token fails Foundry's requirements as a whole, before any repository is checked.
/// <see cref="WrongKind"/> names a token kind Foundry refuses, such as <c>github-classic</c>.
/// <see cref="NotAllowed"/> lists permissions the token holds but must not, and <see cref="BroadReach"/>
/// the wider-than-selected reaches it has.
/// </summary>
public sealed record NonConformingTokenSummary(
    string? WrongKind,
    IReadOnlyList<string> NotAllowed,
    IReadOnlyList<string> BroadReach);
