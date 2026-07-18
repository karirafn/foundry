using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Persistence.CredentialNamespaceMigrationTests;

/// <summary>
/// Verifies that the unique(host, value) index on credential_namespaces prevents
/// two credentials from claiming the same namespace on the same host.
/// </summary>
public sealed class WhenUniqueConstraintIsViolated : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenUniqueConstraintIsViolated()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task DuplicateNamespaceOnSameHost_ThrowsSqliteException()
    {
        // Arrange — seed two credentials and set identical namespaces on both.
        Guid credentialId1 = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "github-cred-1");
        Guid credentialId2 = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "github-cred-2");

        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        Namespace ns = Namespace.Create("myorg").ValueOrThrow();

        GitHubCredential credential1 = await dbContext.Set<Credential>()
            .OfType<GitHubCredential>()
            .FirstAsync(c => c.Id == CredentialId.From(credentialId1), TestContext.Current.CancellationToken);

        credential1.SetNamespaces([ns]);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act — attempt to claim the same namespace with a different credential.
        GitHubCredential credential2 = await dbContext.Set<Credential>()
            .OfType<GitHubCredential>()
            .FirstAsync(c => c.Id == CredentialId.From(credentialId2), TestContext.Current.CancellationToken);

        credential2.SetNamespaces([ns]);

        // Assert
        await Should.ThrowAsync<DbUpdateException>(async () =>
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SameNamespaceOnDifferentHosts_Succeeds()
    {
        // Arrange — seed one GitHub and one GitLab credential, claim the same namespace value
        // on each; they are on different hosts so the unique(host, value) constraint is not violated.
        Guid gitHubCredentialId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "github-cred-distinct");
        Guid gitLabCredentialId = await AccountSeeder.SeedGitLabAccountAsync(_factory, name: "gitlab-cred-distinct");

        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        Namespace ns = Namespace.Create("sharedorg").ValueOrThrow();

        GitHubCredential gitHubCredential = await dbContext.Set<Credential>()
            .OfType<GitHubCredential>()
            .FirstAsync(c => c.Id == CredentialId.From(gitHubCredentialId), TestContext.Current.CancellationToken);

        gitHubCredential.SetNamespaces([ns]);

        GitLabCredential gitLabCredential = await dbContext.Set<Credential>()
            .OfType<GitLabCredential>()
            .FirstAsync(c => c.Id == CredentialId.From(gitLabCredentialId), TestContext.Current.CancellationToken);

        gitLabCredential.SetNamespaces([ns]);

        // Act — no exception; different hosts allow the same namespace value
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        int count = await dbContext.Set<CredentialNamespace>()
            .Where(n => n.Value == "sharedorg")
            .CountAsync(TestContext.Current.CancellationToken);

        count.ShouldBe(2);
    }
}
