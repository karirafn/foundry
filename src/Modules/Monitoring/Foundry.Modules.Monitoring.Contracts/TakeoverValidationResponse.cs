namespace Foundry.Modules.Monitoring.Contracts;

public sealed record TakeoverValidationResponse(IReadOnlyList<string> InvalidNamespaces);
