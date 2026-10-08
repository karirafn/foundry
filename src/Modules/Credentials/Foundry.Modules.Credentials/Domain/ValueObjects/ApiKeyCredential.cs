namespace Foundry.Modules.Credentials.Domain.ValueObjects;

public abstract record ApiKeyCredential
{
    private ApiKeyCredential() { }

    public sealed record Present(string Value) : ApiKeyCredential;

    public sealed record NotConfigured : ApiKeyCredential;

    public sealed record Unreadable : ApiKeyCredential;
}
