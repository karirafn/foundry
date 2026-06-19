using System.Net;
using System.Net.Http.Headers;

using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure;

internal sealed class GitLabHttpClient(HttpClient httpClient)
{
    public async Task<Result<TokenValidationResult>> ValidateTokenAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<TokenValidationResult>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), "user");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Add("PRIVATE-TOKEN", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return Result<TokenValidationResult>.Ok(TokenValidationResult.AuthFailure());
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<TokenValidationResult>.Fail(GitLabErrors.UnexpectedStatusCode((int)response.StatusCode));
        }

        return Result<TokenValidationResult>.Ok(TokenValidationResult.Validated([]));
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        string uriString = uri.ToString();
        return uriString.EndsWith('/') ? uri : new Uri(uriString + '/');
    }
}

internal static class GitLabErrors
{
    public static readonly Error InvalidBaseUrl = new(
        "GitLab.InvalidBaseUrl",
        "The base URL must use the https scheme.");

    public static Error UnexpectedStatusCode(int statusCode) =>
        new("GitLab.UnexpectedStatusCode", $"GitLab API returned unexpected status code {statusCode}.");
}
