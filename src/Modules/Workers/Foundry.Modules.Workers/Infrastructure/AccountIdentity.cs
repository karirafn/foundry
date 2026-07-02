using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Shared;

namespace Foundry.Modules.Workers.Infrastructure;

/// <summary>
/// Parsed result of <c>claude auth status --json</c>.
/// Contains the display identity fields needed after a successful OAuth login.
/// </summary>
internal sealed record AccountIdentity(string Email, string OrgName, string SubscriptionType)
{
    private static readonly Error NotLoggedIn = new(
        "AccountIdentity.NotLoggedIn",
        "The auth status response indicates the user is not logged in.");

    private static readonly Error InvalidJson = new(
        "AccountIdentity.InvalidJson",
        "The auth status response could not be parsed as valid JSON.");

    private static readonly Error MissingFields = new(
        "AccountIdentity.MissingFields",
        "The auth status response is missing required fields.");

    /// <summary>
    /// Parses the JSON output of <c>claude auth status --json</c>.
    /// Returns a failure when the JSON is invalid or <c>loggedIn</c> is <c>false</c>.
    /// </summary>
    internal static Result<AccountIdentity> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result<AccountIdentity>.Fail(InvalidJson);
        }

        JsonObject? root;

        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return Result<AccountIdentity>.Fail(InvalidJson);
        }

        if (root is null)
        {
            return Result<AccountIdentity>.Fail(InvalidJson);
        }

        if (!root.TryGetPropertyValue("loggedIn", out JsonNode? loggedInNode))
        {
            return Result<AccountIdentity>.Fail(MissingFields);
        }

        if (loggedInNode?.GetValue<bool>() is not true)
        {
            return Result<AccountIdentity>.Fail(NotLoggedIn);
        }

        string email = root["email"]?.GetValue<string>() ?? string.Empty;
        string orgName = root["orgName"]?.GetValue<string>() ?? string.Empty;
        string subscriptionType = root["subscriptionType"]?.GetValue<string>() ?? string.Empty;

        return new AccountIdentity(email, orgName, subscriptionType);
    }
}
