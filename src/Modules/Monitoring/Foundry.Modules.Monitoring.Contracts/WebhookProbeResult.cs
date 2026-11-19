namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>The outcome of one webhook ingress self-probe (ADR 0116).</summary>
public abstract record WebhookProbeResult
{
    private WebhookProbeResult()
    {
    }

    /// <summary>The probe request answered 2xx and its code arrived at this process.</summary>
    public sealed record Reached : WebhookProbeResult;

    /// <summary>The probe did not demonstrably reach this process, for <see cref="Failure"/>.</summary>
    public sealed record Unreachable(WebhookProbeFailure Failure) : WebhookProbeResult;
}

/// <summary>Why a webhook ingress self-probe failed; <see cref="Cause"/> is the stable wire name.</summary>
public abstract record WebhookProbeFailure(string Cause)
{
    /// <summary>The host did not resolve.</summary>
    public sealed record Dns() : WebhookProbeFailure("dns");

    /// <summary>The TLS handshake or certificate validation failed.</summary>
    public sealed record Tls() : WebhookProbeFailure("tls");

    /// <summary>No response arrived in time, or no connection could be made.</summary>
    public sealed record Timeout() : WebhookProbeFailure("timeout");

    /// <summary>A server answered 404, or 2xx without the code reaching this process.</summary>
    public sealed record OtherInstance() : WebhookProbeFailure("other-instance");

    /// <summary>A server answered with any other status, redirects included.</summary>
    public sealed record HttpStatus(int StatusCode) : WebhookProbeFailure("http-status");
}
