using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

using Foundry.Modules.Credentials.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Roles.Models;

/// <summary>Lists the models the workers' credential can use.</summary>
internal interface IClaudeModelsApi
{
    Task<Result<IReadOnlyList<ClaudeModel>>> ListAsync(ClaudeApiCredential credential, CancellationToken cancellationToken);
}

/// <summary>
/// Client for <c>GET https://api.anthropic.com/v1/models</c>. Pages with <c>limit=100</c>, following
/// <c>after_id</c> while <c>has_more</c> is set. An OAuth token is sent as <c>Authorization: Bearer</c> (which the
/// endpoint accepts for the <c>user:inference</c> scope without a beta header); an API key as <c>x-api-key</c>.
/// </summary>
internal sealed class AnthropicModelsClient(HttpClient httpClient) : IClaudeModelsApi
{
    internal static readonly Uri BaseAddress = new("https://api.anthropic.com/");
    internal const string AnthropicVersion = "2023-06-01";
    internal const int PageSize = 100;

    // A guard against a server that keeps answering has_more; 20 pages is 2,000 models.
    internal const int MaxPages = 20;

    internal const string FailedCode = "Worker.ModelListFailed";

    public async Task<Result<IReadOnlyList<ClaudeModel>>> ListAsync(
        ClaudeApiCredential credential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);

        List<ClaudeModel> models = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        string? afterId = null;

        for (int page = 0; page < MaxPages; page++)
        {
            Result<ModelPage> pageResult = await GetPageAsync(credential, afterId, cancellationToken);

            if (pageResult is not Result<ModelPage>.Success { Value: var modelPage })
            {
                return Result<IReadOnlyList<ClaudeModel>>.Fail(((Result<ModelPage>.Failure)pageResult).Error);
            }

            foreach (ClaudeModel model in modelPage.Models)
            {
                if (seen.Add(model.Id))
                {
                    models.Add(model);
                }
            }

            string? nextAfterId = modelPage.LastId
                ?? (modelPage.Models.Count > 0 ? modelPage.Models[modelPage.Models.Count - 1].Id : null);

            if (!modelPage.HasMore || nextAfterId is null || string.Equals(nextAfterId, afterId, StringComparison.Ordinal))
            {
                return models;
            }

            afterId = nextAfterId;
        }

        return models;
    }

    private async Task<Result<ModelPage>> GetPageAsync(
        ClaudeApiCredential credential,
        string? afterId,
        CancellationToken cancellationToken)
    {
        string query = string.Create(CultureInfo.InvariantCulture, $"v1/models?limit={PageSize}");

        if (afterId is not null)
        {
            query += $"&after_id={Uri.EscapeDataString(afterId)}";
        }

        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(BaseAddress, query));
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        switch (credential)
        {
            case ClaudeApiCredential.OAuthToken token:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Secret);
                break;
            case ClaudeApiCredential.ApiKey key:
                request.Headers.Add("x-api-key", key.Secret);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(credential), credential.GetType().Name, "Unknown credential kind.");
        }

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<ModelPage>.Fail(new Error(
                    FailedCode,
                    $"The Models API answered HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."));
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

            return ParsePage(document.RootElement);
        }
        catch (HttpRequestException ex)
        {
            return Result<ModelPage>.Fail(new Error(FailedCode, $"The Models API could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<ModelPage>.Fail(new Error(FailedCode, "The Models API did not answer in time."));
        }
        catch (JsonException)
        {
            return Result<ModelPage>.Fail(new Error(FailedCode, "The Models API returned a response that is not JSON."));
        }
    }

    internal static Result<ModelPage> ParsePage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out JsonElement data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return Result<ModelPage>.Fail(new Error(FailedCode, "The Models API response has no data array."));
        }

        List<ClaudeModel> models = [];

        foreach (JsonElement item in data.EnumerateArray())
        {
            string? id = ReadString(item, "id");

            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string displayName = ReadString(item, "display_name") is { Length: > 0 } name ? name : id;
            models.Add(new ClaudeModel(id, displayName, ReadLifecycle(item)));
        }

        bool hasMore = root.TryGetProperty("has_more", out JsonElement hasMoreElement)
            && hasMoreElement.ValueKind == JsonValueKind.True;

        return new ModelPage(models, hasMore, ReadString(root, "last_id"));
    }

    // `lifecycle` is read leniently: a plain string ("active", "deprecated"), or an object carrying the state in
    // `status` or `state`.
    private static string? ReadLifecycle(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("lifecycle", out JsonElement lifecycle))
        {
            return null;
        }

        return lifecycle.ValueKind switch
        {
            JsonValueKind.String => lifecycle.GetString(),
            JsonValueKind.Object => ReadString(lifecycle, "status") ?? ReadString(lifecycle, "state"),
            _ => null,
        };
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal sealed record ModelPage(IReadOnlyList<ClaudeModel> Models, bool HasMore, string? LastId);
}
