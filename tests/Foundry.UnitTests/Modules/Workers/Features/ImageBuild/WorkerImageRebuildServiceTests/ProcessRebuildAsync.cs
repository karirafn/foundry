using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildServiceTests;

public sealed class ProcessRebuildAsync
{
    private static WorkerImageRebuildService BuildService(
        IImageOperations imageOperations,
        IGlobalSettingsQueries? settingsQueries = null,
        CapturingIntegrationEventDispatcher? dispatcher = null,
        string contentRootPath = "",
        bool imageBuildEnabled = true,
        string imageTag = "foundry-worker:local",
        IWorkerImageRebuildQueue? queue = null)
    {
        ServiceCollection services = new();
        services.AddScoped<IGlobalSettingsQueries>(_ =>
            settingsQueries ?? new StubGlobalSettingsQueries(
                settingsExists: true,
                imageTag: imageTag,
                buildEnabled: imageBuildEnabled));
        services.AddScoped<IIntegrationEventDispatcher>(_ =>
            dispatcher ?? new CapturingIntegrationEventDispatcher());

        ServiceProvider sp = services.BuildServiceProvider();

        StubHostEnvironment hostEnv = new(contentRootPath);

        return new WorkerImageRebuildService(
            queue ?? new NullWorkerImageRebuildQueue(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            imageOperations,
            hostEnv,
            NullLogger<WorkerImageRebuildService>.Instance);
    }

    [Fact]
    public async Task WhenGlobalSettingsMissing_DoesNotBuildImage()
    {
        // Arrange — settings query returns null (no row)
        SpyImageOperations spyImages = new();
        WorkerImageRebuildService sut = BuildService(
            spyImages,
            settingsQueries: new StubGlobalSettingsQueries(settingsExists: false));

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert
        spyImages.BuildCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenGlobalSettingsMissing_PublishesNoEvents()
    {
        // Arrange — settings query returns null; no event should be published since
        // the early return fires before any dispatch call.
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerImageRebuildService sut = BuildService(
            new SpyImageOperations(),
            settingsQueries: new StubGlobalSettingsQueries(settingsExists: false),
            dispatcher: dispatcher);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert — no ImageBuildRequested (or any other report event) must be published
        dispatcher.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenBuildSucceeds_PublishesImageBuildRequestedFirst()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            WorkerImageRebuildService sut = BuildService(
                new SpyImageOperations(),
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — ImageBuildRequested must be published before ImageBuildSucceeded
            dispatcher.Published.Count.ShouldBeGreaterThanOrEqualTo(1);
            dispatcher.Published[0].ShouldBeOfType<ImageBuildRequested>();
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_PublishesImageBuildSucceeded()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            WorkerImageRebuildService sut = BuildService(
                new SpyImageOperations(),
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — ImageBuildSucceeded published after ImageBuildRequested
            dispatcher.Published.Count.ShouldBe(2);
            dispatcher.Published[0].ShouldBeOfType<ImageBuildRequested>();
            dispatcher.Published[1].ShouldBeOfType<ImageBuildSucceeded>();
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsError_PublishesImageBuildOutcomeFailed()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            const string dockerError = "Build failed: layer error";
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new(dockerError);
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — ImageBuildOutcomeFailed published after ImageBuildRequested
            dispatcher.Published.Count.ShouldBe(2);
            dispatcher.Published[0].ShouldBeOfType<ImageBuildRequested>();
            ImageBuildOutcomeFailed failed = dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
            failed.ErrorTail.ShouldBe(dockerError);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsError_PublishedFailedEventHasAttemptOne()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new("build error");
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            ImageBuildOutcomeFailed failed = dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
            failed.Attempt.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsError_PublishedFailedEventHasNextRetryAtInFuture()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            DateTimeOffset before = DateTimeOffset.UtcNow;
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new("build error");
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            ImageBuildOutcomeFailed failed = dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
            failed.NextRetryAt.ShouldNotBeNull();
            failed.NextRetryAt.Value.ShouldBeGreaterThan(before);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenPersistedAttemptIsOne_FailureReportsAttemptTwo()
    {
        // Arrange — settings returns Attempt=1 (persisted from a prior failed build),
        // so the next failure must report Attempt=2 (derived from persisted state, not in-memory counter).
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new("build error");
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                settingsQueries: new StubGlobalSettingsQueries(attempt: 1),
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — OutcomeFailed must have attempt=2 (persisted 1 + 1)
            IIntegrationEvent last = dispatcher.Published[^1];
            ImageBuildOutcomeFailed failed = last.ShouldBeOfType<ImageBuildOutcomeFailed>();
            failed.Attempt.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceedsAfterFailure_PublishesSucceeded()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            CapturingIntegrationEventDispatcher failDispatcher = new();
            ErrorReportingImageOperations errorImages = new("build error");
            WorkerImageRebuildService firstSut = BuildService(
                errorImages,
                dispatcher: failDispatcher,
                contentRootPath: contentRoot);

            // Fail once
            await firstSut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Now succeed — new service instance, new dispatcher
            CapturingIntegrationEventDispatcher successDispatcher = new();
            WorkerImageRebuildService sut = BuildService(
                new SpyImageOperations(),
                dispatcher: successDispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — success path publishes Requested then Succeeded
            successDispatcher.Published.Count.ShouldBe(2);
            successDispatcher.Published[0].ShouldBeOfType<ImageBuildRequested>();
            successDispatcher.Published[1].ShouldBeOfType<ImageBuildSucceeded>();
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenDockerThrowsException_PublishesOutcomeFailed()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            ThrowingImageOperations throwingImages = new("connection refused");
            WorkerImageRebuildService sut = BuildService(
                throwingImages,
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            dispatcher.Published.Count.ShouldBe(2);
            dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenDockerThrowsExceptionWithSecret_RedactsSecretFromPublishedErrorTail()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            const string secretMessage = "pull access denied: https://user:sk-ant-api03-secret@registry.example.com/image";
            CapturingIntegrationEventDispatcher dispatcher = new();
            ThrowingImageOperations throwingImages = new(secretMessage);
            WorkerImageRebuildService sut = BuildService(
                throwingImages,
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — the published error tail must not contain the raw secret token
            ImageBuildOutcomeFailed failed = dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
            string errorTail = failed.ErrorTail.ShouldNotBeNull();
            errorTail.ShouldNotContain("sk-ant-api03-secret");
            errorTail.ShouldContain("***");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsErrorWithSecret_RedactsSecretFromPublishedErrorTail()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            const string secretError = "https://glpat-abc123secret@gitlab.example.com/image build failed";
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new(secretError);
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — the published error tail must not contain the raw token
            ImageBuildOutcomeFailed failed = dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
            string errorTail = failed.ErrorTail.ShouldNotBeNull();
            errorTail.ShouldNotContain("glpat-abc123secret");
            errorTail.ShouldContain("***");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsErrorWithBuildArgSecret_RedactsSecretFromPublishedErrorTail()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            const string secretError = "ANTHROPIC_API_KEY=sk-ant-key123 in build arg caused failure";
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new(secretError);
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — the published error tail must not contain the raw secret value
            ImageBuildOutcomeFailed failed = dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
            string errorTail = failed.ErrorTail.ShouldNotBeNull();
            errorTail.ShouldNotContain("sk-ant-key123");
            errorTail.ShouldContain("***");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_TagsWorkerImageWithTagFromDb()
    {
        // Arrange
        const string dbTag = "my-custom-worker:v2";
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            SequencedImageOperations sequenced = new();
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                imageTag: dbTag,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — worker build (index 1) must use the tag from DB, not from options
            sequenced.AllParameters.Count.ShouldBe(3);
            ImageBuildParameters workerParams = sequenced.AllParameters[1];
            workerParams.Tags.ShouldContain(dbTag);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    // Two-build wiring tests

    [Fact]
    public async Task WhenBuildSucceeds_BuildsBaseImageBeforeWorkerImage()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            SequencedImageOperations sequenced = new();
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — base image (Dockerfile.base) must be first; login image must be last
            sequenced.AllParameters.Count.ShouldBe(3);
            sequenced.AllParameters[0].Dockerfile.ShouldBe(WorkerImageRebuildService.BaseDockerfile);
            sequenced.AllParameters[1].Dockerfile.ShouldBe("Dockerfile");
            sequenced.AllParameters[2].Dockerfile.ShouldBe(WorkerImageRebuildService.LoginDockerfile);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenBaseBuildFails_DoesNotBuildWorkerImage()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            SequencedImageOperations sequenced = new(failOnFirstCall: true);
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — only the base build was attempted; worker build was skipped
            sequenced.AllParameters.Count.ShouldBe(1);
            sequenced.AllParameters[0].Dockerfile.ShouldBe(WorkerImageRebuildService.BaseDockerfile);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenBaseBuildFails_PublishesOutcomeFailed()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            SequencedImageOperations sequenced = new(failOnFirstCall: true);
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_PassesBuildArgsFromConfig()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            Dictionary<string, string> configBuildArgs = new()
            {
                ["INSTALL_DOTNET"] = "true",
                ["INSTALL_ANGULAR"] = "false",
            };
            StubGlobalSettingsQueries settingsQueries = new(settingsExists: true, buildArgs: configBuildArgs);
            SequencedImageOperations sequenced = new();
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                settingsQueries: settingsQueries,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — worker build (index 1) receives INSTALL_DOTNET from config
            sequenced.AllParameters.Count.ShouldBe(3);
            ImageBuildParameters workerParams = sequenced.AllParameters[1];
            workerParams.BuildArgs.ShouldContainKey("INSTALL_DOTNET");
            workerParams.BuildArgs["INSTALL_DOTNET"].ShouldBe("true");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_PassesBaseImageTagAsWorkerBuildArg()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            SequencedImageOperations sequenced = new();
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — worker build (index 1) receives BASE_IMAGE arg pointing at the base tag
            sequenced.AllParameters.Count.ShouldBe(3);
            ImageBuildParameters workerParams = sequenced.AllParameters[1];
            workerParams.BuildArgs.ShouldContainKey("BASE_IMAGE");
            workerParams.BuildArgs["BASE_IMAGE"].ShouldBe(WorkerImageRebuildService.BaseImageTag);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    // Login image build tests

    [Fact]
    public async Task WhenBuildSucceeds_BuildsLoginImageAfterWorkerImage()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            SequencedImageOperations sequenced = new();
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — login image (Dockerfile.login) must follow the worker image build
            sequenced.AllParameters.Count.ShouldBe(3);
            sequenced.AllParameters[2].Dockerfile.ShouldBe(WorkerImageRebuildService.LoginDockerfile);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_TagsLoginImageWithLoginImageName()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            SequencedImageOperations sequenced = new();
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — login image tag must match the constant used by the login-command query
            sequenced.AllParameters.Count.ShouldBe(3);
            ImageBuildParameters loginParams = sequenced.AllParameters[2];
            loginParams.Tags.ShouldContain(WorkerImageRebuildService.LoginImageName);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_PassesBaseImageTagToLoginBuild()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            SequencedImageOperations sequenced = new();
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — login image build receives BASE_IMAGE build arg
            sequenced.AllParameters.Count.ShouldBe(3);
            ImageBuildParameters loginParams = sequenced.AllParameters[2];
            loginParams.BuildArgs.ShouldContainKey("BASE_IMAGE");
            loginParams.BuildArgs["BASE_IMAGE"].ShouldBe(WorkerImageRebuildService.BaseImageTag);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenWorkerBuildFails_DoesNotBuildLoginImage()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            // failOnSecondCall: base succeeds, worker fails, login must be skipped
            SequencedImageOperations sequenced = new(failOnSecondCall: true);
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — only base + worker builds were attempted; login was skipped
            sequenced.AllParameters.Count.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenWorkerBuildFails_PublishesOutcomeFailed()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            SequencedImageOperations sequenced = new(failOnSecondCall: true);
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenLoginBuildFails_PublishesOutcomeFailed()
    {
        // Arrange — a broken login image leaves the guided command non-functional,
        // so we fail the entire rebuild rather than succeeding silently.
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            SequencedImageOperations sequenced = new(failOnThirdCall: true);
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    // OperationCanceledException propagation test

    [Fact]
    public async Task WhenCancellationRequested_PropagatesOperationCanceledException()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            using CancellationTokenSource cts = new();
            CancellingImageOperations cancellingImages = new(cts);
            WorkerImageRebuildService sut = BuildService(
                cancellingImages,
                contentRootPath: contentRoot);

            // Act
            OperationCanceledException ex = await Should.ThrowAsync<OperationCanceledException>(
                async () =>
                {
                    await cts.CancelAsync();
                    await sut.ProcessRebuildAsync(cts.Token);
                });

            // Assert
            ex.ShouldNotBeNull();
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task WhenCancellationRequested_DoesNotPublishOutcomeFailed()
    {
        // Arrange
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            using CancellationTokenSource cts = new();
            CapturingIntegrationEventDispatcher dispatcher = new();
            CancellingImageOperations cancellingImages = new(cts);
            WorkerImageRebuildService sut = BuildService(
                cancellingImages,
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            await cts.CancelAsync();

            // Act — OCE is expected and suppressed here; we verify no OutcomeFailed was published
            try
            {
                await sut.ProcessRebuildAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected — cancellation propagated correctly
            }

            // Assert — OutcomeFailed must not be published on cancellation
            dispatcher.Published.ShouldNotContain(e => e is ImageBuildOutcomeFailed);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    // Inspect-path tests (build disabled)

    [Fact]
    public async Task WhenBuildDisabled_DoesNotCallBuildImageFromDockerfile()
    {
        // Arrange — disabled build routes to inspect, not build
        SpyImageOperations spyImages = new();
        WorkerImageRebuildService sut = BuildService(spyImages, imageBuildEnabled: false);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert — build path was not taken
        spyImages.BuildCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenBuildDisabledAndImageExists_PublishesImageBuildRequested()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        SpyImageOperations spyImages = new();
        WorkerImageRebuildService sut = BuildService(
            spyImages,
            dispatcher: dispatcher,
            imageBuildEnabled: false);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert — ImageBuildRequested is published even for the inspect path
        dispatcher.Published.ShouldContain(e => e is ImageBuildRequested);
    }

    [Fact]
    public async Task WhenBuildDisabledAndImageExists_PublishesImageBuildSucceeded()
    {
        // Arrange — SpyImageOperations.InspectImageAsync returns successfully (image exists)
        CapturingIntegrationEventDispatcher dispatcher = new();
        SpyImageOperations spyImages = new();
        WorkerImageRebuildService sut = BuildService(
            spyImages,
            dispatcher: dispatcher,
            imageBuildEnabled: false);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert — inspect succeeded → ImageBuildSucceeded published
        dispatcher.Published.Count.ShouldBe(2);
        dispatcher.Published[0].ShouldBeOfType<ImageBuildRequested>();
        dispatcher.Published[1].ShouldBeOfType<ImageBuildSucceeded>();
    }

    [Fact]
    public async Task WhenBuildDisabledAndImageMissing_PublishesImageBuildOutcomeFailedWithTagNamedMessage()
    {
        // Arrange — InspectImageAsync throws DockerImageNotFoundException
        const string tag = "my-worker:v2";
        CapturingIntegrationEventDispatcher dispatcher = new();
        NotFoundImageOperations notFoundImages = new();
        WorkerImageRebuildService sut = BuildService(
            notFoundImages,
            dispatcher: dispatcher,
            imageBuildEnabled: false,
            imageTag: tag);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert — error message names the tag
        dispatcher.Published.Count.ShouldBe(2);
        dispatcher.Published[0].ShouldBeOfType<ImageBuildRequested>();
        ImageBuildOutcomeFailed failed = dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
        failed.ErrorTail.ShouldBe($"image '{tag}' not found on the Docker host");
    }

    [Fact]
    public async Task WhenBuildDisabledAndDaemonDown_PublishesImageBuildOutcomeFailedWithRedactedError()
    {
        // Arrange — InspectImageAsync throws a non-not-found exception (daemon/API error)
        CapturingIntegrationEventDispatcher dispatcher = new();
        InspectThrowingImageOperations throwingImages = new("connection refused");
        WorkerImageRebuildService sut = BuildService(
            throwingImages,
            dispatcher: dispatcher,
            imageBuildEnabled: false);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert — daemon error surfaces as OutcomeFailed
        dispatcher.Published.Count.ShouldBe(2);
        dispatcher.Published[0].ShouldBeOfType<ImageBuildRequested>();
        dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
    }

    [Fact]
    public async Task WhenBuildDisabledAndDaemonDownWithSecret_RedactsError()
    {
        // Arrange
        const string secretError = "pull denied: https://user:sk-ant-api03-secret@registry.example.com/worker";
        CapturingIntegrationEventDispatcher dispatcher = new();
        InspectThrowingImageOperations throwingImages = new(secretError);
        WorkerImageRebuildService sut = BuildService(
            throwingImages,
            dispatcher: dispatcher,
            imageBuildEnabled: false);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert — secret is redacted from the published error
        ImageBuildOutcomeFailed failed = dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
        string errorTail = failed.ErrorTail.ShouldNotBeNull();
        errorTail.ShouldNotContain("sk-ant-api03-secret");
        errorTail.ShouldContain("***");
    }

    [Fact]
    public async Task WhenBuildDisabledAndImageMissingWithOverLongTag_TruncatesErrorTail()
    {
        // Arrange — tag longer than LogTailLines * 120 chars (200 * 120 = 24000) to force truncation
        string longTag = new string('a', 25000);
        CapturingIntegrationEventDispatcher dispatcher = new();
        NotFoundImageOperations notFoundImages = new();
        WorkerImageRebuildService sut = BuildService(
            notFoundImages,
            dispatcher: dispatcher,
            imageBuildEnabled: false,
            imageTag: longTag);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert — error message is truncated (cap = LogTailLines * 120 = 24000 chars)
        ImageBuildOutcomeFailed failed = dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
        string errorTail = failed.ErrorTail.ShouldNotBeNull();
        errorTail.Length.ShouldBeLessThanOrEqualTo(24000);
        // The template text "not found on the Docker host" must be preserved at the tail
        errorTail.ShouldEndWith("not found on the Docker host");
    }

    [Fact]
    public async Task WhenBuildDisabledAndImageMissing_ReusesAttemptProgressionForBackoff()
    {
        // Arrange — persisted Attempt=1 means next failure should report Attempt=2
        const string tag = "my-worker:v3";
        CapturingIntegrationEventDispatcher dispatcher = new();
        NotFoundImageOperations notFoundImages = new();
        WorkerImageRebuildService sut = BuildService(
            notFoundImages,
            settingsQueries: new StubGlobalSettingsQueries(attempt: 1, buildEnabled: false, imageTag: tag),
            dispatcher: dispatcher,
            imageBuildEnabled: false,
            imageTag: tag);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert — same backoff progression as the build path
        ImageBuildOutcomeFailed failed = dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
        failed.Attempt.ShouldBe(2);
        failed.NextRetryAt.ShouldNotBeNull();
    }

    // Restart-continuity regression test (AC#1)

    [Fact]
    public async Task WhenPersistedStateIsFailedWithAttempt_FailureContinuesProgression()
    {
        // Arrange — a fresh service (simulating host restart) whose settings query returns Attempt=3.
        // The service must continue the progression: backoff for attempt 4, not attempt 1.
        // With constants InitialBackoff=30s, attempt-4 backoff = 30s * 2^3 = 240s,
        // which is clearly distinguishable from attempt-1 backoff (30s).
        (string contentRoot, string tempRoot) = CreateTempContextTree();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new("build error");
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                settingsQueries: new StubGlobalSettingsQueries(attempt: 3),
                dispatcher: dispatcher,
                contentRootPath: contentRoot);

            DateTimeOffset before = DateTimeOffset.UtcNow;

            // Act — a fresh service instance with persisted Attempt=3 fails
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — Attempt must be 4 (not 1, which would indicate the counter reset on restart)
            IIntegrationEvent last = dispatcher.Published[^1];
            ImageBuildOutcomeFailed failed = last.ShouldBeOfType<ImageBuildOutcomeFailed>();
            failed.Attempt.ShouldBe(4);

            // NextRetryAt must reflect backoff for attempt 4 with 30s initial: 30s * 2^3 = 240s.
            // Verify NextRetryAt is in the future, which proves attempt-progression (not reset to 1).
            failed.NextRetryAt.ShouldNotBeNull();
            failed.NextRetryAt.Value.ShouldBeGreaterThan(before);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    /// <summary>
    /// Creates a temporary directory tree that matches what <see cref="WorkerImageRebuildService"/> expects.
    /// The service resolves <c>solutionRoot/workers</c> as the Docker build context, where
    /// <c>solutionRoot = contentRoot/../../</c>. Returns the <c>contentRootPath</c> to pass to
    /// <see cref="BuildService"/> and the root temp directory to delete in <c>finally</c>.
    /// </summary>
    private static (string ContentRootPath, string TempRoot) CreateTempContextTree()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}");
        // solutionRoot = tempRoot; contentRoot = tempRoot/src/WebApi
        string contentRoot = Path.Combine(tempRoot, "src", "WebApi");
        string workersDir = Path.Combine(tempRoot, "workers");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(workersDir);
        File.WriteAllText(Path.Combine(workersDir, "Dockerfile"), "FROM scratch");
        return (contentRoot, tempRoot);
    }

    private sealed class SpyImageOperations : IImageOperations
    {
        public bool BuildCalled { get; private set; }
        public ImageBuildParameters? LastParameters { get; private set; }

        public Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
        {
            BuildCalled = true;
            LastParameters = parameters;
            return Task.CompletedTask;
        }

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(
            Stream contents,
            ImageBuildParameters parameters,
            CancellationToken cancellationToken)
        {
            BuildCalled = true;
            LastParameters = parameters;
            return Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
        }
#pragma warning restore CS0618

        public Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImagesListResponse>>([]);
        public Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult(new ImageInspectResponse());
        public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<IDictionary<string, string>>>([]);
        public Task<IList<ImageSearchResponse>> SearchImagesAsync(ImagesSearchParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImageSearchResponse>>([]);
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadImageAsync(ImageLoadParameters parameters, Stream imageStream, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> SaveImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> SaveImagesAsync(string[] names, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task TagImageAsync(string name, ImageTagParameters parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PushImageAsync(string name, ImagePushParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ImagesPruneResponse> PruneImagesAsync(ImagesPruneParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new ImagesPruneResponse());
        public Task<CommitContainerChangesResponse> CommitContainerChangesAsync(CommitContainerChangesParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new CommitContainerChangesResponse());
        public Task<IList<ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken) => Task.FromResult<IList<ImageHistoryResponse>>([]);
    }

    private sealed class ErrorReportingImageOperations(string errorMessage) : IImageOperations
    {
        public Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
        {
            progress.Report(new JSONMessage
            {
                Error = new JSONError { Message = errorMessage },
            });
            return Task.CompletedTask;
        }

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

        public Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImagesListResponse>>([]);
        public Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult(new ImageInspectResponse());
        public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<IDictionary<string, string>>>([]);
        public Task<IList<ImageSearchResponse>> SearchImagesAsync(ImagesSearchParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImageSearchResponse>>([]);
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadImageAsync(ImageLoadParameters parameters, Stream imageStream, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> SaveImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> SaveImagesAsync(string[] names, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task TagImageAsync(string name, ImageTagParameters parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PushImageAsync(string name, ImagePushParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ImagesPruneResponse> PruneImagesAsync(ImagesPruneParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new ImagesPruneResponse());
        public Task<CommitContainerChangesResponse> CommitContainerChangesAsync(CommitContainerChangesParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new CommitContainerChangesResponse());
        public Task<IList<ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken) => Task.FromResult<IList<ImageHistoryResponse>>([]);
    }

    private sealed class ThrowingImageOperations(string message) : IImageOperations
    {
        public Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(message);

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

        public Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImagesListResponse>>([]);
        public Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult(new ImageInspectResponse());
        public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<IDictionary<string, string>>>([]);
        public Task<IList<ImageSearchResponse>> SearchImagesAsync(ImagesSearchParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImageSearchResponse>>([]);
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadImageAsync(ImageLoadParameters parameters, Stream imageStream, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> SaveImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> SaveImagesAsync(string[] names, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task TagImageAsync(string name, ImageTagParameters parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PushImageAsync(string name, ImagePushParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ImagesPruneResponse> PruneImagesAsync(ImagesPruneParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new ImagesPruneResponse());
        public Task<CommitContainerChangesResponse> CommitContainerChangesAsync(CommitContainerChangesParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new CommitContainerChangesResponse());
        public Task<IList<ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken) => Task.FromResult<IList<ImageHistoryResponse>>([]);
    }

    private sealed class CancellingImageOperations(CancellationTokenSource cts) : IImageOperations
    {
        public Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
            => Task.FromCanceled(cts.Token);

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

        public Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImagesListResponse>>([]);
        public Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult(new ImageInspectResponse());
        public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<IDictionary<string, string>>>([]);
        public Task<IList<ImageSearchResponse>> SearchImagesAsync(ImagesSearchParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImageSearchResponse>>([]);
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadImageAsync(ImageLoadParameters parameters, Stream imageStream, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> SaveImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> SaveImagesAsync(string[] names, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task TagImageAsync(string name, ImageTagParameters parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PushImageAsync(string name, ImagePushParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ImagesPruneResponse> PruneImagesAsync(ImagesPruneParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new ImagesPruneResponse());
        public Task<CommitContainerChangesResponse> CommitContainerChangesAsync(CommitContainerChangesParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new CommitContainerChangesResponse());
        public Task<IList<ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken) => Task.FromResult<IList<ImageHistoryResponse>>([]);
    }

    /// <summary>
    /// Records all build calls in sequence. Optionally reports an error on a specific call to
    /// simulate base, worker, or login image build failures.
    /// </summary>
    private sealed class SequencedImageOperations(
        bool failOnFirstCall = false,
        bool failOnSecondCall = false,
        bool failOnThirdCall = false) : IImageOperations
    {
        private readonly List<ImageBuildParameters> _allParameters = [];

        public IReadOnlyList<ImageBuildParameters> AllParameters => _allParameters;

        public Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
        {
            int callIndex = _allParameters.Count;
            _allParameters.Add(parameters);

            bool shouldFail = (failOnFirstCall && callIndex == 0)
                || (failOnSecondCall && callIndex == 1)
                || (failOnThirdCall && callIndex == 2);

            if (shouldFail)
            {
                progress.Report(new JSONMessage
                {
                    Error = new JSONError { Message = $"build {callIndex + 1} failed" },
                });
            }

            return Task.CompletedTask;
        }

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

        public Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImagesListResponse>>([]);
        public Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult(new ImageInspectResponse());
        public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<IDictionary<string, string>>>([]);
        public Task<IList<ImageSearchResponse>> SearchImagesAsync(ImagesSearchParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImageSearchResponse>>([]);
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadImageAsync(ImageLoadParameters parameters, Stream imageStream, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> SaveImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> SaveImagesAsync(string[] names, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task TagImageAsync(string name, ImageTagParameters parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PushImageAsync(string name, ImagePushParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ImagesPruneResponse> PruneImagesAsync(ImagesPruneParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new ImagesPruneResponse());
        public Task<CommitContainerChangesResponse> CommitContainerChangesAsync(CommitContainerChangesParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new CommitContainerChangesResponse());
        public Task<IList<ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken) => Task.FromResult<IList<ImageHistoryResponse>>([]);
    }

    /// <summary>
    /// Throws <see cref="Docker.DotNet.DockerImageNotFoundException"/> on <see cref="InspectImageAsync"/>
    /// to simulate a missing image on the Docker host.
    /// </summary>
    private sealed class NotFoundImageOperations : IImageOperations
    {
        public Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

        public Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImagesListResponse>>([]);
        public Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken)
            => throw new Docker.DotNet.DockerImageNotFoundException(System.Net.HttpStatusCode.NotFound, "No such image");
        public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<IDictionary<string, string>>>([]);
        public Task<IList<ImageSearchResponse>> SearchImagesAsync(ImagesSearchParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImageSearchResponse>>([]);
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadImageAsync(ImageLoadParameters parameters, Stream imageStream, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> SaveImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> SaveImagesAsync(string[] names, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task TagImageAsync(string name, ImageTagParameters parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PushImageAsync(string name, ImagePushParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ImagesPruneResponse> PruneImagesAsync(ImagesPruneParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new ImagesPruneResponse());
        public Task<CommitContainerChangesResponse> CommitContainerChangesAsync(CommitContainerChangesParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new CommitContainerChangesResponse());
        public Task<IList<ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken) => Task.FromResult<IList<ImageHistoryResponse>>([]);
    }

    /// <summary>
    /// Throws a non-not-found exception on <see cref="InspectImageAsync"/> to simulate a daemon/API error.
    /// </summary>
    private sealed class InspectThrowingImageOperations(string errorMessage) : IImageOperations
    {
        public Task BuildImageFromDockerfileAsync(
            ImageBuildParameters parameters,
            Stream contents,
            IEnumerable<AuthConfig> authConfigs,
            IDictionary<string, string> headers,
            IProgress<JSONMessage> progress,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

#pragma warning disable CS0618 // Required for interface compliance
        public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream("{}"u8.ToArray()));
#pragma warning restore CS0618

        public Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImagesListResponse>>([]);
        public Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken)
            => throw new InvalidOperationException(errorMessage);
        public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<IDictionary<string, string>>>([]);
        public Task<IList<ImageSearchResponse>> SearchImagesAsync(ImagesSearchParameters parameters, CancellationToken cancellationToken) => Task.FromResult<IList<ImageSearchResponse>>([]);
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadImageAsync(ImageLoadParameters parameters, Stream imageStream, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> SaveImageAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task<Stream> SaveImagesAsync(string[] names, CancellationToken cancellationToken) => Task.FromResult<Stream>(Stream.Null);
        public Task TagImageAsync(string name, ImageTagParameters parameters, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PushImageAsync(string name, ImagePushParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ImagesPruneResponse> PruneImagesAsync(ImagesPruneParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new ImagesPruneResponse());
        public Task<CommitContainerChangesResponse> CommitContainerChangesAsync(CommitContainerChangesParameters parameters, CancellationToken cancellationToken) => Task.FromResult(new CommitContainerChangesResponse());
        public Task<IList<ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken) => Task.FromResult<IList<ImageHistoryResponse>>([]);
    }

    private sealed class NullWorkerImageRebuildQueue : IWorkerImageRebuildQueue
    {
        public event Action? ImmediateRebuildRequested;

        public void RequestImmediateRebuild()
        {
            ImmediateRebuildRequested?.Invoke();
            TryEnqueue();
        }

        public bool TryEnqueue() => false;

        public async IAsyncEnumerable<bool> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Foundry.WebApi";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
    }

    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _published = [];

        public IReadOnlyList<IIntegrationEvent> Published => _published;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _published.AddRange(events);
            return Task.CompletedTask;
        }
    }

}
