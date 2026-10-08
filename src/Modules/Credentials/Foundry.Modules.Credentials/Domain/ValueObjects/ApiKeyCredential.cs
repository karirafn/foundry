namespace Foundry.Modules.Credentials.Domain.ValueObjects;

public abstract record ApiKeyCredential
{
    private ApiKeyCredential() { }

    public sealed record Present : ApiKeyCredential
    {
        public Present(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            Value = value;
        }

        public string Value { get; }

        public override string ToString() => "Present { Value = *** }";
    }

    public sealed record NotConfigured : ApiKeyCredential;

    public sealed record Unreadable : ApiKeyCredential;
}
