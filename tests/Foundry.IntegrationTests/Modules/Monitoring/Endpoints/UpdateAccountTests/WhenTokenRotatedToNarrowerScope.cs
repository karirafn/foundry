using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

/// <summary>
/// Verifies that rotating a credential to a narrower token shrinks the namespace set,
/// re-evaluates affected repositories, and reports the changed repos in the response (AC2, AC4).
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

            // The real RepositoryEligibilityEvaluator resolves the credential via ICredentialResolver.
            // After dropping bob's namespace, bob/repo-b has no credential → ineligible (no-credential).
            // alice/repo-a still has a credential but the fake HTTP handler returns listing JSON for
            // branch-protection calls, which fails to parse → Unreachable.
            // Use AssignedEligibilityEvaluator to control the outcome precisely for test clarity.
            services.RemoveAll<IRepositoryEligibilityEvaluator>();
            services.AddScoped<IRepositoryEligibilityEvaluator>(_ =>
                new AssignedEligibilityEvaluator(new Dictionary<string, RepositoryEligibility>
                {
                    ["alice/repo-a"] = new RepositoryEligibility.Eligible(),
                    ["bob/repo-b"] = new RepositoryEligibility.Ineligible(
                        [EligibilityViolation.NoCredential("bob")]),
                }));
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
        CredentialCreationResult? createdResult = await createResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        createdResult.ShouldNotBeNull();
        CredentialSummary created = createdResult.Credential;

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

    [Fact]
    public async Task WhenRotatedToNarrowerToken_DroppedOwnerRepoBecomesIneligibleAndAppearsInAffectedList()
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
        CredentialCreationResult? createdResult = await createResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        createdResult.ShouldNotBeNull();
        CredentialSummary created = createdResult.Credential;

        // Seed monitored repos under both owners
        Guid aliceRepoId = await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            created.Id,
            slug: "alice/repo-a");
        Guid bobRepoId = await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            created.Id,
            slug: "bob/repo-b");

        // Set both repos to eligible before rotation
        await RepositoryEligibilitySeeder.SetEligibleAsync(
            _factory,
            aliceRepoId,
            bobRepoId);

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

        // Assert — response contains the affected repositories
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        CredentialUpdateResult? result = await updateResponse.Content
            .ReadFromJsonAsync<CredentialUpdateResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();

        // bob/repo-b lost its credential (AC4); only bob changed (AC2)
        result.AffectedRepositories.ShouldContain(r => r.Slug == "bob/repo-b");
        AffectedRepository bobResult = result.AffectedRepositories.Single(r => r.Slug == "bob/repo-b");
        bobResult.ShouldSatisfyAllConditions(
            () => bobResult.Id.ShouldBe(bobRepoId),
            () => bobResult.PreviousStatus.ShouldBe("eligible"),
            () => bobResult.NewStatus.ShouldBe("ineligible"));

        // alice/repo-a stays eligible — not in affected list
        result.AffectedRepositories.ShouldNotContain(r => r.Slug == "alice/repo-a");
    }
}
