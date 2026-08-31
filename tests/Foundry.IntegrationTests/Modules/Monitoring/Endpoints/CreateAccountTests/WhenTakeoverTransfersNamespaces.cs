using System.Net;
using System.Net.Http.Json;

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
/// Verifies that when takeoverNamespaces is supplied and the namespaces are in the derived set,
/// the claims transfer atomically from their holders to the new credential (AC-2, AC-4, AC-6).
/// </summary>
public sealed class WhenTakeoverTransfersNamespaces : IAsyncDisposable
{
    private const string NewToken = "ghp_new_token";
    private const string HolderName = "holder-user";
    private const string NewAccountName = "new-user";

    private const string OctocatListingJson = """
        [
          { "full_name": "octocat/repo", "private": false, "permissions": { "push": true } }
        ]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTakeoverTransfersNamespaces()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new TokenKeyedValidateTokenStub(new Dictionary<string, string>
                {
                    [NewToken] = NewAccountName,
                }));

            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new StaticListingFakeHandler(HttpStatusCode.OK, OctocatListingJson)),
                    NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenTakeoverRequested_TransfersClaimFromHolder()
    {
        // Arrange — seed an existing holder that owns "octocat"
        Guid holderCredentialId = await CredentialSeeder.SeedWithNamespacesAsync(_factory, HolderName, "octocat");

        object body = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = NewToken,
            takeoverNamespaces = new[] { "octocat" },
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert — new credential created, namespace transferred
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        CredentialCreationResult? result = await response.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Credential.Name.ShouldBe(NewAccountName);
        result.Credential.Namespaces.ShouldContain("octocat");
        result.AffectedRepositories.ShouldNotBeNull();

        // Verify holder no longer owns "octocat"
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        Credential? holder = await dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(
                c => c.Id == CredentialId.From(holderCredentialId),
                TestContext.Current.CancellationToken);
        holder.ShouldNotBeNull();
        holder.Namespaces.ShouldNotContain(n => n.Value == "octocat");
    }

    [Fact]
    public async Task WhenTakeoverRequested_AndConflictMeanwhileVanishes_ClaimsNormally()
    {
        // Arrange — no other credential holds "octocat"; takeoverNamespaces lists it anyway
        object body = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = NewToken,
            takeoverNamespaces = new[] { "octocat" },
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert — claims normally (AC-4)
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        CredentialCreationResult? result = await response.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Credential.Namespaces.ShouldContain("octocat");
        result.AffectedRepositories.ShouldNotBeNull();
    }
}
