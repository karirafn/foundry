using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.IntegrationTests.Modules.Monitoring;

internal static class AccountSeeder
{
    // Seeds a GitHubCredential directly via DbContext when the POST endpoint cannot be used
    // (e.g., because the token validation handler stub would block the request).
    internal static async Task<Guid> SeedGitHubAccountAsync(
        FoundryWebAppFactory factory,
        string name = "My GitHub",
        string? token = "ghp_test_token",
        string baseUrl = "https://github.com")
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        BaseUrl parsedBaseUrl = BaseUrl.Create(baseUrl).ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create(name, token, parsedBaseUrl);
        dbContext.Set<Credential>().Add(credential);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return credential.Id.Value;
    }

    // Seeds a GitLabCredential directly via DbContext when the POST endpoint cannot be used.
    internal static async Task<Guid> SeedGitLabAccountAsync(
        FoundryWebAppFactory factory,
        string name = "My GitLab",
        string? token = "glpat-test_token",
        string baseUrl = "https://gitlab.com")
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        BaseUrl parsedBaseUrl = BaseUrl.Create(baseUrl).ValueOrThrow();
        GitLabCredential credential = GitLabCredential.Create(name, token, parsedBaseUrl);
        dbContext.Set<Credential>().Add(credential);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return credential.Id.Value;
    }

    // Sets namespaces on a credential so the resolver can match repositories under those owners.
    // No endpoint exposes namespace seeding directly — namespace derivation is handled internally;
    // seed via DbContext to simulate the state that derivation would produce.
    internal static async Task SetOwnerNamespacesAsync(
        FoundryWebAppFactory factory,
        Guid accountId,
        params string[] owners)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        Credential? credential = await dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == CredentialId.From(accountId), CancellationToken.None);

        if (credential is null)
        {
            return;
        }

        IEnumerable<Namespace> namespaces = owners.Select(o => Namespace.Create(o).ValueOrThrow());
        credential.SetNamespaces(namespaces);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
