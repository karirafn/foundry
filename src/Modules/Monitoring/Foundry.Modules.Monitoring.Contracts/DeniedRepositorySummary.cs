namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// A repository the account's token is denied on. <see cref="MissingPermissions"/> holds the display
/// labels of what the token lacks, in permission-list order.
/// </summary>
public sealed record DeniedRepositorySummary(Guid Id, string Slug, IReadOnlyList<string> MissingPermissions);
