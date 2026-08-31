using System.Net;
using System.Net.Http.Json;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

/// <summary>
/// Verifies that creating a credential derives its namespace set from the provider's writable-repo listing.
/// </summary>
public sealed class WhenNamespacesAreDerived : IAsyncDisposable
{
    private const string ResolvedAccountName = "octocat";

    // Two repos owned by "octocat" (writable) and one by "other-org" (not writable).
    private const string GitHubListingJson = """
        [
          { "full_name": "octocat/repo-a", "private": false, "permissions": { "push": true } },
          { "full_name": "octocat/repo-b", "private": true, "permissions": { "push": true } },
          { "full_name": "other-org/repo-c", "private": false, "permissions": { "push": false } }
        ]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenNamespacesAreDerived()
    {
        ValidateToken.Response validResponse = new(
            Kind: ValidateToken.Kinds.Authenticated,
            AccountName: ResolvedAccountName,
            MissingScopes: [],
            DetectedProvider: null);

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(validResponse)));

            // Replace the named HttpClient registrations with a fake that returns the listing.
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(new HttpClient(new ListingFakeHandler(HttpStatusCode.OK, GitHubListingJson)), NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenCredentialAdded_DerivesNamespacesFromWritableRepos()
    {
        // Arrange
        object body = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        CredentialCreationResult? result = await response.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();

        // Verify "octocat" namespace was derived (both writable repos share this owner)
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        Credential? credential = await dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == CredentialId.From(result.Credential.Id), TestContext.Current.CancellationToken);

        credential.ShouldNotBeNull();
        credential.Namespaces.Count.ShouldBe(1);
        credential.Namespaces.ShouldContain(ns => ns.Value == "octocat");
        credential.Namespaces.ShouldNotContain(ns => ns.Value == "other-org");
    }

    private sealed class StubValidateTokenHandler(Result<ValidateToken.Response> result)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class ListingFakeHandler(HttpStatusCode statusCode, string responseBody) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (StaticListingFakeHandler.IsProbePost(request))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
            }

            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
