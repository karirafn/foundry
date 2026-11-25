namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// One permission an account token needs. <see cref="Verification"/> is how Foundry checks it:
/// <c>probe</c>, <c>role</c>, <c>granted</c>, <c>always-granted</c>, <c>unverifiable</c> or <c>not-checked</c>.
/// <see cref="Group"/> is the heading a run of consecutive requirements is listed under, or
/// <see langword="null"/> when the requirement stands alone.
/// </summary>
public sealed record TokenPermissionRequirement(string Label, string Verification, string? Group = null);
