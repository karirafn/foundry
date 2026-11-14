using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetAccountsTests;

/// <summary>
/// An account whose token the provider refused (an Invalid Token) is reported by GET /api/accounts with
/// TokenStatus "invalid" and the time the refusal was first confirmed.
/// </summary>
public sealed class WhenAccountTokenIsInvalid : IAsyncDisposable
{
    private static readonly DateTimeOffset DetectedAt = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountTokenIsInvalid()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsInvalidStatusWithDetectionTime()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        await MarkInvalidAsync(accountId);

        // Act
        CredentialSummary account = await GetSingleAccountAsync();

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.TokenStatus.ShouldBe("invalid"),
            () => account.TokenInvalidSince.ShouldBe(DetectedAt),
            () => account.HasToken.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenTokenIsValid_TokenInvalidSinceIsNull()
    {
        // Arrange
        await AccountSeeder.SeedGitHubAccountAsync(_factory);

        // Act
        CredentialSummary account = await GetSingleAccountAsync();

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.TokenStatus.ShouldBe("present"),
            () => account.TokenInvalidSince.ShouldBeNull());
    }

    [Fact]
    public async Task WhenTokenIsAlsoUnreadable_ReturnsUnreadableStatus()
    {
        // Arrange — the key ring was lost after the refusal was detected: the stored ciphertext no longer
        // decrypts. Written with raw SQL because the converter's write leg cannot produce an unreadable token.
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        await MarkInvalidAsync(accountId);
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            await dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE accounts SET token = {0}",
                "not-valid-base64!!!");
        }

        // Act
        CredentialSummary account = await GetSingleAccountAsync();

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.TokenStatus.ShouldBe("unreadable"),
            () => account.TokenInvalidSince.ShouldBeNull());
    }

    private async Task MarkInvalidAsync(Guid accountId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        Credential credential = await dbContext.Set<Credential>()
            .SingleAsync(c => c.Id == CredentialId.From(accountId), TestContext.Current.CancellationToken);
        credential.MarkTokenInvalid(DetectedAt);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<CredentialSummary> GetSingleAccountAsync()
    {
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<CredentialSummary>? accounts = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<CredentialSummary>>(TestContext.Current.CancellationToken);
        accounts.ShouldNotBeNull();
        return accounts.ShouldHaveSingleItem();
    }
}
