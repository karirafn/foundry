using System.Net;

using Docker.DotNet;

using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Testing;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.FoundryImageSweepTests;

public sealed class SweepAsync
{
    private readonly CapturingLogger<FoundryImageSweep> _logger = new();

    private FoundryImageSweep CreateSweep(FakeImageHost host) => new(host, _logger);

    [Fact]
    public async Task RemovesALabelledDanglingImage()
    {
        FakeImageHost host = new FakeImageHost().Add("sha256:old");

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        host.RemainingIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task KeepsADanglingImageWithoutTheLabel()
    {
        FakeImageHost host = new FakeImageHost().Add("sha256:other-project", label: null);

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        host.RemainingIds.ShouldBe(["sha256:other-project"]);
    }

    [Fact]
    public async Task KeepsATaggedLabelledImage()
    {
        FakeImageHost host = new FakeImageHost().Add("sha256:current", tag: "foundry-worker:local");

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        host.RemainingIds.ShouldBe(["sha256:current"]);
    }

    [Fact]
    public async Task ListsByTheFoundryLabelAndDanglingState()
    {
        FakeImageHost host = new();

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        IDictionary<string, IDictionary<string, bool>> filters = host.ListCalls[0].Filters;
        filters["label"].Keys.ShouldBe(["dev.foundry.image"]);
        filters["dangling"].Keys.ShouldBe(["true"]);
    }

    [Fact]
    public async Task NeverForcesARemoval()
    {
        FakeImageHost host = new FakeImageHost().Add("sha256:old");

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        host.DeleteCalls.ShouldHaveSingleItem().Parameters.Force.ShouldNotBe(true);
    }

    [Fact]
    public async Task RemovesTheParentBuildStepsEachRemovalExposes()
    {
        FakeImageHost host = new FakeImageHost()
            .Add("sha256:step-1")
            .Add("sha256:step-2", parent: "sha256:step-1")
            .Add("sha256:top", parent: "sha256:step-2");

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        host.RemainingIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task StopsAfterAPassThatRemovesNothing()
    {
        FakeImageHost host = new FakeImageHost()
            .Add("sha256:step-1")
            .Add("sha256:top", parent: "sha256:step-1");

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        host.ListCalls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task SkipsAnImageAContainerUsesAndRemovesTheRest()
    {
        FakeImageHost host = new FakeImageHost()
            .Add("sha256:in-use", inUse: true)
            .Add("sha256:old");

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        host.RemainingIds.ShouldBe(["sha256:in-use"]);
    }

    [Fact]
    public async Task LogsAnImageAContainerUsesAtDebugLevel()
    {
        FakeImageHost host = new FakeImageHost().Add("sha256:in-use", inUse: true);

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Debug && e.Message.Contains("sha256:in-use"));
        _logger.Entries.ShouldNotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task EndsWhenEveryRemainingImageIsInUse()
    {
        FakeImageHost host = new FakeImageHost()
            .Add("sha256:in-use-1", inUse: true)
            .Add("sha256:in-use-2", inUse: true);

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        host.ListCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SkipsAnImageAlreadyGone()
    {
        FakeImageHost host = new FakeImageHost().Add("sha256:old");
        host.DeleteFailure = new DockerImageNotFoundException(HttpStatusCode.NotFound, "No such image: sha256:old");

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        _logger.Entries.ShouldNotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task LogsAWarningWhenDockerIsUnreachable()
    {
        FakeImageHost host = new() { ListFailure = new HttpRequestException("Connection refused") };

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task LogsAWarningOnAnUnexpectedRemovalError()
    {
        FakeImageHost host = new FakeImageHost().Add("sha256:old");
        host.DeleteFailure = new DockerApiException(HttpStatusCode.InternalServerError, "{\"message\":\"driver failed\"}");

        await CreateSweep(host).SweepAsync(TestContext.Current.CancellationToken);

        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        FakeImageHost host = new FakeImageHost().Add("sha256:old");
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();
        host.ListFailure = new OperationCanceledException(cts.Token);

        await Should.ThrowAsync<OperationCanceledException>(() => CreateSweep(host).SweepAsync(cts.Token));
    }
}
