using System.Net;
using System.Text;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubJobLogClientTests;

public sealed class GetJobLogAsync
{
    private const string Token = "ghp_secret_token";
    private static readonly Uri ApiBaseUrl = new("https://api.github.com");
    private static readonly Uri DownloadUrl = new("https://results-receiver.actions.githubusercontent.com/logs/7?sig=abc");

    private static RepositorySlug Slug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static async Task<Result<string>> ActAsync(RedirectingHandler handler) =>
        await new GitHubJobLogClient(new HttpClient(handler)).GetJobLogAsync(
            ApiBaseUrl, Slug, 7, Token, TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenTheApiRedirects_ReturnsTheLogFromTheDownloadUrl()
    {
        // Arrange
        RedirectingHandler handler = new(HttpStatusCode.Found, DownloadUrl, HttpStatusCode.OK, "step 3 failed");

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Success>().Value.ShouldBe("step 3 failed");
    }

    [Fact]
    public async Task WhenTheApiRedirects_RequestsTheJobLogsEndpointWithTheToken()
    {
        // Arrange
        RedirectingHandler handler = new(HttpStatusCode.Found, DownloadUrl, HttpStatusCode.OK, "log");

        // Act
        await ActAsync(handler);

        // Assert
        HttpRequestMessage apiRequest = handler.Requests[0];
        apiRequest.ShouldSatisfyAllConditions(
            () => apiRequest.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/actions/jobs/7/logs"),
            () => apiRequest.Headers.Authorization.ShouldNotBeNull().Parameter.ShouldBe(Token));
    }

    [Fact]
    public async Task WhenTheRedirectIsFollowed_TheDownloadRequestCarriesNoAuthorizationHeader()
    {
        // Arrange
        RedirectingHandler handler = new(HttpStatusCode.Found, DownloadUrl, HttpStatusCode.OK, "log");

        // Act
        await ActAsync(handler);

        // Assert
        handler.Requests.Count.ShouldBe(2);
        HttpRequestMessage downloadRequest = handler.Requests[1];
        downloadRequest.ShouldSatisfyAllConditions(
            () => downloadRequest.RequestUri.ShouldBe(DownloadUrl),
            () => downloadRequest.Headers.Authorization.ShouldBeNull(),
            () => downloadRequest.Headers.Contains("Authorization").ShouldBeFalse(),
            () => downloadRequest.Headers.ToString().ShouldNotContain(Token));
    }

    [Fact]
    public async Task WhenTheRedirectTargetIsNotHttps_FailsWithoutFollowingIt()
    {
        // Arrange
        RedirectingHandler handler = new(
            HttpStatusCode.Found, new Uri("http://storage.example/logs/7"), HttpStatusCode.OK, "log");

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>().Error.Code.ShouldBe(JobLogErrors.UnavailableCode);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTheDownloadUrlHasExpired_Fails()
    {
        // Arrange
        RedirectingHandler handler = new(HttpStatusCode.Found, DownloadUrl, HttpStatusCode.Forbidden, "expired");

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>().Error.Code.ShouldBe(JobLogErrors.UnavailableCode);
    }

    [Fact]
    public async Task WhenTheJobHasNoLog_Fails()
    {
        // Arrange
        RedirectingHandler handler = new(HttpStatusCode.NotFound, null, HttpStatusCode.OK, string.Empty);

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>().Error.Code.ShouldBe(JobLogErrors.UnavailableCode);
    }

    // Answers the first request with the API's status (and Location), every later request with the download's.
    private sealed class RedirectingHandler(
        HttpStatusCode apiStatus,
        Uri? location,
        HttpStatusCode downloadStatus,
        string downloadBody) : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests = [];

        public IReadOnlyList<HttpRequestMessage> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);

            if (_requests.Count == 1)
            {
                HttpResponseMessage apiResponse = new(apiStatus);
                apiResponse.Headers.Location = location;
                return Task.FromResult(apiResponse);
            }

            return Task.FromResult(new HttpResponseMessage(downloadStatus)
            {
                Content = new StringContent(downloadBody, Encoding.UTF8, "text/plain"),
            });
        }
    }
}
