using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Modules.Settings.Features;
using Foundry.Modules.Settings.Infrastructure;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Infrastructure.CredentialVolumeReaderTests;

public sealed class ReadStatusAsync : IAsyncDisposable
{
    // _tempDir serves as both the Docker volumes root (injected into the reader) and the
    // mountpoint for tests that exercise the credential-reading code path.
    private readonly string _tempDir;

    public ReadStatusAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await Task.CompletedTask;
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private CredentialVolumeReader BuildSut(StubVolumeOperations volumeOps) =>
        new(volumeOps, NullLogger<CredentialVolumeReader>.Instance, dockerVolumesRoot: _tempDir);

    [Fact]
    public async Task WhenVolumeNotFound_ReturnsPresentFalse()
    {
        // Arrange
        StubVolumeOperations volumeOps = new(throwDockerApiException: true);
        CredentialVolumeReader sut = BuildSut(volumeOps);

        // Act
        CredentialVolumeStatus result = await sut.ReadStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Present.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenVolumeNotFound_ReturnsNullExpiresAtAndSubscriptionType()
    {
        // Arrange
        StubVolumeOperations volumeOps = new(throwDockerApiException: true);
        CredentialVolumeReader sut = BuildSut(volumeOps);

        // Act
        CredentialVolumeStatus result = await sut.ReadStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ExpiresAt.ShouldBeNull(),
            () => result.SubscriptionType.ShouldBeNull());
    }

