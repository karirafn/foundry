using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class ProbeWriteAccessAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static readonly (HttpStatusCode, string) Granted = (HttpStatusCode.UnprocessableEntity, string.Empty);
    private static readonly (HttpStatusCode, string) Refused = (HttpStatusCode.Forbidden, string.Empty);
    private static readonly (HttpStatusCode, string) DefaultBranch = (HttpStatusCode.OK, """{"default_branch":"main"}""");
    private static readonly (HttpStatusCode, string) ReadGranted = (HttpStatusCode.OK, "[]");
    private static readonly (HttpStatusCode, string) ActionsGranted = (HttpStatusCode.NotFound, string.Empty);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient CreateSut(SequentialFakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static Task<Result<WritePermissionProbeResult>> ProbeAsync(GitHubHttpClient sut) =>
        sut.ProbeWriteAccessAsync(ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

    [Fact]
    public async Task WhenAllProbesGranted_ReturnsGranted()
    {
        // Arrange
        SequentialFakeHandler handler = new(
            [Granted, Granted, Granted, Granted, Granted, DefaultBranch, ReadGranted, ActionsGranted]);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<WritePermissionProbeResult> result = await ProbeAsync(sut);

        // Assert
        result.ShouldBeOfType<Result<WritePermissionProbeResult>.Success>()
            .Value.ShouldBeOfType<WritePermissionProbeResult.Granted>();
    }

    [Fact]
    public async Task WhenAllProbesGranted_RunsProbesInPermissionListOrder()
    {
        // Arrange
        SequentialFakeHandler handler = new(
            [Granted, Granted, Granted, Granted, Granted, DefaultBranch, ReadGranted, ActionsGranted]);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        await ProbeAsync(sut);

        // Assert
        handler.Requests.Select(request => $"{request.Method} {request.RequestUri?.PathAndQuery}").ShouldBe(
        [
            "POST /repos/owner/repo/git/refs",
            "POST /repos/owner/repo/issues",
            "POST /repos/owner/repo/pulls",
            "POST /repos/owner/repo/rulesets",
            "POST /repos/owner/repo/hooks",
            "GET /repos/owner/repo",
            "GET /repos/owner/repo/commits/main/statuses?per_page=1",
            "POST /repos/owner/repo/actions/jobs/1/rerun",
        ]);
    }

    [Fact]
    public async Task WhenAdministrationAndWebhooksAreRefused_ReturnsBothAsMissing()
    {
        // Arrange
        SequentialFakeHandler handler = new(
            [Granted, Granted, Granted, Refused, Refused, DefaultBranch, ReadGranted, ActionsGranted]);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<WritePermissionProbeResult> result = await ProbeAsync(sut);

        // Assert
        result.ShouldBeOfType<Result<WritePermissionProbeResult>.Success>()
            .Value.ShouldBeOfType<WritePermissionProbeResult.Missing>()
            .Permissions.ShouldBe([WritePermission.Administration, WritePermission.Webhooks]);
    }

    [Fact]
    public async Task WhenContentsIsRefused_RunsEveryRemainingProbe()
    {
        // Arrange
        SequentialFakeHandler handler = new(
            [Refused, Granted, Granted, Granted, Granted, DefaultBranch, ReadGranted, ActionsGranted]);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        await ProbeAsync(sut);

        // Assert
        handler.Requests.Count.ShouldBe(8);
    }

    [Fact]
    public async Task WhenEveryProbeIsRefused_ReturnsEveryProbedPermissionAsMissing()
    {
        // Arrange
        SequentialFakeHandler handler = new(
            [Refused, Refused, Refused, Refused, Refused, DefaultBranch, Refused, Refused]);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<WritePermissionProbeResult> result = await ProbeAsync(sut);

        // Assert
        result.ShouldBeOfType<Result<WritePermissionProbeResult>.Success>()
            .Value.ShouldBeOfType<WritePermissionProbeResult.Missing>()
            .Permissions.ShouldBe(
            [
                WritePermission.Contents,
                WritePermission.Issues,
                WritePermission.PullRequests,
                WritePermission.Administration,
                WritePermission.Webhooks,
                WritePermission.CommitStatuses,
                WritePermission.Actions,
            ]);
    }

    [Fact]
    public async Task WhenAProbeFailsAfterARefusal_ReturnsTheFailureWithoutCallingFurtherProbes()
    {
        // Arrange
        SequentialFakeHandler handler = new(
            [Refused, Granted, Granted, (HttpStatusCode.NotFound, string.Empty)]);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<WritePermissionProbeResult> result = await ProbeAsync(sut);

        // Assert
        result.ShouldBeOfType<Result<WritePermissionProbeResult>.Failure>();
        handler.Requests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task WhenContentsProbeFailsWithTransportError_ReturnsFailureWithoutCallingFurtherProbes()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.InternalServerError, string.Empty)]);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<WritePermissionProbeResult> result = await ProbeAsync(sut);

        // Assert
        result.ShouldBeOfType<Result<WritePermissionProbeResult>.Failure>();
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenAProbeIsRateLimited_ReturnsRateLimitExhaustedFailure()
    {
        // Arrange
        Dictionary<string, string> noHeaders = [];
        Dictionary<string, string> rateLimited = new() { ["X-RateLimit-Remaining"] = "0" };
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.UnprocessableEntity, string.Empty, noHeaders),
            (HttpStatusCode.UnprocessableEntity, string.Empty, noHeaders),
            (HttpStatusCode.UnprocessableEntity, string.Empty, noHeaders),
            (HttpStatusCode.Forbidden, string.Empty, rateLimited),
        ]);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<WritePermissionProbeResult> result = await ProbeAsync(sut);

        // Assert
        result.ShouldBeOfType<Result<WritePermissionProbeResult>.Failure>()
            .Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }
}
