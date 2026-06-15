using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

namespace Foundry.IntegrationTests.Modules.Monitoring;

internal static class RepositorySeeder
{
    internal static async Task<Guid> SeedRepositoryAsync(
        FoundryWebAppFactory factory,
        Guid accountId,
        string slug = "owner/repo",
        int? pollIntervalSeconds = null)
    {
        using HttpClient client = factory.CreateClient();

        object body = new { slug, pollIntervalSeconds };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            CancellationToken.None);

        response.EnsureSuccessStatusCode();

        RepositorySummary summary = (await response.Content
            .ReadFromJsonAsync<RepositorySummary>(CancellationToken.None))
            .ShouldNotBeNull();

        return summary.Id;
    }
}
