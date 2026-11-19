namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// One permission an account token needs. <see cref="Verification"/> is how Foundry checks it:
/// <c>probe</c>, <c>role</c>, <c>scope</c>, <c>always-granted</c> or <c>unverifiable</c>.
/// </summary>
public sealed record TokenPermissionRequirement(string Label, string Verification);
