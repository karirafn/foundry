using System.Net;
using System.Net.Http.Json;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

/// <summary>
/// Verifies that rotating a credential to a narrower token shrinks the namespace set.
/// </summary>
public sealed class WhenTokenRotatedToNarrowerScope : IAsyncDisposable
{
    // Original token sees two writable namespaces; new (narrower) token sees only one.
    private const string OriginalToken = "ghp_original";
    private const string NarrowerToken = "ghp_narrower";

    private const string BroadListingJson = """
        [
          { "full_name": "alice/repo-a", "private": false, "permissions": { "push": true } },
          { "full_name": "bob/repo-b", "private": false, "permissions": { "push": true } }
        ]
        """;

    private const string NarrowListingJson = """
        [
          { "full_name": "alice/repo-a", "private": false, "permissions": { "push": true } },
          { "full_name": "bob/repo-b", "private": false, "permissions": { "push": false } }
        ]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenRotatedToNarrowerScope()
    {
        Dictionary<string, string> tokenToListing = new()
        {
            [OriginalToken] = BroadListingJson,
            [NarrowerToken] = NarrowListingJson,
        };

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler());

            services.RemoveAll<GitHubHttpClient>();
            // The HttpClient takes ownership of the handler, so no separate disposal needed here.
            services.AddSingleton(
                new GitHubHttpClient(new HttpClient(new TokenKeyedListingFakeHandler(tokenToListing))));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenRotatedToNarrowerToken_NamespaceSetShrinks()
    {
        // Arrange — create with broad token (alice + bob namespaces)
        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = OriginalToken,
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        CredentialSummary? created = await createResponse.Content
            .ReadFromJsonAsync<CredentialSummary>(TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();

        // Confirm broad listing seeded both namespaces
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        Credential? credentialBefore = await dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(
                c => c.Id == CredentialId.From(created.Id),
                TestContext.Current.CancellationToken);
        credentialBefore.ShouldNotBeNull();
        credentialBefore.Namespaces.Count.ShouldBe(2);

        // Act — rotate to narrower token (alice only)
        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = NarrowerToken,
        };

        HttpResponseMessage updateResponse = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using IServiceScope scopeAfter = _factory.Services.CreateScope();
        DbContext dbContextAfter = scopeAfter.ServiceProvider.GetRequiredService<DbContext>();
        Credential? credentialAfter = await dbContextAfter.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(
                c => c.Id == CredentialId.From(created.Id),
                TestContext.Current.CancellationToken);
        credentialAfter.ShouldNotBeNull();
        credentialAfter.Namespaces.Count.ShouldBe(1);
        credentialAfter.Namespaces.ShouldContain(ns => ns.Value == "alice");
        credentialAfter.Namespaces.ShouldNotContain(ns => ns.Value == "bob");
    }

    private sealed class StubValidateTokenHandler : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken)
        {
            ValidateToken.Response response = new(
                IsValid: true,
                IsAuthFailure: false,
                MissingScopes: [],
                AccountName: "test-user");
            return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
        }
    }

    private sealed class TokenKeyedListingFakeHandler(Dictionary<string, string> tokenToListing)
        : DelegatingHandler
    {
        private string _lastTokenSeen = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Extract the token from PRIVATE-TOKEN header (GitLab) or Authorization header (GitHub)
            if (request.Headers.TryGetValues("Authorization", out IEnumerable<string>? authValues))
            {
                string bearer = authValues.FirstOrDefault() ?? string.Empty;
                // Format: "Bearer <token>"
                _lastTokenSeen = bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? bearer["Bearer ".Length..]
                    : string.Empty;
            }

            string json = tokenToListing.TryGetValue(_lastTokenSeen, out string? listing)
                ? listing
                : "[]";

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
