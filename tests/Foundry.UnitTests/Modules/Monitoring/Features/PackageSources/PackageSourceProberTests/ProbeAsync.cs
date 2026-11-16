using System.Net;
using System.Text;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.PackageSources;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSources.PackageSourceProberTests;

public sealed class ProbeAsync
{
    private const string IndexUrl = "https://feed.example/packages/index.json";
    private const string SearchUrl = "https://feed.example/packages/query";
    private const string SearchRequestUrl = SearchUrl + "?q=&take=1";

    private static readonly UpstreamCredential Credential = new("deploy-bot", "secret-token");

    private static readonly string ExpectedAuthorization =
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("deploy-bot:secret-token"));

    private static async Task<PackageSourceVerdict.Probed> RunProbeAsync(
        FakeUpstream upstream,
        UpstreamCredential? credential = null,
        string indexUrl = IndexUrl)
    {
        using HttpClient httpClient = new(upstream);
        PackageSourceProber sut = new(new PackageSourceUpstreamClient(httpClient));
        return await sut.ProbeAsync(new Uri(indexUrl), credential, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenIndexAndSearchAnswer2xx_IsReadable()
    {
        // Arrange
        FakeUpstream upstream = new FakeUpstream()
            .Json(IndexUrl, FakeUpstream.ServiceIndex(SearchUrl))
            .Json(SearchRequestUrl, """{ "totalHits": 0, "data": [] }""");

        // Act
        PackageSourceVerdict.Probed verdict = await RunProbeAsync(upstream, Credential);

        // Assert
        verdict.ShouldBeOfType<PackageSourceVerdict.Readable>();
    }

    [Fact]
    public async Task WithACredential_SendsItAsBasicAuthToBothRequests()
    {
        // Arrange
        FakeUpstream upstream = new FakeUpstream()
            .Json(IndexUrl, FakeUpstream.ServiceIndex(SearchUrl))
            .Json(SearchRequestUrl, "{}");

        // Act
        await RunProbeAsync(upstream, Credential);

        // Assert
        upstream.Requests.Select(r => (r.Url.AbsoluteUri, r.Method, r.Authorization)).ShouldBe(
        [
            (IndexUrl, HttpMethod.Get, ExpectedAuthorization),
            (SearchRequestUrl, HttpMethod.Get, ExpectedAuthorization),
        ]);
    }

    [Fact]
    public async Task WithoutACredential_SendsNoAuthorization()
    {
        // Arrange
        FakeUpstream upstream = new FakeUpstream()
            .Json(IndexUrl, FakeUpstream.ServiceIndex(SearchUrl))
            .Json(SearchRequestUrl, "{}");

        // Act
        PackageSourceVerdict.Probed verdict = await RunProbeAsync(upstream, credential: null);

        // Assert
        verdict.ShouldBeOfType<PackageSourceVerdict.Readable>();
        upstream.Requests.ShouldAllBe(r => r.Authorization == null);
    }

    [Fact]
    public async Task WhenTheIndexAnswers401_IsUnreadableWithThatStatus()
    {
        // Arrange
        FakeUpstream upstream = new FakeUpstream().Status(IndexUrl, HttpStatusCode.Unauthorized);

        // Act
        PackageSourceVerdict.Probed verdict = await RunProbeAsync(upstream, Credential);

        // Assert
        verdict.ShouldBe(new PackageSourceVerdict.Unreadable(401, "service index returned 401"));
    }

    [Fact]
    public async Task WhenTheSearchAnswers403_IsUnreadableWithThatStatus()
    {
        // Arrange
        FakeUpstream upstream = new FakeUpstream()
            .Json(IndexUrl, FakeUpstream.ServiceIndex(SearchUrl))
            .Status(SearchRequestUrl, HttpStatusCode.Forbidden);

        // Act
        PackageSourceVerdict.Probed verdict = await RunProbeAsync(upstream, Credential);

        // Assert
        verdict.ShouldBe(new PackageSourceVerdict.Unreadable(403, "search returned 403"));
    }

    [Fact]
    public async Task WhenTheIndexAnswers429_IsUnreadableWithThatStatus()
    {
        // Arrange
        FakeUpstream upstream = new FakeUpstream().Status(IndexUrl, HttpStatusCode.TooManyRequests);

        // Act
        PackageSourceVerdict.Probed verdict = await RunProbeAsync(upstream, Credential);

        // Assert
        verdict.ShouldBe(new PackageSourceVerdict.Unreadable(429, "service index returned 429"));
    }

    [Fact]
    public async Task WhenTheIndexBodyIsNotJson_IsUnreadableAsNotAServiceIndex()
    {
        // Arrange
        FakeUpstream upstream = new FakeUpstream().Json(IndexUrl, "<html>Sign in</html>");

        // Act
        PackageSourceVerdict.Probed verdict = await RunProbeAsync(upstream, Credential);

        // Assert
        verdict.ShouldBe(new PackageSourceVerdict.Unreadable(200, "not a NuGet v3 service index"));
    }

    [Fact]
    public async Task WhenTheIndexHasNoSearchResource_IsUnreadableAsNotAServiceIndex()
    {
        // Arrange
        FakeUpstream upstream = new FakeUpstream().Json(
            IndexUrl,
            """{ "version": "3.0.0", "resources": [ { "@id": "https://feed.example/r/", "@type": "RegistrationsBaseUrl" } ] }""");

        // Act
        PackageSourceVerdict.Probed verdict = await RunProbeAsync(upstream, Credential);

        // Assert
        verdict.ShouldBe(new PackageSourceVerdict.Unreadable(200, "not a NuGet v3 service index"));
    }

    [Fact]
    public async Task WhenTheIndexRequestFailsInTransport_IsUnreadableWithoutAStatus()
    {
        // Arrange
        FakeUpstream upstream = new FakeUpstream().Throw(IndexUrl);

        // Act
        PackageSourceVerdict.Probed verdict = await RunProbeAsync(upstream, Credential);

        // Assert
        PackageSourceVerdict.Unreadable unreadable = verdict.ShouldBeOfType<PackageSourceVerdict.Unreadable>();
        unreadable.ShouldSatisfyAllConditions(
            () => unreadable.HttpStatus.ShouldBeNull(),
            () => unreadable.Reason.ShouldStartWith("service index request failed: "));
    }

    [Fact]
    public async Task WhenTheIndexRedirectsToAnotherHost_DoesNotForwardTheCredential()
    {
        // Arrange
        const string MovedIndexUrl = "https://storage.example/feed/index.json";
        FakeUpstream upstream = new FakeUpstream()
            .Redirect(IndexUrl, MovedIndexUrl)
            .Json(MovedIndexUrl, FakeUpstream.ServiceIndex(SearchUrl))
            .Json(SearchRequestUrl, "{}");

        // Act
        PackageSourceVerdict.Probed verdict = await RunProbeAsync(upstream, Credential);

        // Assert
        verdict.ShouldBeOfType<PackageSourceVerdict.Readable>();
        upstream.Requests.Select(r => (r.Url.AbsoluteUri, r.Authorization)).ShouldBe(
        [
            (IndexUrl, ExpectedAuthorization),
            (MovedIndexUrl, null),
            (SearchRequestUrl, ExpectedAuthorization),
        ]);
    }

    [Fact]
    public async Task WhenTheIndexRedirectsWithinItsOrigin_KeepsTheCredential()
    {
        // Arrange
        const string MovedIndexUrl = "https://feed.example/v3/index.json";
        FakeUpstream upstream = new FakeUpstream()
            .Redirect(IndexUrl, "/v3/index.json")
            .Json(MovedIndexUrl, FakeUpstream.ServiceIndex(SearchUrl))
            .Json(SearchRequestUrl, "{}");

        // Act
        await RunProbeAsync(upstream, Credential);

        // Assert
        upstream.Requests[1].ShouldSatisfyAllConditions(
            request => request.Url.AbsoluteUri.ShouldBe(MovedIndexUrl),
            request => request.Authorization.ShouldBe(ExpectedAuthorization));
    }

    [Fact]
    public async Task WhenTheSearchResourceIsOnAnotherHost_DoesNotSendItTheCredential()
    {
        // Arrange
        const string ForeignSearchUrl = "https://search.example/query";
        FakeUpstream upstream = new FakeUpstream()
            .Json(IndexUrl, FakeUpstream.ServiceIndex(ForeignSearchUrl))
            .Json(ForeignSearchUrl + "?q=&take=1", "{}");

        // Act
        await RunProbeAsync(upstream, Credential);

        // Assert
        upstream.Requests[1].Authorization.ShouldBeNull();
    }
}
