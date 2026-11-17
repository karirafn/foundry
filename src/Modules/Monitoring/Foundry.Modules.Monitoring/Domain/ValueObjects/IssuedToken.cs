namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>An account token together with the expiry the provider reported for it.</summary>
public sealed record IssuedToken(string Value, TokenExpiry Expiry);
