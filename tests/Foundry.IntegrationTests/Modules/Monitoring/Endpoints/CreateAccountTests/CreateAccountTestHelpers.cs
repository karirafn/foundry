using System.Net;
using System.Text;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

/// <summary>
/// Stub that returns a valid token response keyed by token value → account name.
/// Falls back to "unknown-user" for unregistered tokens.
/// </summary>
internal sealed class TokenKeyedValidateTokenStub(Dictionary<string, string> tokenToName)
    : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
{
    public Task<Result<ValidateToken.Response>> HandleAsync(
        ValidateToken.Query query,
        CancellationToken cancellationToken)
    {
        string accountName = tokenToName.TryGetValue(query.Token, out string? name) ? name : "unknown-user";
        ValidateToken.Response response = new(
            IsValid: true,
            IsAuthFailure: false,
            ScopesVerified: true,
            MissingScopes: [],
            AccountName: accountName);
        return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
    }
}

/// <summary>
/// A fake HttpMessageHandler for the GitHub listing endpoint.
/// Returns a fixed listing JSON regardless of the request.
/// </summary>
internal sealed class StaticListingFakeHandler(HttpStatusCode statusCode, string responseBody) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = new(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}

/// <summary>
/// A fake HttpMessageHandler for the GitHub listing endpoint.
/// Returns a listing JSON keyed by the Bearer token in the Authorization header.
/// Falls back to "[]" for unknown tokens.
/// </summary>
internal sealed class TokenKeyedListingFakeHandler(Dictionary<string, string> tokenToListing) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string token = string.Empty;
        if (request.Headers.TryGetValues("Authorization", out IEnumerable<string>? authValues))
        {
            string bearer = authValues.FirstOrDefault() ?? string.Empty;
            token = bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? bearer["Bearer ".Length..]
                : string.Empty;
        }

        string json = tokenToListing.TryGetValue(token, out string? listing) ? listing : "[]";
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}

/// <summary>
/// Seeds a credential with the given namespaces directly via DbContext.
/// Use only when the POST endpoint cannot establish the desired state.
/// </summary>
internal static class CredentialSeeder
{
    internal static async Task<Guid> SeedWithNamespacesAsync(
        FoundryWebAppFactory factory,
        string name,
        params string[] namespaces)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create(name, "ghp_seeded", baseUrl);
        credential.SetNamespaces(namespaces.Select(n => Namespace.Create(n).ValueOrThrow()));
        dbContext.Set<Credential>().Add(credential);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return credential.Id.Value;
    }
}
