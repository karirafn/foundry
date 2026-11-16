using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>The only HTTP methods Foundry sends to a Package Source: it only ever reads.</summary>
internal enum UpstreamMethod
{
    Get,
    Head,
}

/// <summary>
/// Makes every request Foundry sends to a Package Source: the probe's, and the proxy's. It sends the credential as
/// Basic auth, and follows redirects itself (the <see cref="HttpClient"/> it is built with must not), so the credential
/// goes only to the source URL's origin and never to a host a redirect points at.
/// </summary>
/// <remarks>
/// The response is returned once its headers are read, so a caller can stream the body; the caller disposes it.
/// </remarks>
internal sealed class PackageSourceUpstreamClient(HttpClient httpClient)
{
    /// <summary>The most redirects followed for one request before it fails.</summary>
    internal const int MaxRedirects = 10;

    public async Task<Result<HttpResponseMessage>> SendAsync(
        Uri sourceUrl,
        UpstreamMethod method,
        Uri requestUrl,
        UpstreamCredential? credential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceUrl);
        ArgumentNullException.ThrowIfNull(requestUrl);

        Uri url = requestUrl;

        for (int redirects = 0; ; redirects++)
        {
            if (!IsHttp(url))
            {
                return Result<HttpResponseMessage>.Fail(PackageSourceUpstreamErrors.NotHttp());
            }

            HttpResponseMessage response;
            try
            {
                using HttpRequestMessage request = BuildRequest(method, url, SameOrigin(url, sourceUrl) ? credential : null);
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                return Result<HttpResponseMessage>.Fail(PackageSourceUpstreamErrors.TransportFailed(ex.Message));
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Result<HttpResponseMessage>.Fail(PackageSourceUpstreamErrors.TimedOut());
            }

            if (!IsRedirect(response.StatusCode) || response.Headers.Location is not { } location)
            {
                return Result<HttpResponseMessage>.Ok(response);
            }

            response.Dispose();

            if (redirects >= MaxRedirects)
            {
                return Result<HttpResponseMessage>.Fail(PackageSourceUpstreamErrors.TooManyRedirects());
            }

            url = location.IsAbsoluteUri ? location : new Uri(url, location);
        }
    }

    private static HttpRequestMessage BuildRequest(UpstreamMethod method, Uri url, UpstreamCredential? credential)
    {
        HttpMethod httpMethod = method switch
        {
            UpstreamMethod.Get => HttpMethod.Get,
            UpstreamMethod.Head => HttpMethod.Head,
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Only GET and HEAD reach a package source."),
        };

        HttpRequestMessage request = new(httpMethod, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        if (credential is not null)
        {
            string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credential.Username}:{credential.Token}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }

        return request;
    }

    /// <summary>Whether two URLs share an origin: scheme, host and port.</summary>
    internal static bool SameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port;

    private static bool IsHttp(Uri url) => url.IsAbsoluteUri && url.Scheme is ("http" or "https");

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}

internal static class PackageSourceUpstreamErrors
{
    public const string TransportFailedCode = "PackageSourceUpstream.TransportFailed";
    public const string TimedOutCode = "PackageSourceUpstream.TimedOut";
    public const string TooManyRedirectsCode = "PackageSourceUpstream.TooManyRedirects";
    public const string NotHttpCode = "PackageSourceUpstream.NotHttp";

    public static Error TransportFailed(string message) =>
        new(TransportFailedCode, message);

    public static Error TimedOut() =>
        new(TimedOutCode, "the request timed out");

    public static Error TooManyRedirects() =>
        new(TooManyRedirectsCode, $"more than {PackageSourceUpstreamClient.MaxRedirects} redirects");

    public static Error NotHttp() =>
        new(NotHttpCode, "the URL is not http or https");
}
