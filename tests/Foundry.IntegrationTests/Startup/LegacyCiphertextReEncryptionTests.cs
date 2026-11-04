using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.DataProtection;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Startup;

/// <summary>
/// Seeds ciphertext under a legacy Data Protection discriminator, boots the host, and proves the
/// startup step re-encrypted it under the pinned application name.
/// </summary>
public sealed class LegacyCiphertextReEncryptionTests : IDisposable
{
    private const string CurrentApplicationName = "Foundry";
    private const string AccountTokenPurpose = "Foundry.Monitoring.Encryption";
    private const string ConfiguredLegacyName = @"C:\dev\foundry\src\Foundry.WebApi\";
    private const string GitHubUrl = "https://github.com";
    private const string Token = "ghp_account_token";
    private const string BranchRulesToken = "ghp_branch_rules_token";
    private const string ApiKey = "sk-ant-legacy";

    private readonly TemporaryKeyRing _keys = new();

    public void Dispose() => _keys.Dispose();

    [Fact]
    public async Task WhenCiphertextWrittenUnderHostDefaultDiscriminator_AllSecretsReadBackPresent()
    {
        // Arrange
        string legacyName = await ResolveHostDefaultDiscriminatorAsync();
        await using FoundryWebAppFactory factory = FoundryWebAppFactory.WithPersistentDataProtection(_keys.Directory);
        Seeded seeded = await MigrateAndSeedAsync(factory, legacyName);

        // Act
        using HttpClient client = factory.CreateClient();

        // Assert
        await AssertAllSecretsPresentAsync(factory, seeded);
    }

    [Fact]
    public async Task WhenCiphertextWrittenUnderHostDefaultDiscriminator_AccountTokenDecryptsUnderPinnedApplicationName()
    {
        // Arrange
        string legacyName = await ResolveHostDefaultDiscriminatorAsync();
        await using FoundryWebAppFactory factory = FoundryWebAppFactory.WithPersistentDataProtection(_keys.Directory);
        Seeded seeded = await MigrateAndSeedAsync(factory, legacyName);
        using HttpClient client = factory.CreateClient();
        IDataProtector pinned = _keys.ProviderFor(CurrentApplicationName).CreateProtector(AccountTokenPurpose);

        // Act
        string raw = await ReadRawTokenAsync(factory);

        // Assert
        DataProtectedText.Unprotect(pinned, raw).ShouldBe(Token);
    }

    [Fact]
    public async Task WhenCiphertextWrittenUnderConfiguredLegacyName_AllSecretsReadBackPresent()
    {
        // Arrange
        await using FoundryWebAppFactory factory = FoundryWebAppFactory.WithPersistentDataProtection(
            _keys.Directory,
            ConfiguredLegacyName);
        Seeded seeded = await MigrateAndSeedAsync(factory, ConfiguredLegacyName);

        // Act
        using HttpClient client = factory.CreateClient();

        // Assert
        await AssertAllSecretsPresentAsync(factory, seeded);
    }

    private static async Task<string> ResolveHostDefaultDiscriminatorAsync()
    {
        await using FoundryWebAppFactory probe = new();
        IHostEnvironment environment = probe.Services.GetRequiredService<IHostEnvironment>();
        return Path.TrimEndingDirectorySeparator(environment.ContentRootPath) + Path.DirectorySeparatorChar;
    }

    private async Task<Seeded> MigrateAndSeedAsync(FoundryWebAppFactory factory, string discriminator)
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(factory.Connection)
            .Options;

        await using FoundryDbContext dbContext = new(options, _keys.ProviderFor(discriminator));
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

        GitHubCredential credential = GitHubCredential.Create(
            "legacy-account",
            Token,
            BaseUrl.Create(GitHubUrl).ValueOrThrow(),
            BranchRulesToken);
        MonitoredRepository repository = new MonitoredRepositoryBuilder().Build();
        string webhookSecret = repository.EnsureWebhookSecret();
        ClaudeAccount account = new ClaudeAccountBuilder().WithApiKey(ApiKey).Build();

        dbContext.AddRange(credential, repository, account);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new Seeded(webhookSecret);
    }

    private static async Task AssertAllSecretsPresentAsync(FoundryWebAppFactory factory, Seeded seeded)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        GitHubCredential credential = await dbContext.Set<GitHubCredential>()
            .SingleAsync(TestContext.Current.CancellationToken);
        MonitoredRepository repository = await dbContext.Set<MonitoredRepository>()
            .SingleAsync(TestContext.Current.CancellationToken);
        ClaudeAccount account = await dbContext.Set<ClaudeAccount>()
            .SingleAsync(TestContext.Current.CancellationToken);

        credential.Token.ShouldBe(new ProviderToken.Present(Token));
        credential.BranchRulesToken.ShouldBe(new ProviderToken.Present(BranchRulesToken));
        repository.EnsureWebhookSecret().ShouldBe(seeded.WebhookSecret);
        account.AuthMode.ShouldBe(new AuthMode.ApiKey(new ApiKeyCredential.Present(ApiKey)));
    }

    private static async Task<string> ReadRawTokenAsync(FoundryWebAppFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        return await dbContext.Database
            .SqlQueryRaw<string>("SELECT token AS Value FROM accounts")
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private sealed record Seeded(string WebhookSecret);
}
