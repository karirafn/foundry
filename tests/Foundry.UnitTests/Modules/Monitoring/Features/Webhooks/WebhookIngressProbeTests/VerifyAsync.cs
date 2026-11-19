using System.Net;
using System.Security.Authentication;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Webhooks;

using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.WebhookIngressProbeTests;

public sealed class VerifyAsync
{
    private const string PublicBaseUrl = "https://hooks.example.com";

    private static (WebhookIngressProbe Probe, StubHandler Handler) BuildSut(
        Func<HttpRequestMessage, WebhookIngressProbe, Task<HttpResponseMessage>> respond)
    {
        StubHandler handler = new();
        WebhookIngressProbe probe = new(new StubHttpClientFactory(handler), Options.Create(new WebhookIngressOptions()));
        handler.Respond = request => respond(request, probe);
        return (probe, handler);
    }

    private static string CodeOf(HttpRequestMessage request) => request.RequestUri!.Segments[^1];

    private static WebhookProbeFailure FailureOf(WebhookProbeResult result) =>
        result.ShouldBeOfType<WebhookProbeResult.Unreachable>().Failure;

    [Fact]
    public async Task WhenCodeArrivesAndResponseIs2xx_Reaches()
    {
        // Arrange
        (WebhookIngressProbe sut, StubHandler handler) = BuildSut((request, probe) =>
        {
            probe.TryRecordArrival(CodeOf(request)).ShouldBeTrue();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        // Act
        WebhookProbeResult result = await sut.VerifyAsync(PublicBaseUrl, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<WebhookProbeResult.Reached>();
        handler.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Get);
    }

    [Fact]
    public async Task WhenSent_RequestsProbePathWithA256BitHexCode()
    {
        // Arrange
        (WebhookIngressProbe sut, StubHandler handler) = BuildSut((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        // Act
        await sut.VerifyAsync(PublicBaseUrl + "/", CancellationToken.None);

        // Assert
        Uri uri = handler.Requests.ShouldHaveSingleItem().RequestUri!;
        uri.GetLeftPart(UriPartial.Authority).ShouldBe(PublicBaseUrl);
        uri.AbsolutePath.ShouldStartWith("/webhooks/probe/");
        uri.Segments[^1].ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task WhenResponseIs2xxButCodeNeverArrived_FailsAsOtherInstance()
    {
        // Arrange
        (WebhookIngressProbe sut, _) = BuildSut((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

        // Act
        WebhookProbeResult result = await sut.VerifyAsync(PublicBaseUrl, CancellationToken.None);

        // Assert
        FailureOf(result).ShouldBeOfType<WebhookProbeFailure.OtherInstance>().Cause.ShouldBe("other-instance");
    }

    [Fact]
    public async Task WhenResponseIs404_FailsAsOtherInstance()
    {
        // Arrange
        (WebhookIngressProbe sut, _) = BuildSut((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        // Act
        WebhookProbeResult result = await sut.VerifyAsync(PublicBaseUrl, CancellationToken.None);

        // Assert
        FailureOf(result).ShouldBeOfType<WebhookProbeFailure.OtherInstance>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task WhenResponseIsAnotherStatus_FailsAsHttpStatusWithTheCode(HttpStatusCode status)
    {
        // Arrange
        (WebhookIngressProbe sut, _) = BuildSut((request, probe) =>
        {
            probe.TryRecordArrival(CodeOf(request));
            return Task.FromResult(new HttpResponseMessage(status));
        });

        // Act
        WebhookProbeResult result = await sut.VerifyAsync(PublicBaseUrl, CancellationToken.None);

        // Assert
        WebhookProbeFailure.HttpStatus failure = FailureOf(result).ShouldBeOfType<WebhookProbeFailure.HttpStatus>();
        failure.StatusCode.ShouldBe((int)status);
        failure.Cause.ShouldBe("http-status");
    }

    [Fact]
    public async Task WhenHostDoesNotResolve_FailsAsDns()
    {
        // Arrange
        (WebhookIngressProbe sut, _) = BuildSut((_, _) =>
            throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known."));

        // Act
        WebhookProbeResult result = await sut.VerifyAsync(PublicBaseUrl, CancellationToken.None);

        // Assert
        FailureOf(result).ShouldBeOfType<WebhookProbeFailure.Dns>().Cause.ShouldBe("dns");
    }

    [Fact]
    public async Task WhenTlsHandshakeFails_FailsAsTls()
    {
        // Arrange
        (WebhookIngressProbe sut, _) = BuildSut((_, _) =>
            throw new HttpRequestException(
                HttpRequestError.SecureConnectionError,
                "The SSL connection could not be established.",
                new AuthenticationException("The remote certificate is invalid.")));

        // Act
        WebhookProbeResult result = await sut.VerifyAsync(PublicBaseUrl, CancellationToken.None);

        // Assert
        FailureOf(result).ShouldBeOfType<WebhookProbeFailure.Tls>().Cause.ShouldBe("tls");
    }

    [Fact]
    public async Task WhenNoResponseInTime_FailsAsTimeout()
    {
        // Arrange
        (WebhookIngressProbe sut, _) = BuildSut((_, _) => throw new TaskCanceledException("timed out", new TimeoutException()));

        // Act
        WebhookProbeResult result = await sut.VerifyAsync(PublicBaseUrl, CancellationToken.None);

        // Assert
        FailureOf(result).ShouldBeOfType<WebhookProbeFailure.Timeout>().Cause.ShouldBe("timeout");
    }

    [Fact]
    public async Task WhenConnectionIsRefused_FailsAsTimeout()
    {
        // Arrange
        (WebhookIngressProbe sut, _) = BuildSut((_, _) =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused"));

        // Act
        WebhookProbeResult result = await sut.VerifyAsync(PublicBaseUrl, CancellationToken.None);

        // Assert
        FailureOf(result).ShouldBeOfType<WebhookProbeFailure.Timeout>();
    }

    [Fact]
    public async Task WhenCallerCancels_Throws()
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        (WebhookIngressProbe sut, _) = BuildSut(async (_, _) =>
        {
            await cancellation.CancelAsync();
            throw new TaskCanceledException();
        });

        // Act / Assert
        await Should.ThrowAsync<OperationCanceledException>(() => sut.VerifyAsync(PublicBaseUrl, cancellation.Token));
    }

    [Fact]
    public async Task WhenProbeCompletes_ItsCodeIsNoLongerAccepted()
    {
        // Arrange
        string? code = null;
        (WebhookIngressProbe sut, _) = BuildSut((request, probe) =>
        {
            code = CodeOf(request);
            probe.TryRecordArrival(code);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        // Act
        await sut.VerifyAsync(PublicBaseUrl, CancellationToken.None);

        // Assert
        sut.TryRecordArrival(code!).ShouldBeFalse();
    }

    [Fact]
    public void WhenCodeIsNotPending_IsRefused()
    {
        // Arrange
        (WebhookIngressProbe sut, _) = BuildSut((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

        // Act
        bool recorded = sut.TryRecordArrival(new string('a', 64));

        // Assert
        recorded.ShouldBeFalse();
    }

    [Fact]
    public void Port_IsTheBoundWebhookPort()
    {
        // Arrange
        WebhookIngressProbe sut = new(new StubHttpClientFactory(new StubHandler()), Options.Create(new WebhookIngressOptions { Port = 6001 }));

        // Act / Assert
        sut.Port.ShouldBe(6001);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } =
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Respond(request);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