    [Fact]
    public async Task WhenMountpointIsEmpty_ReturnsPresentFalse()
    {
        // Arrange
        StubVolumeOperations volumeOps = new(mountpoint: string.Empty);
        CredentialVolumeReader sut = BuildSut(volumeOps);

        // Act
        CredentialVolumeStatus result = await sut.ReadStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Present.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenCredentialFileAbsent_ReturnsPresentFalse()
    {
        // Arrange — empty temp dir, no .credentials.json
        StubVolumeOperations volumeOps = new(mountpoint: _tempDir);
        CredentialVolumeReader sut = BuildSut(volumeOps);

        // Act
        CredentialVolumeStatus result = await sut.ReadStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Present.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenCredentialFilePresent_ReturnsPresentTrue()
    {
        // Arrange
        string credFile = Path.Combine(_tempDir, ".credentials.json");
        string json = BuildCredentialJson(expiresAt: "2027-01-01T00:00:00Z", subscriptionType: "pro");
        await File.WriteAllTextAsync(credFile, json, TestContext.Current.CancellationToken);
        StubVolumeOperations volumeOps = new(mountpoint: _tempDir);
        CredentialVolumeReader sut = BuildSut(volumeOps);

        // Act
        CredentialVolumeStatus result = await sut.ReadStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Present.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenCredentialFileHasExpiresAt_ReturnsExpiresAt()
    {
        // Arrange
        string credFile = Path.Combine(_tempDir, ".credentials.json");
        string json = BuildCredentialJson(expiresAt: "2027-06-15T12:00:00Z", subscriptionType: "pro");
        await File.WriteAllTextAsync(credFile, json, TestContext.Current.CancellationToken);
        StubVolumeOperations volumeOps = new(mountpoint: _tempDir);
        CredentialVolumeReader sut = BuildSut(volumeOps);

        // Act
        CredentialVolumeStatus result = await sut.ReadStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        DateTimeOffset expected = new(2027, 6, 15, 12, 0, 0, TimeSpan.Zero);
        result.ExpiresAt.ShouldBe(expected);
    }

    [Fact]
    public async Task WhenCredentialFileHasEpochMillisExpiresAt_ReturnsExpiresAt()
    {
        // Arrange
        DateTimeOffset expiry = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long epochMs = expiry.ToUnixTimeMilliseconds();
        string credFile = Path.Combine(_tempDir, ".credentials.json");
        string json = BuildCredentialJsonWithEpoch(epochMs, "pro");
        await File.WriteAllTextAsync(credFile, json, TestContext.Current.CancellationToken);
        StubVolumeOperations volumeOps = new(mountpoint: _tempDir);
        CredentialVolumeReader sut = BuildSut(volumeOps);

        // Act
        CredentialVolumeStatus result = await sut.ReadStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ExpiresAt.ShouldBe(expiry);
    }

    [Fact]
    public async Task WhenCredentialFileHasSubscriptionType_ReturnsSubscriptionType()
    {
        // Arrange
        string credFile = Path.Combine(_tempDir, ".credentials.json");
        string json = BuildCredentialJson(expiresAt: "2027-01-01T00:00:00Z", subscriptionType: "max");
        await File.WriteAllTextAsync(credFile, json, TestContext.Current.CancellationToken);
        StubVolumeOperations volumeOps = new(mountpoint: _tempDir);
        CredentialVolumeReader sut = BuildSut(volumeOps);

        // Act
        CredentialVolumeStatus result = await sut.ReadStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.SubscriptionType.ShouldBe("max");
    }

    [Fact]
    public async Task WhenCredentialFileIsMalformedJson_ReturnsPresentTrueWithNullFields()
    {
        // Arrange
        string credFile = Path.Combine(_tempDir, ".credentials.json");
        await File.WriteAllTextAsync(credFile, "{ not valid json }}}", TestContext.Current.CancellationToken);
        StubVolumeOperations volumeOps = new(mountpoint: _tempDir);
        CredentialVolumeReader sut = BuildSut(volumeOps);

        // Act
        CredentialVolumeStatus result = await sut.ReadStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Present.ShouldBeTrue(),
            () => result.ExpiresAt.ShouldBeNull(),
            () => result.SubscriptionType.ShouldBeNull());
    }

    [Fact]
    public async Task WhenMountpointIsOutsideDockerVolumesRoot_ReturnsPresentFalse()
    {
        // Arrange — mountpoint path does not start with the configured Docker volumes root (_tempDir)
        string outsidePath = Path.Combine(Path.GetTempPath(), "attacker-controlled-" + Path.GetRandomFileName());
        StubVolumeOperations volumeOps = new(mountpoint: outsidePath);
        CredentialVolumeReader sut = BuildSut(volumeOps);

        // Act
        CredentialVolumeStatus result = await sut.ReadStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Present.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenMountpointIsOutsideDockerVolumesRoot_DoesNotReadFile()
    {
        // Arrange — a credentials file exists in a separate dir, but the mountpoint is outside the root
        string outsideDir = Path.Combine(Path.GetTempPath(), "attacker-" + Path.GetRandomFileName());
        Directory.CreateDirectory(outsideDir);
        try
        {
            string credFile = Path.Combine(outsideDir, ".credentials.json");
            string json = BuildCredentialJson(expiresAt: "2027-01-01T00:00:00Z", subscriptionType: "pro");
            await File.WriteAllTextAsync(credFile, json, TestContext.Current.CancellationToken);
            StubVolumeOperations volumeOps = new(mountpoint: outsideDir);
            CredentialVolumeReader sut = BuildSut(volumeOps);

            // Act
            CredentialVolumeStatus result = await sut.ReadStatusAsync(TestContext.Current.CancellationToken);

            // Assert — file must not be read because mountpoint is outside allowed root
            result.ShouldSatisfyAllConditions(
                () => result.Present.ShouldBeFalse(),
                () => result.ExpiresAt.ShouldBeNull(),
                () => result.SubscriptionType.ShouldBeNull());
        }
        finally
        {
            Directory.Delete(outsideDir, recursive: true);
        }
    }

    [Fact]
    public async Task WhenCredentialFileHasNoClaudeAiOauthSection_ReturnsPresentTrueWithNullFields()
    {
        // Arrange
        string credFile = Path.Combine(_tempDir, ".credentials.json");
        await File.WriteAllTextAsync(credFile, """{ "other": {} }""", TestContext.Current.CancellationToken);
        StubVolumeOperations volumeOps = new(mountpoint: _tempDir);
        CredentialVolumeReader sut = BuildSut(volumeOps);

        // Act
        CredentialVolumeStatus result = await sut.ReadStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Present.ShouldBeTrue(),
            () => result.ExpiresAt.ShouldBeNull(),
            () => result.SubscriptionType.ShouldBeNull());
    }

    private static string BuildCredentialJson(string expiresAt, string subscriptionType)
        => $$"""
            {
                "claudeAiOauth": {
                    "accessToken": "access-token",
                    "refreshToken": "refresh-token",
                    "expiresAt": "{{expiresAt}}",
                    "subscriptionType": "{{subscriptionType}}"
                }
            }
            """;

    private static string BuildCredentialJsonWithEpoch(long epochMs, string subscriptionType)
        => $$"""
            {
                "claudeAiOauth": {
                    "accessToken": "access-token",
                    "refreshToken": "refresh-token",
                    "expiresAt": {{epochMs}},
                    "subscriptionType": "{{subscriptionType}}"
                }
            }
            """;
}

internal sealed class StubVolumeOperations(
    string mountpoint = "/mnt/volume",
    bool throwDockerApiException = false) : IVolumeOperations
{
    public Task<VolumeResponse> CreateAsync(
        VolumesCreateParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<VolumeResponse> InspectAsync(string name, CancellationToken cancellationToken)
    {
        if (throwDockerApiException)
        {
            throw new DockerApiException(System.Net.HttpStatusCode.NotFound, "Volume not found");
        }

        return Task.FromResult(new VolumeResponse { Mountpoint = mountpoint });
    }

    public Task<VolumesListResponse> ListAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<VolumesListResponse> ListAsync(
        VolumesListParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<VolumesPruneResponse> PruneAsync(
        VolumesPruneParameters parameters,
        CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task RemoveAsync(string name, bool? force, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
