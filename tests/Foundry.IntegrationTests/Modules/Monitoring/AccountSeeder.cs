using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.IntegrationTests.Modules.Monitoring;

internal static class AccountSeeder
{
    // Seeds a GitHubAccount directly via DbContext when the POST endpoint cannot be used
    // (e.g., because the token validation handler stub would block the request).
    internal static async Task<Guid> SeedGitHubAccountAsync(
        FoundryWebAppFactory factory,
        string name = "My GitHub",
        string? token = "ghp_test_token",
        string baseUrl = "https://github.com")
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        BaseUrl parsedBaseUrl = BaseUrlFactory.Create(baseUrl);
        GitHubAccount account = GitHubAccount.Create(name, token, parsedBaseUrl);
        dbContext.Set<Account>().Add(account);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return account.Id.Value;
    }

    // Seeds a GitLabAccount directly via DbContext when the POST endpoint cannot be used.
    internal static async Task<Guid> SeedGitLabAccountAsync(
        FoundryWebAppFactory factory,
        string name = "My GitLab",
        string? token = "glpat-test_token",
        string baseUrl = "https://gitlab.com")
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        BaseUrl parsedBaseUrl = BaseUrlFactory.Create(baseUrl);
        GitLabAccount account = GitLabAccount.Create(name, token, parsedBaseUrl);
        dbContext.Set<Account>().Add(account);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return account.Id.Value;
    }
}
