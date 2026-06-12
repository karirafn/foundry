using System.Net;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;

namespace Foundry.Modules.Settings.Features;

internal sealed class AnthropicAuthValidator(
    HttpClient httpClient,
    IGlobalSettingsQueries globalSettingsQueries) : IAuthValidator
{
    private const string ModelsEndpoint = "/v1/models?limit=1";
    private const string ApiKeyHeader = "x-api-key";

    public async Task<AuthValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        GlobalSettingsSummary? settings = await globalSettingsQueries.GetSettingsAsync(cancellationToken);

        return settings?.AuthMode switch
        {
            "OAuth" => ValidateOAuth(settings),
            "ApiKey" => await ValidateApiKeyAsync(cancellationToken),
            _ => AuthValidationResult.Invalid("Configure Claude authentication in Settings"),
        };
    }

    private static AuthValidationResult ValidateOAuth(GlobalSettingsSummary settings)
    {
        if (settings.ExpiresAt is not DateTimeOffset expiresAt || expiresAt <= DateTimeOffset.UtcNow)
        {
            return AuthValidationResult.Invalid(
                "OAuth token expired — run `claude setup-token` to generate a new one");
        }

        return AuthValidationResult.Valid();
    }

    private async Task<AuthValidationResult> ValidateApiKeyAsync(CancellationToken cancellationToken)
    {
        (string Key, string Value)? envVar = await globalSettingsQueries
            .GetAuthEnvironmentVariableAsync(cancellationToken);

        string apiKey = envVar?.Value ?? string.Empty;

        if (string.IsNullOrEmpty(apiKey))
        {
            return AuthValidationResult.Invalid("Configure Claude authentication in Settings");
        }

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, ModelsEndpoint);
            request.Headers.Add(ApiKeyHeader, apiKey);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => AuthValidationResult.Valid(),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    AuthValidationResult.Invalid("API key is invalid — check Settings"),
                HttpStatusCode.TooManyRequests => AuthValidationResult.ValidOptimistic(),
                _ when (int)response.StatusCode >= 500 => AuthValidationResult.ValidOptimistic(),
                _ => AuthValidationResult.Invalid("API key is invalid — check Settings"),
            };
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken != cancellationToken)
        {
            return AuthValidationResult.ValidOptimistic();
        }
        catch (HttpRequestException)
        {
            return AuthValidationResult.ValidOptimistic();
        }
    }
}
