using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.ValueObjects;

/// <summary>
/// The public base URL GitHub and GitLab deliver webhooks to: an absolute <c>https</c> URL without query,
/// fragment or userinfo. A trailing slash is trimmed so the payload URL is built as
/// <c>&lt;base&gt;/webhooks/{github|gitlab}/&lt;hookId&gt;</c>.
/// </summary>
public sealed record WebhookPublicBaseUrl
{
    private WebhookPublicBaseUrl(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<WebhookPublicBaseUrl> Create(string value)
    {
        string trimmed = value.Trim();

        if (trimmed.Length > GlobalSettings.MaxWebhookPublicBaseUrlLength
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || string.IsNullOrEmpty(uri.Host))
        {
            return Result<WebhookPublicBaseUrl>.Fail(SettingsErrors.InvalidWebhookPublicBaseUrl);
        }

        return Result<WebhookPublicBaseUrl>.Ok(new WebhookPublicBaseUrl(trimmed.TrimEnd('/')));
    }

    // Stored values were normalized when written; a row that predates normalization is wrapped as-is
    // rather than failing the load.
    internal static WebhookPublicBaseUrl Restore(string storedValue) => new(storedValue);
}
