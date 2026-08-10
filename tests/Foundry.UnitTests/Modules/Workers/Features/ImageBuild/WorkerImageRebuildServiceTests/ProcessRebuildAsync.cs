using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        string? contextPath = null,
        bool imageBuildEnabled = true,
        TimeSpan? initialBackoff = null,
        IWorkerImageRebuildQueue? queue = null)
    {
        ServiceCollection services = new();
        services.AddScoped<IGlobalSettingsQueries>(_ =>
            settingsQueries ?? new StubGlobalSettingsQueries(settingsExists: true));
        services.AddScoped<IIntegrationEventDispatcher>(_ =>
            dispatcher ?? new CapturingIntegrationEventDispatcher());

        ServiceProvider sp = services.BuildServiceProvider();

        WorkerOptions workerOptions = new()
        {
            Image = "test-image:latest",
            ImageBuild = new ImageBuildOptions
            {
                Enabled = imageBuildEnabled,
                ContextPath = contextPath ?? string.Empty,
                InitialBackoff = initialBackoff ?? TimeSpan.FromSeconds(30),
            },
        };

        StubHostEnvironment hostEnv = new(contentRootPath);

        return new WorkerImageRebuildService(
            queue ?? new NullWorkerImageRebuildQueue(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            imageOperations,
            hostEnv,
            Options.Create(workerOptions),
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
        string contextDir = CreateTempContextDir();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            WorkerImageRebuildService sut = BuildService(
                new SpyImageOperations(),
                dispatcher: dispatcher,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — ImageBuildRequested must be published before ImageBuildSucceeded
            dispatcher.Published.Count.ShouldBeGreaterThanOrEqualTo(1);
            dispatcher.Published[0].ShouldBeOfType<ImageBuildRequested>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_PublishesImageBuildSucceeded()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            WorkerImageRebuildService sut = BuildService(
                new SpyImageOperations(),
                dispatcher: dispatcher,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — ImageBuildSucceeded published after ImageBuildRequested
            dispatcher.Published.Count.ShouldBe(2);
            dispatcher.Published[0].ShouldBeOfType<ImageBuildRequested>();
            dispatcher.Published[1].ShouldBeOfType<ImageBuildSucceeded>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsError_PublishesImageBuildOutcomeFailed()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            const string dockerError = "Build failed: layer error";
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new(dockerError);
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                dispatcher: dispatcher,
                contextPath: contextDir);

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
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsError_PublishedFailedEventHasAttemptOne()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new("build error");
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                dispatcher: dispatcher,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            ImageBuildOutcomeFailed failed = dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
            failed.Attempt.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsError_PublishedFailedEventHasNextRetryAtInFuture()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            DateTimeOffset before = DateTimeOffset.UtcNow;
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new("build error");
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                dispatcher: dispatcher,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            ImageBuildOutcomeFailed failed = dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
            failed.NextRetryAt.ShouldNotBeNull();
            failed.NextRetryAt.Value.ShouldBeGreaterThan(before);
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBuildFailsTwice_SecondFailedEventHasAttemptTwo()
    {
        // Arrange — tiny backoff so test runs quickly
        string contextDir = CreateTempContextDir();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new("build error");
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                dispatcher: dispatcher,
                contextPath: contextDir,
                initialBackoff: TimeSpan.FromMilliseconds(1));

            // Act — call twice on the same service instance to accumulate attempt
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — last published event is OutcomeFailed with attempt=2
            IIntegrationEvent last = dispatcher.Published[^1];
            ImageBuildOutcomeFailed failed = last.ShouldBeOfType<ImageBuildOutcomeFailed>();
            failed.Attempt.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceedsAfterFailure_PublishesSucceeded()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            CapturingIntegrationEventDispatcher failDispatcher = new();
            ErrorReportingImageOperations errorImages = new("build error");
            WorkerImageRebuildService firstSut = BuildService(
                errorImages,
                dispatcher: failDispatcher,
                contextPath: contextDir,
                initialBackoff: TimeSpan.FromMilliseconds(1));

            // Fail once
            await firstSut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Now succeed — new service instance, new dispatcher
            CapturingIntegrationEventDispatcher successDispatcher = new();
            WorkerImageRebuildService sut = BuildService(
                new SpyImageOperations(),
                dispatcher: successDispatcher,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — success path publishes Requested then Succeeded
            successDispatcher.Published.Count.ShouldBe(2);
            successDispatcher.Published[0].ShouldBeOfType<ImageBuildRequested>();
            successDispatcher.Published[1].ShouldBeOfType<ImageBuildSucceeded>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenDockerThrowsException_PublishesOutcomeFailed()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            ThrowingImageOperations throwingImages = new("connection refused");
            WorkerImageRebuildService sut = BuildService(
                throwingImages,
                dispatcher: dispatcher,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            dispatcher.Published.Count.ShouldBe(2);
            dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenDockerThrowsExceptionWithSecret_RedactsSecretFromPublishedErrorTail()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            const string secretMessage = "pull access denied: https://user:sk-ant-api03-secret@registry.example.com/image";
            CapturingIntegrationEventDispatcher dispatcher = new();
            ThrowingImageOperations throwingImages = new(secretMessage);
            WorkerImageRebuildService sut = BuildService(
                throwingImages,
                dispatcher: dispatcher,
                contextPath: contextDir);

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
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsErrorWithSecret_RedactsSecretFromPublishedErrorTail()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            const string secretError = "https://glpat-abc123secret@gitlab.example.com/image build failed";
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new(secretError);
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                dispatcher: dispatcher,
                contextPath: contextDir);

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
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenDockerReportsErrorWithBuildArgSecret_RedactsSecretFromPublishedErrorTail()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            const string secretError = "ANTHROPIC_API_KEY=sk-ant-key123 in build arg caused failure";
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new(secretError);
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                dispatcher: dispatcher,
                contextPath: contextDir);

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
            Directory.Delete(contextDir, true);
        }
    }

    // Two-build wiring tests

    [Fact]
    public async Task WhenBuildSucceeds_BuildsBaseImageBeforeWorkerImage()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SequencedImageOperations sequenced = new();
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contextPath: contextDir);

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
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBaseBuildFails_DoesNotBuildWorkerImage()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SequencedImageOperations sequenced = new(failOnFirstCall: true);
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — only the base build was attempted; worker build was skipped
            sequenced.AllParameters.Count.ShouldBe(1);
            sequenced.AllParameters[0].Dockerfile.ShouldBe(WorkerImageRebuildService.BaseDockerfile);
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBaseBuildFails_PublishesOutcomeFailed()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            SequencedImageOperations sequenced = new(failOnFirstCall: true);
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                dispatcher: dispatcher,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_PassesBuildArgsFromConfig()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

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
                contextPath: contextDir);

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
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_PassesBaseImageTagAsWorkerBuildArg()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SequencedImageOperations sequenced = new();
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contextPath: contextDir);

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
            Directory.Delete(contextDir, true);
        }
    }

    // Login image build tests

    [Fact]
    public async Task WhenBuildSucceeds_BuildsLoginImageAfterWorkerImage()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SequencedImageOperations sequenced = new();
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — login image (Dockerfile.login) must follow the worker image build
            sequenced.AllParameters.Count.ShouldBe(3);
            sequenced.AllParameters[2].Dockerfile.ShouldBe(WorkerImageRebuildService.LoginDockerfile);
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_TagsLoginImageWithLoginImageName()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SequencedImageOperations sequenced = new();
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — login image tag must match the constant used by the login-command query
            sequenced.AllParameters.Count.ShouldBe(3);
            ImageBuildParameters loginParams = sequenced.AllParameters[2];
            loginParams.Tags.ShouldContain(WorkerImageRebuildService.LoginImageName);
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenBuildSucceeds_PassesBaseImageTagToLoginBuild()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            SequencedImageOperations sequenced = new();
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contextPath: contextDir);

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
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenWorkerBuildFails_DoesNotBuildLoginImage()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            // failOnSecondCall: base succeeds, worker fails, login must be skipped
            SequencedImageOperations sequenced = new(failOnSecondCall: true);
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — only base + worker builds were attempted; login was skipped
            sequenced.AllParameters.Count.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenWorkerBuildFails_PublishesOutcomeFailed()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            SequencedImageOperations sequenced = new(failOnSecondCall: true);
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                dispatcher: dispatcher,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenLoginBuildFails_PublishesOutcomeFailed()
    {
        // Arrange — a broken login image leaves the guided command non-functional,
        // so we fail the entire rebuild rather than succeeding silently.
        string contextDir = CreateTempContextDir();

        try
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            SequencedImageOperations sequenced = new(failOnThirdCall: true);
            WorkerImageRebuildService sut = BuildService(
                sequenced,
                dispatcher: dispatcher,
                contextPath: contextDir);

            // Act
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert
            dispatcher.Published[1].ShouldBeOfType<ImageBuildOutcomeFailed>();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    // OperationCanceledException propagation test

    [Fact]
    public async Task WhenCancellationRequested_PropagatesOperationCanceledException()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            using CancellationTokenSource cts = new();
            CancellingImageOperations cancellingImages = new(cts);
            WorkerImageRebuildService sut = BuildService(
                cancellingImages,
                contextPath: contextDir);

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
            Directory.Delete(contextDir, true);
        }
    }

    [Fact]
    public async Task WhenCancellationRequested_DoesNotPublishOutcomeFailed()
    {
        // Arrange
        string contextDir = CreateTempContextDir();

        try
        {
            using CancellationTokenSource cts = new();
            CapturingIntegrationEventDispatcher dispatcher = new();
            CancellingImageOperations cancellingImages = new(cts);
            WorkerImageRebuildService sut = BuildService(
                cancellingImages,
                dispatcher: dispatcher,
                contextPath: contextDir);

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
            Directory.Delete(contextDir, true);
        }
    }

    // Enabled flag tests

    [Fact]
    public async Task WhenImageBuildDisabled_DoesNotBuildImage()
    {
        // Arrange
        SpyImageOperations spyImages = new();
        WorkerImageRebuildService sut = BuildService(spyImages, imageBuildEnabled: false);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert
        spyImages.BuildCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenImageBuildDisabled_PublishesNoEvents()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerImageRebuildService sut = BuildService(
            new SpyImageOperations(),
            dispatcher: dispatcher,
            imageBuildEnabled: false);

        // Act
        await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Published.ShouldBeEmpty();
    }

    // Backoff state tests

    [Fact]
    public async Task WhenFailureThenSupersede_AttemptResetsAndNextFailureStartsAtOne()
    {
        // Arrange — verifies the CODE-H1 invariant: after a supersede cancels the pending retry,
        // the next failure path atomically reads attempt=1 (reset + increment are one lock scope).
        // The service must be subscribed (via StartingAsync) before ImmediateRebuildRequested fires.
        string contextDir = CreateTempContextDir();

        try
        {
            SupersedingQueue queue = new();
            CapturingIntegrationEventDispatcher dispatcher = new();
            ErrorReportingImageOperations errorImages = new("build error");
            WorkerImageRebuildService sut = BuildService(
                errorImages,
                dispatcher: dispatcher,
                contextPath: contextDir,
                queue: queue,
                initialBackoff: TimeSpan.FromMilliseconds(1));

            // StartingAsync subscribes the event handler
            await sut.StartingAsync(TestContext.Current.CancellationToken);

            // Act — first failure accumulates attempt=1
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Supersede resets _attempt=0 atomically via the registered handler
            queue.RaiseImmediateRebuild();

            // Second failure must start fresh: attempt increments from 0 → 1
            await sut.ProcessRebuildAsync(TestContext.Current.CancellationToken);

            // Assert — last published OutcomeFailed must have attempt=1
            IIntegrationEvent last = dispatcher.Published[^1];
            ImageBuildOutcomeFailed failed = last.ShouldBeOfType<ImageBuildOutcomeFailed>();
            failed.Attempt.ShouldBe(1);

            // Disposal must not throw — no orphaned CTS
            sut.Dispose();
        }
        finally
        {
            Directory.Delete(contextDir, true);
        }
    }

    private static string CreateTempContextDir()
    {
        string contextDir = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(contextDir);
        File.WriteAllText(Path.Combine(contextDir, "Dockerfile"), "FROM scratch");
        return contextDir;
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
    /// A queue that allows tests to directly raise ImmediateRebuildRequested to simulate supersede events.
    /// </summary>
    private sealed class SupersedingQueue : IWorkerImageRebuildQueue
    {
        private event Action? _immediateRebuildRequested;

        public event Action? ImmediateRebuildRequested
        {
            add => _immediateRebuildRequested += value;
            remove => _immediateRebuildRequested -= value;
        }

        public void RaiseImmediateRebuild()
        {
            _immediateRebuildRequested?.Invoke();
        }

        public void RequestImmediateRebuild()
        {
            _immediateRebuildRequested?.Invoke();
        }

        public bool TryEnqueue() => false;

        public async IAsyncEnumerable<bool> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
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

    /// <summary>
    /// Stubs <see cref="IGlobalSettingsQueries"/> so tests do not need a database.
    /// When <paramref name="settingsExists"/> is false, <see cref="GetSettingsAsync"/> returns null,
    /// triggering the early-return path. When true, returns a minimal populated summary.
    /// </summary>
    private sealed class StubGlobalSettingsQueries(
        bool settingsExists,
        IReadOnlyDictionary<string, string>? buildArgs = null) : IGlobalSettingsQueries
    {
        private static readonly GlobalSettingsSummary DefaultSummary = new(
            MaxConcurrent: 1,
            TimeoutMinutes: 60,
            SystemPromptTemplate: null,
            WorkerPromptTemplate: null,
            UsageLimitResetsAt: null,
            IsDispatchPaused: false,
            AutoResumeOnUsageReset: true,
            DefaultCooldownMinutes: 0,
            InstallDotnet: false,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false,
            ImageBuildStatus: ImageBuildStatus.Idle,
            LastImageBuildError: null,
            HasUsableImage: false,
            NextRetryAt: null,
            Attempt: 0);

        public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult(settingsExists ? DefaultSummary : (GlobalSettingsSummary?)null);

        public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(1);

        public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(60);

        public Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<(string?, string?)>((null, null));

        public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
            => Task.FromResult(new DispatchPauseState(null, false, true));

        public Task<int> GetDefaultCooldownMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(buildArgs ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>());
    }
}
