using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.WebhookTests;

/// <summary>Manual registration: details without the secret, explicit reveal, and "I've added the webhook".</summary>
public sealed class WhenRegisteringManually : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory = WebhookTestHost.CreateFactory();
    private readonly HttpClient _client;

    public WhenRegisteringManually()
    {
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<(Guid AccountId, MonitoredRepository Repository)> ArrangeAsync(string? publicBaseUrl)
    {
        await WebhookTestHost.SeedSettingsAsync(_factory, publicBaseUrl);
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory);
        return (accountId, repository);
    }

    [Fact]
    public async Task WhenIngressConfigured_DetailsCarryPayloadUrlEventsAndNoSecret()
    {
        // Arrange
        (Guid accountId, MonitoredRepository repository) = await ArrangeAsync("https://hooks.example.com/foundry");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repository.Id.Value}/webhook", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        raw.ShouldNotContain(repository.WebhookSecret!);
        RepositoryWebhookInfo info = (await response.Content.ReadFromJsonAsync<RepositoryWebhookInfo>(
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        info.Status.ShouldBe("not-registered");
        info.PayloadUrl.ShouldBe($"https://hooks.example.com/foundry/webhooks/github/{repository.WebhookHookId}");
        info.ContentType.ShouldBe("application/json");
        info.Events.ShouldContain("issues");
        info.Events.ShouldContain("check_suite");
        info.BackstopIntervalMinutes.ShouldBe(15);
    }

    [Fact]
    public async Task WhenPublicBaseUrlMissing_StatusIsNotConfigured()
    {
        // Arrange
        (Guid accountId, MonitoredRepository repository) = await ArrangeAsync(publicBaseUrl: null);

        // Act
        RepositoryWebhookInfo? info = await _client.GetFromJsonAsync<RepositoryWebhookInfo>(
            new Uri($"/api/accounts/{accountId}/repositories/{repository.Id.Value}/webhook", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        info.ShouldNotBeNull();
        info.Status.ShouldBe(RepositorySummary.WebhookNotConfigured);
        info.PayloadUrl.ShouldBeNull();
    }

    [Fact]
    public async Task WhenSecretRevealed_ReturnsSecretWithNoStoreAndListOmitsIt()
    {
        // Arrange
        (Guid accountId, MonitoredRepository repository) = await ArrangeAsync("https://hooks.example.com");

        // Act
        HttpResponseMessage reveal = await _client.PostAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repository.Id.Value}/webhook/secret", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);
        HttpResponseMessage list = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        reveal.StatusCode.ShouldBe(HttpStatusCode.OK);
        reveal.Headers.CacheControl?.NoStore.ShouldBeTrue();
        RepositoryWebhookSecret secret = (await reveal.Content.ReadFromJsonAsync<RepositoryWebhookSecret>(
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        secret.Secret.ShouldBe(repository.WebhookSecret);

        string listBody = await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        listBody.ShouldNotContain(secret.Secret);
        listBody.ShouldContain("\"webhookStatus\":\"not-registered\"");
    }

    [Fact]
    public async Task WhenOperatorRecordsRegistration_StatusBecomesPending()
    {
        // Arrange
        (Guid accountId, MonitoredRepository repository) = await ArrangeAsync("https://hooks.example.com");

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repository.Id.Value}/webhook/registered", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        RepositoryWebhookInfo info = (await response.Content.ReadFromJsonAsync<RepositoryWebhookInfo>(
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        info.Status.ShouldBe("pending");
    }

    [Fact]
    public async Task WhenAccountDoesNotCoverRepository_Returns404()
    {
        // Arrange
        (_, MonitoredRepository repository) = await ArrangeAsync("https://hooks.example.com");
        Guid otherAccount = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "Other");

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/accounts/{otherAccount}/repositories/{repository.Id.Value}/webhook/secret", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
