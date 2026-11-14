namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class TokenStatuses
{
    internal const string Present = "present";
    internal const string Absent = "absent";
    internal const string Unreadable = "unreadable";

    /// <summary>The provider refused the account token (an Invalid Token).</summary>
    internal const string Invalid = "invalid";
}
