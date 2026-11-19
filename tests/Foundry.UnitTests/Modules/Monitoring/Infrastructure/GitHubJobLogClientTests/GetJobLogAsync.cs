using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Foundry.Modules.Monitoring;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubJobLogClientTests;

public sealed class GetJobLogAsync
{
    private const int Tail = JobLogTailReader.JobLogTailBytes;
    private const string Token = "ghp_secret_token";
    private const long OverBufferCap = MonitoringModule.MaxResponseContentBufferSize + 1024;
    private static readonly Uri ApiBaseUrl = new("https://api.github.com");
    private static readonly Uri DownloadUrl = new("https://results-receiver.actions.githubusercontent.com/logs/7?sig=abc");

    private static RepositorySlug Slug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    // The client carries the module's buffer cap, so a body read whole rather than streamed fails as it does live.
    private static async Task<Result<string>> ActAsync(StorageHandler handler) =>
        await new GitHubJobLogClient(
                new HttpClient(handler) { MaxResponseContentBufferSize = MonitoringModule.MaxResponseContentBufferSize })
            .GetJobLogAsync(ApiBaseUrl, Slug, 7, Token, TestContext.Current.CancellationToken);

    private static StorageHandler Redirecting(StorageMode mode, LogSource log) =>
        new(HttpStatusCode.Found, DownloadUrl, mode, log);

    [Fact]
    public async Task WhenTheApiRedirects_ReturnsTheLogFromTheDownloadUrl()
    {
        // Arrange
        StorageHandler handler = Redirecting(StorageMode.HonoursRange, LogSource.Text("step 3 failed"));

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Success>().Value.ShouldBe("step 3 failed");
    }

    [Fact]
    public async Task WhenTheApiRedirects_RequestsTheJobLogsEndpointWithTheToken()
    {
        // Arrange
        StorageHandler handler = Redirecting(StorageMode.HonoursRange, LogSource.Text("log"));

        // Act
        await ActAsync(handler);

        // Assert
        HttpRequestMessage apiRequest = handler.Requests[0];
        apiRequest.ShouldSatisfyAllConditions(
            () => apiRequest.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/actions/jobs/7/logs"),
            () => apiRequest.Headers.Authorization.ShouldNotBeNull().Parameter.ShouldBe(Token));
    }

    [Theory]
    [InlineData(StorageMode.HonoursRange, 3)]
    [InlineData(StorageMode.HonoursRange, OverBufferCap)]
    [InlineData(StorageMode.IgnoresRange, OverBufferCap)]
    [InlineData(StorageMode.ProbeWithoutContentRange, OverBufferCap)]
    [InlineData(StorageMode.RangeNotSatisfiable, OverBufferCap)]
    public async Task WhenTheRedirectIsFollowed_NoStorageRequestCarriesTheToken(StorageMode mode, long logLength)
    {
        // Arrange
        StorageHandler handler = Redirecting(mode, LogSource.Generated(logLength));

        // Act
        await ActAsync(handler);

        // Assert
        handler.Requests.Count.ShouldBeGreaterThan(1);
        handler.Requests.Skip(1).ShouldAllBe(request =>
            request.RequestUri == DownloadUrl
            && request.Headers.Authorization == null
            && !request.Headers.Contains("Authorization")
            && !request.Headers.ToString().Contains(Token));
    }

    [Fact]
    public async Task WhenTheLogIsLargerThanTheBufferCap_ReturnsItsTailWithoutDownloadingTheRest()
    {
        // Arrange
        StorageHandler handler = Redirecting(StorageMode.HonoursRange, LogSource.Generated(OverBufferCap));

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Success>().Value
            .ShouldBe(GeneratedLogStream.TextOf(OverBufferCap - Tail, OverBufferCap));
        handler.StorageBytesServed.ShouldBeLessThanOrEqualTo(Tail + 1);
    }

    [Fact]
    public async Task WhenTheLogIsLargerThanTheTail_ProbesItsLengthThenRequestsTheTailFromItsStartOffset()
    {
        // Arrange
        long length = (2L * Tail) + 99;
        StorageHandler handler = Redirecting(StorageMode.HonoursRange, LogSource.Generated(length));

        // Act
        await ActAsync(handler);

        // Assert
        handler.Requests.Count.ShouldBe(3);
        string probeRange = handler.Requests[1].Headers.Range.ShouldNotBeNull().ToString();
        string tailRange = handler.Requests[2].Headers.Range.ShouldNotBeNull().ToString();
        probeRange.ShouldBe("bytes=0-0");
        tailRange.ShouldBe($"bytes={length - Tail}-");
    }

    [Fact]
    public async Task WhenTheLogIsShorterThanTheTail_ReadsItWhole()
    {
        // Arrange
        StorageHandler handler = Redirecting(StorageMode.HonoursRange, LogSource.Generated(Tail - 1));

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Success>().Value.ShouldBe(GeneratedLogStream.TextOf(0, Tail - 1));
    }

    [Fact]
    public async Task WhenTheLogIsEmpty_ReturnsAnEmptyString()
    {
        // Arrange
        StorageHandler handler = Redirecting(StorageMode.HonoursRange, LogSource.Generated(0));

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Success>().Value.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(StorageMode.IgnoresRange)]
    [InlineData(StorageMode.ProbeWithoutContentRange)]
    [InlineData(StorageMode.RangeNotSatisfiable)]
    public async Task WhenTheStorageHostCannotServeTheTailRange_StreamsTheBodyAndKeepsItsTail(StorageMode mode)
    {
        // Arrange
        StorageHandler handler = Redirecting(mode, LogSource.Generated(OverBufferCap));

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Success>().Value
            .ShouldBe(GeneratedLogStream.TextOf(OverBufferCap - Tail, OverBufferCap));
    }

