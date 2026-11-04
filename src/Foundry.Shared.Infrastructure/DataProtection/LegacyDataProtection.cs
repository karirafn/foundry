namespace Foundry.Shared.Infrastructure.DataProtection;

public static class LegacyDataProtection
{
    public static string? ResolveDiscriminator(string? configured, string? hostDefault, string currentName)
    {
        string? resolved = string.IsNullOrWhiteSpace(configured) ? hostDefault : configured.Trim();

        if (string.IsNullOrWhiteSpace(resolved) || string.Equals(resolved, currentName, StringComparison.Ordinal))
        {
            return null;
        }

        return resolved;
    }
}
