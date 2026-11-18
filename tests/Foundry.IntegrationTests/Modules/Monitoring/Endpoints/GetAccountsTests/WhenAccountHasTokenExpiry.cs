using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetAccountsTests;

public sealed class WhenAccountHasTokenExpiry : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountHasTokenExpiry()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<CredentialSummary> GetSingleAccountAsync()
    {
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<CredentialSummary>? accounts = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<CredentialSummary>>(TestContext.Current.CancellationToken);
        return accounts.ShouldNotBeNull().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ReturnsTheStoredExpiry()
    {
        // Arrange
        DateTimeOffset expiresAt = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero).AddDays(60);
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            GitLabCredential credential = GitLabCredential.Create(
                AccountName.Create("alice").ValueOrThrow(),
                new IssuedToken("glpat_token", new TokenExpiry.On(expiresAt)),
                BaseUrl.Create("https://gitlab.com").ValueOrThrow());
            dbContext.Set<Credential>().Add(credential);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        CredentialSummary account = await GetSingleAccountAsync();

        // Assert
        account.TokenExpiry.ShouldBe(new TokenExpirySummary("current", expiresAt));
    }

    [Fact]
    public async Task WhenTheRowPredatesExpiryTracking_ReturnsNotChecked()
    {
        // Arrange — a row written without the expiry columns, as every row was before the migration.
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "INSERT INTO accounts (id, name, base_url, host, type) VALUES ({0}, {1}, {2}, {3}, {4})",
                [Guid.NewGuid().ToString(), "alice", "https://gitlab.com/", "gitlab.com", "gitlab"],
                TestContext.Current.CancellationToken);
        }

        // Act
        CredentialSummary account = await GetSingleAccountAsync();

        // Assert
        account.TokenExpiry.ShouldBe(new TokenExpirySummary("notChecked", null));
    }
}
