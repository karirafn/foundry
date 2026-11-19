using System.Globalization;
using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.PackageSourceProxy;

/// <summary>Whether a GitLab group feed's registration index lists a requested project download.</summary>
internal abstract record DownloadConfirmation
{
    private DownloadConfirmation()
    {
    }

    internal sealed record Listed : DownloadConfirmation;

    /// <summary>Not listed, or the feed's answer could not be read as a registration (not JSON, or too large).</summary>
    internal sealed record NotListed : DownloadConfirmation;

    /// <summary>The source answered 401 to Foundry's credential.</summary>
    internal sealed record CredentialRefused : DownloadConfirmation;

    /// <summary>The source could not be reached, or answered with another non-success status.</summary>
    internal sealed record Unreadable(string Reason) : DownloadConfirmation;
}

/// <summary>
/// Confirms a GitLab group feed's project download against the feed's own registration index, so the credential
/// reaches only the packages the feed lists. The index is read at <c>&lt;RegistrationsBaseUrl&gt;/{id}/index.json</c>,
/// following page <c>@id</c>s when a page carries no inline items, and lists the download when some
/// <c>packageContent</c> is the requested URL.
/// </summary>
internal static class GroupFeedDownloadConfirmation
{
    private const string PackageContentProperty = "packageContent";

    public static async Task<DownloadConfirmation> ConfirmAsync(
        PackageSourceUpstreamClient upstreamClient,
        ProxiedPackageSource proxied,
        Uri serviceIndexUrl,
        ProxiedRequest.GroupFeedDownload download,
        UpstreamCredential? credential,
        CancellationToken cancellationToken)
    {
        Fetched serviceIndex = await FetchAsync(upstreamClient, serviceIndexUrl, serviceIndexUrl, credential, cancellationToken);
        if (serviceIndex is Fetched.Settled { Outcome: var serviceIndexOutcome })
        {
            return serviceIndexOutcome;
        }

        Uri? registrationUrl;
        using (JsonDocument indexDocument = ((Fetched.Json)serviceIndex).Document)
        {
            registrationUrl = RegistrationUrlOf(indexDocument.RootElement, download.PackageId, proxied);
        }

        if (registrationUrl is null)
        {
            return new DownloadConfirmation.NotListed();
        }

        Fetched registration = await FetchAsync(upstreamClient, serviceIndexUrl, registrationUrl, credential, cancellationToken);
        if (registration is Fetched.Settled { Outcome: var registrationOutcome })
        {
            return registrationOutcome;
        }

        List<Uri> pages;
        using (JsonDocument registrationDocument = ((Fetched.Json)registration).Document)
        {
            if (Lists(registrationDocument.RootElement, download.Url))
            {
                return new DownloadConfirmation.Listed();
            }

            pages = PagesWithoutItems(registrationDocument.RootElement, proxied);
        }

        foreach (Uri pageUrl in pages)
        {
            Fetched page = await FetchAsync(upstreamClient, serviceIndexUrl, pageUrl, credential, cancellationToken);
            if (page is Fetched.Settled { Outcome: var pageOutcome })
            {
                return pageOutcome;
            }

            using JsonDocument pageDocument = ((Fetched.Json)page).Document;
            if (Lists(pageDocument.RootElement, download.Url))
            {
                return new DownloadConfirmation.Listed();
            }
        }

        return new DownloadConfirmation.NotListed();
    }

