namespace Foundry.Modules.Credentials.Contracts;

public interface ICredentialGate
{
    Task<bool> CanDispatchAsync(CancellationToken cancellationToken);
}
