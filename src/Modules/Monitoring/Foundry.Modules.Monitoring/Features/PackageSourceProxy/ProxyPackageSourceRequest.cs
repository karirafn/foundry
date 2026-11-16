using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Foundry.Modules.Monitoring.Features.PackageSourceProxy;

/// <summary>
/// The Package Source Proxy (ADR 0112): serves a live run's read-only requests for a Package Source its repository
/// declares, forwarding them upstream with Foundry's credential. The worker presents its Run Source Token as the Basic
/// password; NuGet sends it only after a Basic challenge, so a request without credentials gets one.
/// </summary>
internal static class ProxyPackageSourceRequest
{
    internal const string Realm = "foundry-package-sources";

    internal const string ChallengeHeader = $"Basic realm=\"{Realm}\"";

    /// <summary>The upstream response headers passed through to the worker; everything else stays upstream.</summary>
    private static readonly string[] ForwardedResponseHeaders =
    [
        HeaderNames.CacheControl,
        HeaderNames.ContentDisposition,
        HeaderNames.ContentEncoding,
        HeaderNames.ContentType,
        HeaderNames.ETag,
        HeaderNames.Expires,
        HeaderNames.LastModified,
    ];

    internal sealed class Handler(
        DbContext dbContext,
        IRunSourceTokenQueries tokenQueries,
        ICredentialResolver credentialResolver,
        PackageSourceUpstreamClient upstreamClient,
        IOptions<PackageSourceProxyOptions> options,
        ILogger<Handler> logger)
    {
        public async Task HandleAsync(HttpContext context, Guid sourceId, CancellationToken cancellationToken)
        {
            HttpRequest request = context.Request;
            HttpResponse response = context.Response;

            UpstreamMethod? method = MethodOf(request.Method);
            if (method is not { } upstreamMethod)
            {
                response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                response.Headers.Allow = "GET, HEAD";
                return;
            }

            if (RunSourceTokenOf(request) is not { } token)
            {
                Challenge(response);
                return;
            }

            Guid? runRepositoryId = await tokenQueries.FindLiveRunRepositoryAsync(RunSourceTokenHash.Of(token), cancellationToken);
            if (runRepositoryId is not { } repositoryId)
            {
                Challenge(response);
                return;
            }

            PackageSourceId packageSourceId = PackageSourceId.From(sourceId);
            MonitoredRepositoryId monitoredRepositoryId = MonitoredRepositoryId.From(repositoryId);
            PackageSource? source = await dbContext.Set<PackageSource>()
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == packageSourceId, cancellationToken);
            MonitoredRepository? repository = await dbContext.Set<MonitoredRepository>()
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == monitoredRepositoryId, cancellationToken);

            if (source is null || repository is null || !Declares(repository, source))
            {
                Challenge(response);
                return;
            }

            ProxiedPackageSource proxied = new(packageSourceId, source.ServiceIndexUrl, options.Value.WorkerBaseUrl);
            string below = BelowSourceId(request.Path, sourceId);
            if (proxied.UpstreamUrlFor(below, request.QueryString.ToUriComponent()) is not { } upstreamUrl)
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            UpstreamCredential? credential = await CredentialForAsync(repository, source.ServiceIndexUrl, cancellationToken);

            Result<HttpResponseMessage> sent = await upstreamClient.SendAsync(
                source.ServiceIndexUrl,
                upstreamMethod,
                upstreamUrl,
                credential,
                cancellationToken);

            if (sent is Result<HttpResponseMessage>.Failure failure)
            {
                logger.LogWarning(
                    "Package source proxy could not reach {Url}: {Reason}",
                    source.ServiceIndexUrl,
                    failure.Error.Message);
                await BadGatewayAsync(response, $"Foundry could not reach the package source {source.ServiceIndexUrl}.", cancellationToken);
                return;
            }

            using HttpResponseMessage upstream = ((Result<HttpResponseMessage>.Success)sent).Value;

            if (upstream.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Passed through, an upstream 401 would read to NuGet as a challenge to the worker's own credentials.
                logger.LogWarning("Package source {Url} refused Foundry's credential.", source.ServiceIndexUrl);
                await BadGatewayAsync(response, $"The package source {source.ServiceIndexUrl} refused Foundry's credential.", cancellationToken);
                return;
            }

