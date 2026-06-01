namespace Foundry.Modules.Monitoring.Contracts;

public sealed record ReviewComment(string Body, string? FilePath = null, int? Line = null);
