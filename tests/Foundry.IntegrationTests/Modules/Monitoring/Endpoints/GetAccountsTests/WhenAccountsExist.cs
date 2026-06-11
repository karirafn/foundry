using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetAccountsTests;

public sealed class WhenAccountsExist : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountsExist()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedAccountAsync(string name, string? token, string baseUrl)
    {
        // No HTTP POST endpoint exists yet — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GitHubAccount account = GitHubAccount.Create(name, token, new Uri(baseUrl));
        dbContext.Set<Account>().Add(account);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsAllAccounts()
    {
        // Arrange
        await SeedAccountAsync("GitHub Cloud", "ghp_test", "https://github.com");
        await SeedAccountAsync("GitHub Enterprise", null, "https://ghes.example.com");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<AccountSummary>? accounts = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<AccountSummary>>(TestContext.Current.CancellationToken);
        accounts.ShouldNotBeNull();
        accounts.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ProjectsFieldsCorrectly()
    {
        // Arrange
        await SeedAccountAsync("My GitHub", "ghp_secret", "https://github.com");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<AccountSummary>? accounts = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<AccountSummary>>(TestContext.Current.CancellationToken);
        accounts.ShouldNotBeNull();
        AccountSummary account = accounts.ShouldHaveSingleItem();
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe("My GitHub"),
            () => account.ProviderType.ShouldBe("github"),
            () => account.BaseUrl.ShouldBe("https://github.com/"),
            () => account.HasToken.ShouldBeTrue());
    }

    [Fact]
    public async Task DoesNotReturnToken()
    {
        // Arrange
        await SeedAccountAsync("My GitHub", "ghp_secret_token", "https://github.com");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        responseBody.ShouldNotContain("ghp_secret_token");
    }
}
