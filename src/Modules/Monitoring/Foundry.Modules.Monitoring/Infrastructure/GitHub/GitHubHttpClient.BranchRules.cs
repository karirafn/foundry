using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient
{
    /// <summary>The name of the single repository ruleset Foundry owns and upserts.</summary>
    internal const string EligibilityRulesetName = "foundry-eligibility";

    private const int MaxRulesetErrorBodyLength = 500;

    /// <summary>
    /// Upserts the <see cref="EligibilityRulesetName"/> repository ruleset: looked up by name among
    /// the repository's own rulesets, then created (<c>POST</c>) or replaced (<c>PUT</c>, which also
    /// re-enables a disabled ruleset and clears any bypass actors). Never creates a duplicate.
    /// </summary>
    public async Task<Result> ApplyBranchRulesAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri baseUri = EnsureTrailingSlash(apiBaseUrl);

        // includes_parents=false: only rulesets owned by this repository — an org-level ruleset
        // with the same name is not ours to modify (and cannot be updated through this endpoint).
        Uri listUri = new(baseUri, $"repos/{owner}/{repo}/rulesets?includes_parents=false&per_page=100");

        using HttpRequestMessage listRequest = CreateRulesetRequest(HttpMethod.Get, listUri, token);
        using HttpResponseMessage listResponse = await httpClient.SendAsync(listRequest, cancellationToken);

        if (!listResponse.IsSuccessStatusCode)
        {
            return Result.Fail(await ErrorFromRulesetFailureAsync(listResponse, slug, cancellationToken));
        }

        RecordRestHeadroom(listResponse);

        string listBody = await listResponse.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubRulesetSummaryDto>? rulesets =
            JsonSerializer.Deserialize<List<GitHubRulesetSummaryDto>>(listBody, JsonOptions);

        GitHubRulesetSummaryDto? existing = (rulesets ?? []).FirstOrDefault(r =>
            string.Equals(r.Name, EligibilityRulesetName, StringComparison.Ordinal)
            && (r.SourceType is null || string.Equals(r.SourceType, "Repository", StringComparison.Ordinal)));

        (HttpMethod method, Uri writeUri) = existing is null
            ? (HttpMethod.Post, new Uri(baseUri, $"repos/{owner}/{repo}/rulesets"))
            : (HttpMethod.Put, new Uri(baseUri, $"repos/{owner}/{repo}/rulesets/{existing.Id}"));

        using HttpRequestMessage writeRequest = CreateRulesetRequest(method, writeUri, token);
        writeRequest.Content = new StringContent(
            JsonSerializer.Serialize(BuildEligibilityRuleset(), JsonOptions),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage writeResponse = await httpClient.SendAsync(writeRequest, cancellationToken);

        if (!writeResponse.IsSuccessStatusCode)
        {
            return Result.Fail(await ErrorFromRulesetFailureAsync(writeResponse, slug, cancellationToken));
        }

        RecordRestHeadroom(writeResponse);
        return Result.Ok();
    }

    internal static GitHubRulesetRequestDto BuildEligibilityRuleset() =>
        new(
            Name: EligibilityRulesetName,
            Target: "branch",
            Enforcement: "active",
            Conditions: new GitHubRulesetConditionsDto(
                new GitHubRulesetRefNameDto(Include: ["~DEFAULT_BRANCH"], Exclude: [])),
            Rules:
            [
                new GitHubRulesetRuleDto(
                    "pull_request",
                    new GitHubPullRequestRuleParametersDto(
                        DismissStaleReviewsOnPush: false,
                        RequireCodeOwnerReview: false,
                        RequireLastPushApproval: false,
                        RequiredApprovingReviewCount: 0,
                        RequiredReviewThreadResolution: false)),
                new GitHubRulesetRuleDto("non_fast_forward", null),
                new GitHubRulesetRuleDto("deletion", null),
            ],
            BypassActors: []);

    private static HttpRequestMessage CreateRulesetRequest(HttpMethod method, Uri uri, string token)
    {
        HttpRequestMessage request = new(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));
        return request;
    }

    private static async Task<Error> ErrorFromRulesetFailureAsync(
        HttpResponseMessage response,
        RepositorySlug slug,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimited(response))
        {
            return GitHubErrors.RateLimitExhausted;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubErrorBodyDto? errorDto = DeserializeErrorBody(body);
        string? providerMessage = string.IsNullOrWhiteSpace(errorDto?.Message)
            ? null
            : TruncateWithEllipsis(RedactSecrets(errorDto.Message), MaxRulesetErrorBodyLength);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return BranchRuleWriteErrors.InsufficientPermission(
                $"Writing branch rules on {slug} returned 403 — the branch-rules token needs the " +
                $"Administration (read and write) permission." +
                (providerMessage is null ? string.Empty : $" GitHub: {providerMessage}"));
        }

        int statusCode = (int)response.StatusCode;
        return GitHubErrors.ProviderError(
            $"Writing branch rules on {slug} returned {statusCode}" +
            (providerMessage is null ? "." : $" — {providerMessage}"));
    }

    private sealed record GitHubRulesetSummaryDto(long Id, string? Name, string? SourceType);

    internal sealed record GitHubRulesetRequestDto(
        string Name,
        string Target,
        string Enforcement,
        GitHubRulesetConditionsDto Conditions,
        IReadOnlyList<GitHubRulesetRuleDto> Rules,
        IReadOnlyList<object> BypassActors);

    internal sealed record GitHubRulesetConditionsDto(GitHubRulesetRefNameDto RefName);

    internal sealed record GitHubRulesetRefNameDto(IReadOnlyList<string> Include, IReadOnlyList<string> Exclude);

    internal sealed record GitHubRulesetRuleDto(
        string Type,
        [property: System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        GitHubPullRequestRuleParametersDto? Parameters);

    internal sealed record GitHubPullRequestRuleParametersDto(
        bool DismissStaleReviewsOnPush,
        bool RequireCodeOwnerReview,
        bool RequireLastPushApproval,
        int RequiredApprovingReviewCount,
        bool RequiredReviewThreadResolution);
}
