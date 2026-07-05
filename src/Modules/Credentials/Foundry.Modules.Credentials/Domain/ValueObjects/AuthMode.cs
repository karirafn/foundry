namespace Foundry.Modules.Credentials.Domain.ValueObjects;

// CA1724 suppressed: nested type name 'OAuth' is intentional domain terminology
// and is unambiguous in this namespace context despite the conflict with
// Microsoft.AspNetCore.Authentication.OAuth namespace.
public abstract record AuthMode
{
    private AuthMode() { }

    public sealed record ApiKey(string Key) : AuthMode;

#pragma warning disable CA1724
    public sealed record OAuth(string? SubscriptionType) : AuthMode;
#pragma warning restore CA1724
}
