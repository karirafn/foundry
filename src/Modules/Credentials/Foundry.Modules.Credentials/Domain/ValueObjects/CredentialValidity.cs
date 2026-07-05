namespace Foundry.Modules.Credentials.Domain.ValueObjects;

public abstract record CredentialValidity
{
    private CredentialValidity() { }

    public sealed record Valid : CredentialValidity;

    public sealed record Invalid(string Reason) : CredentialValidity;
}
