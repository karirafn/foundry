using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Workers.Features.DockerAvailability;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Health.GetSystemStatusTests;

public sealed class WhenAccountsHaveInvalidTokens : IAsyncDisposable
{
    private static readonly DateTimeOffset DetectedAt = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountsHaveInvalidTokens()
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
    public async Task WhenNoAccountIsInvalid_ReturnsZeroAndNoMessage()
    {
        // Arrange
        await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "valid-account");

        // Act
        SystemStatus status = await GetStatusAsync();

        // Assert
        status.ShouldSatisfyAllConditions(
            () => status.InvalidTokenAccountCount.ShouldBe(0),
            () => status.AccountTokenMessage.ShouldBeNull());
    }

    [Fact]
    public async Task WhenOneAccountIsInvalid_ReturnsTheSingularCopy()
    {
        // Arrange
        await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "valid-account");
        await MarkInvalidAsync(await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "invalid-account"));

        // Act
        SystemStatus status = await GetStatusAsync();

        // Assert
        status.ShouldSatisfyAllConditions(
            () => status.InvalidTokenAccountCount.ShouldBe(1),
            () => status.AccountTokenMessage.ShouldBe(
                "1 account has an invalid token — enter a new one to resume monitoring."));
    }

    [Fact]
    public async Task WhenSeveralAccountsAreInvalid_ReturnsThePluralCopy()
    {
        // Arrange
        await MarkInvalidAsync(await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "first-invalid"));
        await MarkInvalidAsync(await AccountSeeder.SeedGitLabAccountAsync(_factory, name: "second-invalid"));

        // Act
        SystemStatus status = await GetStatusAsync();

        // Assert
        status.ShouldSatisfyAllConditions(
            () => status.InvalidTokenAccountCount.ShouldBe(2),
            () => status.AccountTokenMessage.ShouldBe(
                "2 accounts have invalid tokens — enter new ones to resume monitoring."));
    }

    private async Task<SystemStatus> GetStatusAsync()
    {
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/system/status", UriKind.Relative),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        SystemStatus? status = await response.Content.ReadFromJsonAsync<SystemStatus>(
            TestContext.Current.CancellationToken);
        status.ShouldNotBeNull();
        return status;
    }

    private async Task MarkInvalidAsync(Guid accountId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        CredentialId id = CredentialId.From(accountId);
        Credential account = await db.Set<Credential>().SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken);
        account.MarkTokenInvalid(DetectedAt);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
