using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Features.ApiCredential;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Credentials;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.ApiCredential.ClaudeApiCredentialReaderTests;

public sealed class ReadAsync : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeCredentialsOrchestrator _orchestrator = new();

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        await using FoundryDbContext setup = CreateDbContext();
        await setup.Database.EnsureCreatedAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    private async Task SeedAsync(ClaudeAccount account)
    {
        await using FoundryDbContext db = CreateDbContext();
        db.Set<ClaudeAccount>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Result<ClaudeApiCredential>> ReadCredentialAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        ClaudeApiCredentialReader sut = new(db, _orchestrator);
        return await sut.ReadAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenApiKeyMode_ReturnsTheApiKeyWithoutStartingAHelperContainer()
    {
        // Arrange
        await SeedAsync(new ClaudeAccountBuilder().WithApiKey("sk-ant-key").Build());

        // Act
        Result<ClaudeApiCredential> result = await ReadCredentialAsync();

        // Assert
        ClaudeApiCredential credential = result.ShouldBeOfType<Result<ClaudeApiCredential>.Success>().Value;
        credential.ShouldBeOfType<ClaudeApiCredential.ApiKey>().Secret.ShouldBe("sk-ant-key");
        _orchestrator.ReadAccessTokenCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenOAuthMode_ReadsTheTokenThroughTheCredentialHelper()
    {
        // Arrange
        await SeedAsync(new ClaudeAccountBuilder().WithOAuth().Build());
        _orchestrator.AccessTokenResult = Result<string>.Ok("sk-ant-oat01-live");

        // Act
        Result<ClaudeApiCredential> result = await ReadCredentialAsync();

        // Assert
        ClaudeApiCredential credential = result.ShouldBeOfType<Result<ClaudeApiCredential>.Success>().Value;
        credential.ShouldBeOfType<ClaudeApiCredential.OAuthToken>().Secret.ShouldBe("sk-ant-oat01-live");
        credential.ToString().ShouldNotContain("sk-ant-oat01-live");
        _orchestrator.ReadAccessTokenCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenOAuthTokenCannotBeRead_ReturnsTheFailure()
    {
        // Arrange
        await SeedAsync(new ClaudeAccountBuilder().WithOAuth().Build());
        _orchestrator.AccessTokenResult = Result<string>.Fail(new Error("Credentials.OAuthTokenUnavailable", "signed out"));

        // Act
        Result<ClaudeApiCredential> result = await ReadCredentialAsync();

        // Assert
        result.ShouldBeOfType<Result<ClaudeApiCredential>.Failure>().Error.Code.ShouldBe("Credentials.OAuthTokenUnavailable");
    }

    [Fact]
    public async Task WhenNoCredentialIsConfigured_ReturnsNotConfigured()
    {
        // Arrange
        await SeedAsync(new ClaudeAccountBuilder().WithNotConfiguredApiKey().Build());

        // Act
        Result<ClaudeApiCredential> result = await ReadCredentialAsync();

        // Assert
        result.ShouldBeOfType<Result<ClaudeApiCredential>.Failure>().Error.Code.ShouldBe("Credentials.NotConfigured");
    }
}