            await RelayAsync(upstream, proxied, upstreamMethod, response, cancellationToken);
        }

        private async Task<UpstreamCredential?> CredentialForAsync(
            MonitoredRepository repository,
            Uri sourceUrl,
            CancellationToken cancellationToken)
        {
            if (!PackageSourceAccess.UsesAccount(sourceUrl, repository.Host))
            {
                return null;
            }

            Credential? account = await credentialResolver.ResolveAsync(repository.Host, repository.Slug, cancellationToken);
            return account?.TokenUsability is TokenUsability.Usable usable
                ? new UpstreamCredential(account.Name.Value, usable.Token)
                : null;
        }

        private static async Task RelayAsync(
            HttpResponseMessage upstream,
            ProxiedPackageSource proxied,
            UpstreamMethod method,
            HttpResponse response,
            CancellationToken cancellationToken)
        {
            response.StatusCode = (int)upstream.StatusCode;
            CopyHeaders(upstream, response);

            if (method == UpstreamMethod.Head)
            {
                return;
            }

            await using Stream stream = await upstream.Content.ReadAsStreamAsync(cancellationToken);

            if (IsRewritableJson(upstream.Content.Headers))
            {
                (byte[] head, bool complete) = await ReadBoundedAsync(stream, cancellationToken);
                if (complete)
                {
                    byte[] rewritten = proxied.RewriteJson(head) ?? head;
                    response.ContentLength = rewritten.Length;
                    await response.Body.WriteAsync(rewritten, cancellationToken);
                    return;
                }

                // Too large to rewrite: what was read goes out first, and the rest streams as it came.
                await response.Body.WriteAsync(head, cancellationToken);
            }

            await stream.CopyToAsync(response.Body, cancellationToken);
        }

        private static void CopyHeaders(HttpResponseMessage upstream, HttpResponse response)
        {
            foreach (string name in ForwardedResponseHeaders)
            {
                if (upstream.Headers.TryGetValues(name, out IEnumerable<string>? values)
                    || upstream.Content.Headers.TryGetValues(name, out values))
                {
                    response.Headers[name] = values.ToArray();
                }
            }

            // A rewritten body has a new length; any other body keeps the upstream one.
            if (upstream.Content.Headers.ContentLength is { } length && !IsRewritableJson(upstream.Content.Headers))
            {
                response.ContentLength = length;
            }
        }

        /// <summary>
        /// Reads <paramref name="stream"/> up to <see cref="MonitoringModule.MaxResponseContentBufferSize"/>: the bytes
        /// read, and whether that was the whole body.
        /// </summary>
        private static async Task<(byte[] Head, bool Complete)> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
        {
            using MemoryStream buffer = new();
            byte[] chunk = new byte[81920];
            int read;

            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
                if (buffer.Length > MonitoringModule.MaxResponseContentBufferSize)
                {
                    return (buffer.ToArray(), false);
                }
            }

            return (buffer.ToArray(), true);
        }

        private static async Task BadGatewayAsync(HttpResponse response, string message, CancellationToken cancellationToken)
        {
            response.StatusCode = StatusCodes.Status502BadGateway;
            response.ContentType = "text/plain; charset=utf-8";
            await response.WriteAsync(message + "\n", cancellationToken);
        }
    }

    internal static class Endpoint
    {
        public static void Map(IEndpointRouteBuilder routes)
        {
            routes.Map(PackageSourceProxyOptions.PathPrefix + "/{sourceId:guid}/{**upstreamPath}", static (
                    HttpContext context,
                    Guid sourceId,
                    Handler handler,
                    CancellationToken cancellationToken) => handler.HandleAsync(context, sourceId, cancellationToken))
                .ExcludeFromDescription();
        }
    }

    private static UpstreamMethod? MethodOf(string method) =>
        HttpMethods.IsGet(method) ? UpstreamMethod.Get
        : HttpMethods.IsHead(method) ? UpstreamMethod.Head
        : null;

    private static void Challenge(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.Headers.WWWAuthenticate = ChallengeHeader;
    }

    /// <summary>The Basic password of the request, which is the Run Source Token; the username is ignored.</summary>
    private static string? RunSourceTokenOf(HttpRequest request)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out AuthenticationHeaderValue? header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || header.Parameter is not { } encoded)
        {
            return null;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return null;
        }

        int separator = decoded.IndexOf(':', StringComparison.Ordinal);
        string password = separator < 0 ? string.Empty : decoded[(separator + 1)..];
        return password.Length == 0 ? null : password;
    }

    private static bool Declares(MonitoredRepository repository, PackageSource source) =>
        repository.DeclaredPackageSources.Any(declared =>
            string.Equals(declared.ServiceIndexUrl.AbsoluteUri, source.ServiceIndexUrl.AbsoluteUri, StringComparison.Ordinal));

    /// <summary>The escaped request path below <c>/package-sources/&lt;sourceId&gt;</c>, starting with <c>/</c>.</summary>
    private static string BelowSourceId(PathString path, Guid sourceId)
    {
        path.StartsWithSegments($"{PackageSourceProxyOptions.PathPrefix}/{sourceId}", StringComparison.OrdinalIgnoreCase, out PathString remaining);
        return remaining.HasValue ? remaining.ToUriComponent() : "/";
    }

    private static bool IsRewritableJson(HttpContentHeaders headers)
    {
        if (headers.ContentEncoding.Count > 0 || headers.ContentType?.MediaType is not { } mediaType)
        {
            return false;
        }

        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }
}
