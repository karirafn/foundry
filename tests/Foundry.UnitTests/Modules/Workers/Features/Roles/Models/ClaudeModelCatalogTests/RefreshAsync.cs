using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Workers.Features.Roles.Models;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Roles.Models.ClaudeModelCatalogTests;

public sealed class RefreshAsync
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly ClaudeModel Opus = new("claude-opus-4-5-20251101", "Claude Opus 4.5", "active");

    private readonly StubCredentialReader _credentials = new();
    private readonly StubModelsApi _api = new();

    private ClaudeModelCatalog CreateSut()
    {
        ServiceCollection services = new();
        services.AddScoped<IClaudeApiCredentialReader>(_ => _credentials);
        services.AddSingleton<IClaudeModelsApi>(_api);
        ServiceProvider provider = services.BuildServiceProvider();

        return new ClaudeModelCatalog(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(Now),
            NullLogger<ClaudeModelCatalog>.Instance);
    }

    [Fact]
    public async Task WhenTheApiAnswers_CachesTheListWithTheSameCredentialWorkersUse()
    {
        // Arrange
        _api.Result = Result<IReadOnlyList<ClaudeModel>>.Ok([Opus]);
        ClaudeModelCatalog sut = CreateSut();

        // Act
        ClaudeModelList list = await sut.RefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        list.ShouldSatisfyAllConditions(
            () => list.Models.ShouldBe([Opus]),
            () => list.RefreshedAt.ShouldBe(Now),
            () => list.RefreshError.ShouldBeNull(),
            () => sut.Current.ShouldBe(list),
            () => _api.LastCredential.ShouldBe(_credentials.Credential));
    }

    [Fact]
    public async Task WhenARefreshFails_KeepsThePreviousListAndRecordsTheError()
    {
        // Arrange
        _api.Result = Result<IReadOnlyList<ClaudeModel>>.Ok([Opus]);
        ClaudeModelCatalog sut = CreateSut();
        await sut.RefreshAsync(TestContext.Current.CancellationToken);
        _api.Result = Result<IReadOnlyList<ClaudeModel>>.Fail(new Error(AnthropicModelsClient.FailedCode, "unreachable"));

        // Act
        ClaudeModelList list = await sut.RefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        list.ShouldSatisfyAllConditions(
            () => list.Models.ShouldBe([Opus]),
            () => list.RefreshedAt.ShouldBe(Now),
            () => list.RefreshError.ShouldBe("unreachable"));
    }

    [Fact]
    public async Task WhenTheCredentialIsMissing_RecordsTheErrorWithoutCallingTheApi()
    {
        // Arrange
        _credentials.Result = Result<ClaudeApiCredential>.Fail(new Error("Credentials.NotConfigured", "No credential."));
        ClaudeModelCatalog sut = CreateSut();

        // Act
        ClaudeModelList list = await sut.RefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        list.ShouldSatisfyAllConditions(
            () => list.Models.ShouldBeEmpty(),
            () => list.RefreshError.ShouldBe("No credential."),
            () => _api.CallCount.ShouldBe(0));
    }

    [Fact]
    public async Task GetAsync_FetchesOnceThenServesTheCache()
    {
        // Arrange
        _api.Result = Result<IReadOnlyList<ClaudeModel>>.Ok([Opus]);
        ClaudeModelCatalog sut = CreateSut();

        // Act
        await sut.GetAsync(TestContext.Current.CancellationToken);
        ClaudeModelList list = await sut.GetAsync(TestContext.Current.CancellationToken);

        // Assert
        list.Models.ShouldBe([Opus]);
        _api.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetAsync_AfterAFailedFirstFetch_DoesNotRetryOnEveryCall()
    {
        // Arrange
        _api.Result = Result<IReadOnlyList<ClaudeModel>>.Fail(new Error(AnthropicModelsClient.FailedCode, "unreachable"));
        ClaudeModelCatalog sut = CreateSut();

        // Act
        await sut.GetAsync(TestContext.Current.CancellationToken);
        ClaudeModelList list = await sut.GetAsync(TestContext.Current.CancellationToken);

        // Assert
        list.RefreshError.ShouldBe("unreachable");
        _api.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTheReaderThrows_RecordsAGenericError()
    {
        // Arrange
        _credentials.Throw = true;
        ClaudeModelCatalog sut = CreateSut();

        // Act
        ClaudeModelList list = await sut.RefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        list.RefreshError.ShouldNotBeNull();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubCredentialReader : IClaudeApiCredentialReader
    {
        public ClaudeApiCredential Credential { get; } = new ClaudeApiCredential.OAuthToken("sk-ant-oat01-x");

        public Result<ClaudeApiCredential>? Result { get; set; }

        public bool Throw { get; set; }

        public Task<Result<ClaudeApiCredential>> ReadAsync(CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("docker gone");
            }

            return Task.FromResult(Result ?? Result<ClaudeApiCredential>.Ok(Credential));
        }
    }

    private sealed class StubModelsApi : IClaudeModelsApi
    {
        public Result<IReadOnlyList<ClaudeModel>> Result { get; set; } = Result<IReadOnlyList<ClaudeModel>>.Ok([]);

        public ClaudeApiCredential? LastCredential { get; private set; }

        public int CallCount { get; private set; }

        public Task<Result<IReadOnlyList<ClaudeModel>>> ListAsync(ClaudeApiCredential credential, CancellationToken cancellationToken)
        {
            CallCount++;
            LastCredential = credential;
            return Task.FromResult(Result);
        }
    }
}
