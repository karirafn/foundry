using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Shared;

namespace Foundry.Modules.Credentials.Infrastructure;

/// <summary>
/// Parsed result of <c>claude auth status --json</c>.
/// Contains the display identity fields needed after a successful OAuth login.
/// </summary>
public sealed record AccountIdentity(string Email, string OrgName, string SubscriptionType)
{
    public const int MaxEmailLength = 254;
    public const int MaxOrgNameLength = 200;
    public const int MaxSubscriptionTypeLength = 64;

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
    /// Fields exceeding their length cap are silently truncated.
    /// </summary>
    public static Result<AccountIdentity> Parse(string json)
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

        string email = Truncate(root["email"]?.GetValue<string>() ?? string.Empty, MaxEmailLength);
        string orgName = Truncate(root["orgName"]?.GetValue<string>() ?? string.Empty, MaxOrgNameLength);
        string subscriptionType = Truncate(
            root["subscriptionType"]?.GetValue<string>() ?? string.Empty,
            MaxSubscriptionTypeLength);

        return new AccountIdentity(email, orgName, subscriptionType);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length > maxLength ? value[..maxLength] : value;
}
