using Foundry.WebApi.Shared.Abstractions;

using Microsoft.Extensions.Configuration;

namespace Foundry.WebApi.Shared.Infrastructure;

internal sealed class ConfigurationProviderAuth(IConfiguration configuration) : IProviderAuth
{
    public Task<Result<string>> GetTokenAsync(string secretKeyName, CancellationToken cancellationToken)
    {
        string? token = configuration[secretKeyName];

        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(Result<string>.Fail(ProviderAuthErrors.SecretNotFound(secretKeyName)));
        }

        return Task.FromResult(Result<string>.Ok(token));
    }
}

public static class ProviderAuthErrors
{
    public static Error SecretNotFound(string secretKeyName) =>
        new("ProviderAuth.SecretNotFound", $"Secret key '{secretKeyName}' was not found or is empty.");
}
