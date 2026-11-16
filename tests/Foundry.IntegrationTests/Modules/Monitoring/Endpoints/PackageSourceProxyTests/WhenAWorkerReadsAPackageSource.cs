using System.Net;
using System.Text;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.WebhookTests;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.PackageSourceProxy;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.PackageSourceProxyTests;

/// <summary>
/// The Package Source Proxy (ADR 0112) against a fake upstream: a live run's token reads the sources its repository
/// declares, with Foundry's credential, and nothing else.
/// </summary>
public sealed class WhenAWorkerReadsAPackageSource : IAsyncDisposable
{
    private const int ProxyPort = PackageSourceProxyOptions.DefaultProxyPort;
    private const string Origin = "https://gitlab.com";
    private const string Directory = "/api/v4/groups/42/-/packages/nuget/";
    private const string ServiceIndexUrl = Origin + Directory + "index.json";
    private const string OtherSourceUrl = Origin + "/api/v4/groups/77/-/packages/nuget/index.json";
    private const string AccountName = "My GitLab";

    private const string AccountToken = "glpat-" + "proxy-test-token";

    private const string RegisteredToken = "registered-" + "feed-token";

    private const string ThirdPartyServiceIndexUrl = "https://packages.example/v3/index.json";

    private readonly FakePackageSourceUpstream _upstream = new();
    private readonly FoundryWebAppFactory _factory;

