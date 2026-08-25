using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

public sealed class WhenGitLabAccountIsValid : IAsyncDisposable
{
    private const string ResolvedAccountName = "gitlab-user";

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenGitLabAccountIsValid()
    {
        ValidateToken.Response validResponse = new(
            Kind: ValidateToken.Kinds.Authenticated,
            AccountName: ResolvedAccountName,
            MissingScopes: [],
            DetectedProvider: null);

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(validResponse)));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedAllowedHostAsync(string host)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdateAllowedProviderHosts([host]);
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsCreatedWithCredentialSummary()
    {
        // Arrange
        object body = new
        {
            providerType = "gitlab",
            baseUrl = "https://gitlab.com",
            token = "glpat_test_token",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        CredentialCreationResult? result = await response.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        CredentialSummary account = result.Credential;
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe(ResolvedAccountName),
            () => account.ProviderType.ShouldBe("gitlab"),
            () => account.BaseUrl.ShouldBe("https://gitlab.com/"),
            () => account.HasToken.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenProviderTypeIsMixedCase_ReturnsCreated()
    {
        // Arrange
        object body = new
        {
            providerType = "GitLab",
            baseUrl = "https://gitlab.com",
            token = "glpat_test_token",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>
    /// AC#4: a self-hosted host must appear in the operator allowlist before account creation
    /// is permitted. This test proves the full end-to-end path: seed the allowlist entry via
    /// GlobalSettings, then create the account. DNS for *.example.com is NXDOMAIN so
    /// SystemHostAddressResolver returns [] and the DNS-to-private check passes.
    /// </summary>
    [Fact]
    public async Task WhenSelfHosted_AndHostIsAllowlisted_ReturnsCreatedWithCorrectBaseUrl()
    {
        // Arrange
        await SeedAllowedHostAsync("gitlab.example.com");

        object body = new
        {
            providerType = "gitlab",
            baseUrl = "https://gitlab.example.com",
            token = "glpat_test_token",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        CredentialCreationResult? result = await response.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Credential.BaseUrl.ShouldBe("https://gitlab.example.com/");
    }

    /// <summary>
    /// AC#4 negative path: a self-hosted host NOT in the operator allowlist must be
    /// rejected with 400 before any provider call is made.
    /// </summary>
    [Fact]
    public async Task WhenSelfHosted_AndHostIsNotAllowlisted_ReturnsBadRequestWithHostMessage()
    {
        // Arrange — no GlobalSettings row means empty allowlist
        object body = new
        {
            providerType = "gitlab",
            baseUrl = "https://gitlab.example.com",
            token = "glpat_test_token",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.ShouldContain("gitlab.example.com");
    }

    private sealed class StubValidateTokenHandler(Result<ValidateToken.Response> result)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
