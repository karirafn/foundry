using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Domain.Entities;

/// <summary>The persisted names of the <see cref="TokenExpiry"/> variants.</summary>
internal static class TokenExpiryKinds
{
    internal const string NotChecked = "not_checked";
    internal const string Unavailable = "unavailable";
    internal const string Never = "never";
    internal const string On = "on";
}
