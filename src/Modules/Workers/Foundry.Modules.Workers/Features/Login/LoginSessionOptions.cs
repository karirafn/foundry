namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// Fixed timeout constants for the interactive OAuth login session.
/// These are constants rather than configuration — changing them requires
/// both a container-side and a service-side update to stay in sync.
/// </summary>
internal static class LoginSessionOptions
{
    /// <summary>
    /// Maximum time to wait for the OAuth authorization URL to appear in container logs.
    /// If no URL is observed within this window the session fails with <see cref="LoginFailureReason.UrlTimeout"/>.
    /// </summary>
    internal static readonly TimeSpan UrlTimeout = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Maximum duration of an entire login session (from container start to code submission).
    /// If no code is submitted within this window the session fails with <see cref="LoginFailureReason.CodeTimeout"/>.
    /// </summary>
    internal static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Maximum time to wait for the container to confirm login success after the code is delivered.
    /// Bounds the post-code token-exchange log scan so it cannot outlive the session indefinitely.
    /// Mapped to <see cref="LoginFailureReason.CodeTimeout"/> when it fires.
    /// </summary>
    internal static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(2);
}
