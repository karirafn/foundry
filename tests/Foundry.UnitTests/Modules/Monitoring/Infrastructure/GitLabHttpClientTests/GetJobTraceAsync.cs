using System.Net;

using Foundry.Modules.Monitoring;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetJobTraceAsync
{
    private static readonly Uri ApiBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug Slug => RepositorySlug.Create("group/project").ValueOrThrow();

    private static async Task<Result<string>> ActAsync(FakeHandler handler) =>
        await new GitLabHttpClient(
                new HttpClient(handler),
                NullLogger<GitLabHttpClient>.Instance,
                new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
                new InMemoryProviderRateBudget(),
                TimeProvider.System)
            .GetJobTraceAsync(ApiBaseUrl, Slug, 7, "glpat-token", TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenTheTraceExists_ReturnsItsText()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "$ dotnet test\nFailed!");

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Success>().Value.ShouldBe("$ dotnet test\nFailed!");
    }

    [Fact]
    public async Task WhenCalled_RequestsTheJobTraceOfTheEncodedProjectWithTheToken()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "log");

        // Act
        await ActAsync(handler);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.ShouldSatisfyAllConditions(
            () => request.RequestUri!.AbsoluteUri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/jobs/7/trace"),
            () => request.Headers.GetValues("PRIVATE-TOKEN").ShouldBe(["glpat-token"]));
    }

    [Fact]
    public async Task WhenTheJobIsNotFound_Fails()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, "{}");

        // Act
        Result<string> result = await ActAsync(handler);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>().Error.Code.ShouldBe(JobLogErrors.UnavailableCode);
    }

    [Fact]
    public async Task WhenTheTraceIsLargerThanTheBufferCap_ReturnsItsLastTailBytes()
    {
        // Arrange
        long length = MonitoringModule.MaxResponseContentBufferSize + 1024;
        GeneratedTraceHandler handler = new(length);
        GitLabHttpClient client = new(
            new HttpClient(handler) { MaxResponseContentBufferSize = MonitoringModule.MaxResponseContentBufferSize },
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

        // Act
        Result<string> result = await client.GetJobTraceAsync(
            ApiBaseUrl, Slug, 7, "glpat-token", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<string>.Success>().Value
            .ShouldBe(GeneratedLogStream.TextOf(length - JobLogTailReader.JobLogTailBytes, length));
    }

    private sealed class GeneratedTraceHandler(long length) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new GeneratedLogStream(0, length)),
            });
    }
}
