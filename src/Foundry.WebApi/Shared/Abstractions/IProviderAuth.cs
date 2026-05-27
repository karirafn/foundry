namespace Foundry.WebApi.Shared.Abstractions;

public interface IProviderAuth
{
    Task<Result<string>> GetTokenAsync(string secretKeyName, CancellationToken cancellationToken);
}