    public WhenAWorkerReadsAPackageSource()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
            services.AddHttpClient<PackageSourceUpstreamClient>()
                .ConfigurePrimaryHttpMessageHandler(() => _upstream));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
        _upstream.Dispose();
    }

    [Fact]
    public async Task WithoutCredentials_IsChallengedThenServedWithTheRunToken()
    {
        // Arrange
        Seeded seeded = await SeedAsync();
        _upstream.Json(ServiceIndexUrl, """{"version":"3.0.0","resources":[]}""");
        string path = ProxyPath(seeded.SourceId, "index.json");

        // Act
        HttpContext challenged = await SendAsync(HttpMethods.Get, path, ProxyPort, authorization: null);
        HttpContext served = await SendAsync(HttpMethods.Get, path, ProxyPort, Basic("foundry-run", seeded.Token));

        // Assert
        challenged.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        challenged.Response.Headers.WWWAuthenticate.ToString().ShouldBe("Basic realm=\"foundry-package-sources\"");
        served.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        _upstream.Requests.ShouldHaveSingleItem().Authorization.ShouldBe(Basic(AccountName, AccountToken));
    }

    [Fact]
    public async Task WithAWrongToken_IsChallenged()
    {
        // Arrange
        Seeded seeded = await SeedAsync();

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Get,
            ProxyPath(seeded.SourceId, "index.json"),
            ProxyPort,
            Basic("foundry-run", "not-the-token"));

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        response.Response.Headers.WWWAuthenticate.ToString().ShouldBe("Basic realm=\"foundry-package-sources\"");
        _upstream.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithTheTokenOfAPublishingRun_IsChallenged()
    {
        // Arrange
        Seeded seeded = await SeedAsync(publishing: true);

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Get,
            ProxyPath(seeded.SourceId, "index.json"),
            ProxyPort,
            Basic("foundry-run", seeded.Token));

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        _upstream.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ForASourceTheRunsRepositoryDoesNotDeclare_IsChallenged()
    {
        // Arrange
        Seeded seeded = await SeedAsync();
        PackageSource other = await SeedSourceAsync(OtherSourceUrl);

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Get,
            $"/package-sources/{other.Id.Value}/api/v4/groups/77/-/packages/nuget/index.json",
            ProxyPort,
            Basic("foundry-run", seeded.Token));

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        _upstream.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/api/v4/projects/1/repository/files")]
    [InlineData("/api/v4/groups/42/-/packages/nuget/../../../../projects/1")]
    [InlineData("/api/v4/groups/42/-/packages/nugetx/index.json")]
    public async Task ForAPathOutsideTheServiceIndexDirectory_Returns404(string upstreamPath)
    {
        // Arrange
        Seeded seeded = await SeedAsync();

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Get,
            $"/package-sources/{seeded.SourceId}{upstreamPath}",
            ProxyPort,
            Basic("foundry-run", seeded.Token));

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        _upstream.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithAPost_Returns405()
    {
        // Arrange
        Seeded seeded = await SeedAsync();

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Post,
            ProxyPath(seeded.SourceId, "index.json"),
            ProxyPort,
            Basic("foundry-run", seeded.Token));

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status405MethodNotAllowed);
        _upstream.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task AJsonBody_HasItsUpstreamUrlsRepointedAtTheProxyAndOtherUrlsLeftAlone()
    {
        // Arrange
        Seeded seeded = await SeedAsync();
        _upstream.Json(
            ServiceIndexUrl,
            $$"""
            {
              "version": "3.0.0",
              "resources": [
                { "@id": "{{Origin}}{{Directory}}query", "@type": "SearchQueryService" },
                { "@id": "{{Origin}}{{Directory}}metadata?x=1", "@type": "RegistrationsBaseUrl" },
                { "@id": "{{Origin}}/api/v4/projects/1", "@type": "Other" },
                { "@id": "https://cdn.example{{Directory}}download", "@type": "PackageBaseAddress" }
              ]
            }
            """);

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Get,
            ProxyPath(seeded.SourceId, "index.json"),
            ProxyPort,
            Basic("foundry-run", seeded.Token));

        // Assert
        string body = await ReadBodyAsync(response);
        string proxyBase = $"http://host.docker.internal:{ProxyPort}/package-sources/{seeded.SourceId}";
        body.ShouldSatisfyAllConditions(
            () => body.ShouldContain($"\"{proxyBase}{Directory}query\""),
            () => body.ShouldContain($"\"{proxyBase}{Directory}metadata?x=1\""),
            () => body.ShouldContain($"\"{Origin}/api/v4/projects/1\""),
            () => body.ShouldContain($"\"https://cdn.example{Directory}download\""));
        response.Response.ContentLength.ShouldBe(Encoding.UTF8.GetByteCount(body));
    }

    [Fact]
    public async Task ARequestWithAQuery_ReachesUpstreamWithTheQuery()
    {
        // Arrange
        Seeded seeded = await SeedAsync();
        _upstream.Json(Origin + Directory + "query?q=&take=1", """{"data":[]}""");

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Get,
            ProxyPath(seeded.SourceId, "query"),
            ProxyPort,
            Basic("foundry-run", seeded.Token),
            query: "?q=&take=1");

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        _upstream.Requests.ShouldHaveSingleItem().Url.AbsoluteUri.ShouldBe(Origin + Directory + "query?q=&take=1");
    }

    [Fact]
    public async Task ABinaryBody_StreamsUnchanged()
    {
        // Arrange
        Seeded seeded = await SeedAsync();
        byte[] package = [.. Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251))];
        _upstream.Bytes(Origin + Directory + "download/foo/1.0.0/foo.1.0.0.nupkg", package);

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Get,
            ProxyPath(seeded.SourceId, "download/foo/1.0.0/foo.1.0.0.nupkg"),
            ProxyPort,
            Basic("foundry-run", seeded.Token));

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        response.Response.ContentType.ShouldBe("application/octet-stream");
        using MemoryStream received = new();
        await response.Response.Body.CopyToAsync(received, TestContext.Current.CancellationToken);
        received.ToArray().ShouldBe(package);
    }

    [Fact]
    public async Task AnUpstream401_Becomes502NamingTheSource()
    {
        // Arrange
        Seeded seeded = await SeedAsync();
        _upstream.Status(ServiceIndexUrl, HttpStatusCode.Unauthorized);

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Get,
            ProxyPath(seeded.SourceId, "index.json"),
            ProxyPort,
            Basic("foundry-run", seeded.Token));

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status502BadGateway);
        string body = await ReadBodyAsync(response);
        body.ShouldContain(ServiceIndexUrl);
        body.TrimEnd().ShouldNotContain('\n');
        response.Response.Headers.WWWAuthenticate.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUpstream404_PassesThrough()
    {
        // Arrange
        Seeded seeded = await SeedAsync();

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Get,
            ProxyPath(seeded.SourceId, "missing.json"),
            ProxyPort,
            Basic("foundry-run", seeded.Token));

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        _upstream.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task OnTheMainPort_Returns404()
    {
        // Arrange
        Seeded seeded = await SeedAsync();
        _upstream.Json(ServiceIndexUrl, """{"version":"3.0.0","resources":[]}""");

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Get,
            ProxyPath(seeded.SourceId, "index.json"),
            WebhookTestHost.MainPort,
            Basic("foundry-run", seeded.Token));

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        _upstream.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithARegisteredCredential_SendsItInsteadOfTheAccount()
    {
        // Arrange
        Seeded seeded = await SeedAsync(registered: ("feed-reader", RegisteredToken));
        _upstream.Json(ServiceIndexUrl, """{"version":"3.0.0","resources":[]}""");

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Get,
            ProxyPath(seeded.SourceId, "index.json"),
            ProxyPort,
            Basic("foundry-run", seeded.Token));

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        _upstream.Requests.ShouldHaveSingleItem().Authorization.ShouldBe(Basic("feed-reader", RegisteredToken));
    }

    [Fact]
    public async Task WithARegisteredCredentialOnAnotherHost_SendsIt()
    {
        // Arrange
        Seeded seeded = await SeedAsync(
            sourceUrl: ThirdPartyServiceIndexUrl,
            registered: ("feed-reader", RegisteredToken));
        _upstream.Json(ThirdPartyServiceIndexUrl, """{"version":"3.0.0","resources":[]}""");

        // Act
        HttpContext response = await SendAsync(
            HttpMethods.Get,
            $"/package-sources/{seeded.SourceId}/v3/index.json",
            ProxyPort,
            Basic("foundry-run", seeded.Token));

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        _upstream.Requests.ShouldHaveSingleItem().Authorization.ShouldBe(Basic("feed-reader", RegisteredToken));
    }

    [Fact]
    public async Task AnApiPathOnTheProxyPort_Returns404()
    {
        // Act
        HttpContext response = await SendAsync(HttpMethods.Get, "/api/accounts", ProxyPort, authorization: null);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    private static string ProxyPath(Guid sourceId, string fileUnderDirectory) =>
        $"/package-sources/{sourceId}{Directory}{fileUnderDirectory}";

    private static string Basic(string username, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        using StreamReader reader = new(context.Response.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    private async Task<HttpContext> SendAsync(string method, string path, int localPort, string? authorization, string query = "")
    {
        return await _factory.Server.SendAsync(context =>
        {
            context.Request.Method = method;
            context.Request.Path = path;
            context.Request.QueryString = new QueryString(query.Length == 0 ? null : query);
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost", localPort);
            context.Connection.LocalPort = localPort;

            if (authorization is not null)
            {
                context.Request.Headers.Authorization = authorization;
            }
        });
    }

    /// <summary>
    /// A GitLab repository declaring the source, the account that covers it, and one of its runs, live unless
    /// <paramref name="publishing"/>.
    /// </summary>
    private async Task<Seeded> SeedAsync(
        bool publishing = false,
        string sourceUrl = ServiceIndexUrl,
        (string Username, string Token)? registered = null)
    {
        Guid accountId = await AccountSeeder.SeedGitLabAccountAsync(_factory, AccountName, AccountToken);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

        PackageSource source = await SeedSourceAsync(sourceUrl, registered);

        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "gitlab.com",
            null,
            position: 0).ValueOrThrow();
        DeclaredPackageSource declared = DeclaredPackageSource.Create("group", sourceUrl).ValueOrThrow();
        repository.RecordRootListing(new RootNuGetConfig.Declared("sha", "<configuration />", [declared]), listingETag: null);
        dbContext.Set<MonitoredRepository>().Add(repository);

        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordPreparedBase(new RunBase(CommitSha.From("1111111111111111111111111111111111111111"), RunBranchHead: null), repository.Id);
        ActiveRun active = starting.Activate(ContainerId.From("container-1"), BranchName.From("feat/1-proxy"), repository.Id);
        WorkerRun run = publishing
            ? active.BeginPublishing(
                new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow),
                containerOutput: "log tail",
                RunVerdict.Done(),
                new RunPullRequestText("Title", "Body"))
            : active;
        dbContext.Set<WorkerRun>().Add(run);

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new Seeded(source.Id.Value, starting.SourceToken!.Value);
    }

    private async Task<PackageSource> SeedSourceAsync(string url, (string Username, string Token)? registered = null)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        PackageSource source = PackageSource.Create(new Uri(url));
        if (registered is { } credential)
        {
            source.SetCredential(credential.Username, credential.Token, DateTimeOffset.UtcNow);
        }

        dbContext.Set<PackageSource>().Add(source);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return source;
    }

    private sealed record Seeded(Guid SourceId, string Token);
}
