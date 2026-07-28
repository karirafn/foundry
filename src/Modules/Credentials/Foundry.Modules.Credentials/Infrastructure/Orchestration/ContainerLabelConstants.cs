namespace Foundry.Modules.Credentials.Infrastructure.Orchestration;

/// <summary>
/// Label constants for transient Docker containers managed by the Credentials module.
/// </summary>
internal static class ContainerLabelConstants
{
    /// <summary>
    /// Label key applied to every transient container (login or credential-helper).
    /// The reaper filters by this label — never <c>foundry.managed</c>, which worker containers also carry.
    /// </summary>
    internal const string TransientLabelKey = "foundry.transient";

    internal const string TransientLabelValue = "true";

    /// <summary>
    /// Label key that identifies the container's role within the Credentials module.
    /// </summary>
    internal const string RoleLabelKey = "foundry.role";

    internal const string RoleLogin = "login";

    internal const string RoleCredentialHelper = "credential-helper";
}
