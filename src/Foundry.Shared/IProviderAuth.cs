namespace Foundry.Shared;

public interface IProviderAuth
{
    Task<Result<string>> GetTokenAsync(string secretKeyName, CancellationToken cancellationToken);
}