    [Fact]
    public async Task WhenTheTailRangeStartsInsideAMultiByteCharacter_DropsThePartialCharacter()
    {
        // Arrange: '€' is three bytes, and the tail size is not a multiple of three.
        StorageHandler handler = Redirecting(StorageMode.HonoursRange, LogSource.Text(new string('€', Tail)));

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Success>().Value.ShouldBe(new string('€', Tail / 3));
    }

    [Fact]
    public async Task WhenTheApiAnswersWithTheLogDirectly_KeepsItsTail()
    {
        // Arrange
        StorageHandler handler = new(HttpStatusCode.OK, null, StorageMode.HonoursRange, LogSource.Generated(OverBufferCap));

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Success>().Value
            .ShouldBe(GeneratedLogStream.TextOf(OverBufferCap - Tail, OverBufferCap));
    }

    [Fact]
    public async Task WhenTheRedirectTargetIsNotHttps_FailsWithoutFollowingIt()
    {
        // Arrange
        StorageHandler handler = new(
            HttpStatusCode.Found, new Uri("http://storage.example/logs/7"), StorageMode.HonoursRange, LogSource.Text("log"));

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>().Error.Code.ShouldBe(JobLogErrors.UnavailableCode);
        handler.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(StorageMode.Expired)]
    [InlineData(StorageMode.ExpiresAfterProbe)]
    public async Task WhenTheDownloadUrlHasExpired_Fails(StorageMode mode)
    {
        // Arrange
        StorageHandler handler = Redirecting(mode, LogSource.Generated(OverBufferCap));

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>().Error.Code.ShouldBe(JobLogErrors.UnavailableCode);
    }

    [Fact]
    public async Task WhenTheJobHasNoLog_Fails()
    {
        // Arrange
        StorageHandler handler = new(HttpStatusCode.NotFound, null, StorageMode.HonoursRange, LogSource.Text(string.Empty));

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>().Error.Code.ShouldBe(JobLogErrors.UnavailableCode);
    }

    public enum StorageMode
    {
        HonoursRange,
        IgnoresRange,
        ProbeWithoutContentRange,
        RangeNotSatisfiable,
        Expired,
        ExpiresAfterProbe,
    }

    private sealed class LogSource(long length, byte[]? text)
    {
        public long Length => length;

        public static LogSource Text(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return new LogSource(bytes.Length, bytes);
        }

        public static LogSource Generated(long length) => new(length, null);

        public Stream Open(long start, long end) =>
            text is null ? new GeneratedLogStream(start, end) : new MemoryStream(text, (int)start, (int)(end - start));
    }

    // Answers the first request as GitHub's API does, and every later one as the storage host in the given mode.
    private sealed class StorageHandler(
        HttpStatusCode apiStatus,
        Uri? location,
        StorageMode mode,
        LogSource log) : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests = [];
        private readonly List<GeneratedLogStream> _storageStreams = [];

        public IReadOnlyList<HttpRequestMessage> Requests => _requests;

        public long StorageBytesServed => _storageStreams.Sum(stream => stream.BytesRead);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);

            if (_requests.Count == 1)
            {
                HttpResponseMessage apiResponse = apiStatus is HttpStatusCode.OK
                    ? Body(HttpStatusCode.OK, 0, log.Length, trackServed: false)
                    : new HttpResponseMessage(apiStatus);
                apiResponse.Headers.Location = location;
                return Task.FromResult(apiResponse);
            }

            return Task.FromResult(Storage(request));
        }

        private HttpResponseMessage Storage(HttpRequestMessage request)
        {
            bool isProbe = _requests.Count == 2;
            RangeItemHeaderValue? range = request.Headers.Range?.Ranges.Single();

            if (mode is StorageMode.Expired || (mode is StorageMode.ExpiresAfterProbe && !isProbe))
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("expired") };
            }

            if (range is null || mode is StorageMode.IgnoresRange)
            {
                return Body(HttpStatusCode.OK, 0, log.Length);
            }

            long from = range.From ?? 0;
            if (from >= log.Length || (mode is StorageMode.RangeNotSatisfiable && !isProbe))
            {
                HttpResponseMessage notSatisfiable = new(HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    Content = new ByteArrayContent([]),
                };
                notSatisfiable.Content.Headers.ContentRange = new ContentRangeHeaderValue(log.Length);
                return notSatisfiable;
            }

            long end = Math.Min(log.Length, (range.To ?? long.MaxValue - 1) + 1);
            HttpResponseMessage partial = Body(HttpStatusCode.PartialContent, from, end);
            if (!(mode is StorageMode.ProbeWithoutContentRange && isProbe))
            {
                partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, end - 1, log.Length);
            }

            return partial;
        }

        private HttpResponseMessage Body(HttpStatusCode status, long start, long end, bool trackServed = true)
        {
            Stream stream = log.Open(start, end);
            if (trackServed && stream is GeneratedLogStream generated)
            {
                _storageStreams.Add(generated);
            }

            return new HttpResponseMessage(status) { Content = new StreamContent(stream) };
        }
    }
}