    /// <summary>The registration index URL for <paramref name="packageId"/>, when the service index names a registrations base under the source's directory.</summary>
    private static Uri? RegistrationUrlOf(JsonElement serviceIndex, string packageId, ProxiedPackageSource proxied)
    {
        if (serviceIndex.ValueKind != JsonValueKind.Object
            || !serviceIndex.TryGetProperty("resources", out JsonElement resources)
            || resources.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement resource in resources.EnumerateArray())
        {
            if (resource.ValueKind != JsonValueKind.Object
                || StringProperty(resource, "@type") is not { } type
                || !(type == "RegistrationsBaseUrl" || type.StartsWith("RegistrationsBaseUrl/", StringComparison.Ordinal))
                || StringProperty(resource, "@id") is not { } baseUrl)
            {
                continue;
            }

            string id = packageId.ToLower(CultureInfo.InvariantCulture);
            if (Uri.TryCreate($"{baseUrl.TrimEnd('/')}/{id}/index.json", UriKind.Absolute, out Uri? url)
                && proxied.IsUnderDirectory(url))
            {
                return url;
            }
        }

        return null;
    }

    /// <summary>The <c>@id</c> of each registration page that carries no inline items and lies under the source's directory.</summary>
    private static List<Uri> PagesWithoutItems(JsonElement registration, ProxiedPackageSource proxied)
    {
        List<Uri> pages = [];
        if (registration.ValueKind != JsonValueKind.Object
            || !registration.TryGetProperty("items", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return pages;
        }

        foreach (JsonElement page in items.EnumerateArray())
        {
            if (page.ValueKind == JsonValueKind.Object
                && !page.TryGetProperty("items", out _)
                && StringProperty(page, "@id") is { } pageId
                && Uri.TryCreate(pageId, UriKind.Absolute, out Uri? url)
                && proxied.IsUnderDirectory(url))
            {
                pages.Add(url);
            }
        }

        return pages;
    }

    /// <summary>Whether any <c>packageContent</c> in <paramref name="element"/> is <paramref name="download"/>: same origin, path and query.</summary>
    private static bool Lists(JsonElement element, Uri download)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.NameEquals(PackageContentProperty)
                        && property.Value.ValueKind == JsonValueKind.String
                        && Uri.TryCreate(property.Value.GetString(), UriKind.Absolute, out Uri? listed)
                        && PackageSourceUpstreamClient.SameOrigin(listed, download)
                        && string.Equals(listed.PathAndQuery, download.PathAndQuery, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (Lists(property.Value, download))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (Lists(item, download))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task<Fetched> FetchAsync(
        PackageSourceUpstreamClient upstreamClient,
        Uri serviceIndexUrl,
        Uri url,
        UpstreamCredential? credential,
        CancellationToken cancellationToken)
    {
        Result<HttpResponseMessage> sent = await upstreamClient.SendAsync(
            serviceIndexUrl,
            UpstreamMethod.Get,
            url,
            credential,
            cancellationToken);

        if (sent is Result<HttpResponseMessage>.Failure failure)
        {
            return new Fetched.Settled(new DownloadConfirmation.Unreadable(failure.Error.Message));
        }

        using HttpResponseMessage response = ((Result<HttpResponseMessage>.Success)sent).Value;

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new Fetched.Settled(new DownloadConfirmation.CredentialRefused());
        }

        if (!response.IsSuccessStatusCode)
        {
            return new Fetched.Settled(new DownloadConfirmation.Unreadable($"HTTP {(int)response.StatusCode} from {url}"));
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        (byte[] body, bool complete) = await ProxyPackageSourceRequest.ReadBoundedAsync(stream, cancellationToken);
        if (!complete)
        {
            return new Fetched.Settled(new DownloadConfirmation.NotListed());
        }

        try
        {
            return new Fetched.Json(JsonDocument.Parse(body));
        }
        catch (JsonException)
        {
            return new Fetched.Settled(new DownloadConfirmation.NotListed());
        }
    }

    /// <summary>One upstream read: a JSON document to look in, or the confirmation it already settles.</summary>
    private abstract record Fetched
    {
        private Fetched()
        {
        }

        /// <summary>The caller disposes <paramref name="Document"/>.</summary>
        public sealed record Json(JsonDocument Document) : Fetched;

        public sealed record Settled(DownloadConfirmation Outcome) : Fetched;
    }
}
