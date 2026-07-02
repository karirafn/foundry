using System.Net;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.AnthropicAuthValidatorTests;

public sealed class ValidateAsync
{
    private static AnthropicAuthValidator BuildSut(
        FakeAnthropicApiHandler? handler = null,
        FakeGlobalSettingsQueries? queries = null)
    {
        FakeAnthropicApiHandler apiHandler = handler ?? new FakeAnthropicApiHandler(HttpStatusCode.OK);
        HttpClient httpClient = new(apiHandler)
        {
            BaseAddress = new Uri("https://api.anthropic.com"),
        };
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        FakeGlobalSettingsQueries settingsQueries = queries ?? new FakeGlobalSettingsQueries();

        return new AnthropicAuthValidator(httpClient, settingsQueries);
    }

    [Fact]
    public async Task WhenNoAuthConfigured_ReturnsInvalid()
    {
        // Arrange
        FakeGlobalSettingsQueries queries = new() { Settings = null };
        AnthropicAuthValidator sut = BuildSut(queries: queries);

        // Act
        AuthValidationResult result = await sut.ValidateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeFalse(),
            () => result.ErrorMessage.ShouldBe("Configure Claude authentication in Settings"));
    }

    [Fact]
    public async Task WhenApiKeyIsEmpty_ReturnsInvalid()
    {
        // Arrange
        FakeGlobalSettingsQueries queries = new()
        {
            Settings = new GlobalSettingsSummary("ApiKey", 1, 120, false, false, null, null, null, null, null, false, true, 60, false, false, false, false, false, false, ImageBuildStatus.Idle, null, false),
            AuthEnvVar = ("ANTHROPIC_API_KEY", string.Empty),
        };
        AnthropicAuthValidator sut = BuildSut(queries: queries);

        // Act
        AuthValidationResult result = await sut.ValidateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeFalse(),
            () => result.ErrorMessage.ShouldBe("Configure Claude authentication in Settings"));
    }

    [Fact]
    public async Task WhenOAuthConfigured_ReturnsValid()
    {
        // Arrange — OAuth validity is determined by the shared credential volume, not stored token fields.
        // ExpiresAt and token presence are no longer stored (step 2 change). Step 6 will introduce
        // volume-derived credential status.
        FakeGlobalSettingsQueries queries = new()
        {
            Settings = new GlobalSettingsSummary("OAuth", 1, 120, false, false, null, "pro", null, null, null, false, true, 60, false, false, false, false, false, false, ImageBuildStatus.Idle, null, false),
        };
        AnthropicAuthValidator sut = BuildSut(queries: queries);

        // Act
        AuthValidationResult result = await sut.ValidateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeTrue(),
            () => result.PassedOptimistically.ShouldBeFalse(),
            () => result.ErrorMessage.ShouldBeNull());
    }

    [Fact]
    public async Task WhenApiKeyReturns200_ReturnsValid()
    {
        // Arrange
        FakeGlobalSettingsQueries queries = new()
        {
            Settings = new GlobalSettingsSummary("ApiKey", 1, 120, false, false, null, null, null, null, null, false, true, 60, false, false, false, false, false, false, ImageBuildStatus.Idle, null, false),
            AuthEnvVar = ("ANTHROPIC_API_KEY", "sk-valid-key"),
        };
        FakeAnthropicApiHandler handler = new(HttpStatusCode.OK);
        AnthropicAuthValidator sut = BuildSut(handler: handler, queries: queries);

        // Act
        AuthValidationResult result = await sut.ValidateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeTrue(),
            () => result.PassedOptimistically.ShouldBeFalse(),
            () => result.ErrorMessage.ShouldBeNull());
    }

    [Fact]
    public async Task WhenApiKeyReturns401_ReturnsInvalid()
    {
        // Arrange
        FakeGlobalSettingsQueries queries = new()
        {
            Settings = new GlobalSettingsSummary("ApiKey", 1, 120, false, false, null, null, null, null, null, false, true, 60, false, false, false, false, false, false, ImageBuildStatus.Idle, null, false),
            AuthEnvVar = ("ANTHROPIC_API_KEY", "sk-bad-key"),
        };
        FakeAnthropicApiHandler handler = new(HttpStatusCode.Unauthorized);
        AnthropicAuthValidator sut = BuildSut(handler: handler, queries: queries);

        // Act
        AuthValidationResult result = await sut.ValidateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeFalse(),
            () => result.ErrorMessage.ShouldBe("API key is invalid — check Settings"));
    }

    [Fact]
    public async Task WhenApiKeyReturns403_ReturnsInvalid()
    {
        // Arrange
        FakeGlobalSettingsQueries queries = new()
        {
            Settings = new GlobalSettingsSummary("ApiKey", 1, 120, false, false, null, null, null, null, null, false, true, 60, false, false, false, false, false, false, ImageBuildStatus.Idle, null, false),
            AuthEnvVar = ("ANTHROPIC_API_KEY", "sk-forbidden-key"),
        };
        FakeAnthropicApiHandler handler = new(HttpStatusCode.Forbidden);
        AnthropicAuthValidator sut = BuildSut(handler: handler, queries: queries);

        // Act
        AuthValidationResult result = await sut.ValidateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeFalse(),
            () => result.ErrorMessage.ShouldBe("API key is invalid — check Settings"));
    }

    [Fact]
    public async Task WhenApiKeyReturns500_ReturnsValidOptimistic()
    {
        // Arrange
        FakeGlobalSettingsQueries queries = new()
        {
            Settings = new GlobalSettingsSummary("ApiKey", 1, 120, false, false, null, null, null, null, null, false, true, 60, false, false, false, false, false, false, ImageBuildStatus.Idle, null, false),
            AuthEnvVar = ("ANTHROPIC_API_KEY", "sk-any-key"),
        };
        FakeAnthropicApiHandler handler = new(HttpStatusCode.InternalServerError);
        AnthropicAuthValidator sut = BuildSut(handler: handler, queries: queries);

        // Act
        AuthValidationResult result = await sut.ValidateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeTrue(),
            () => result.PassedOptimistically.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenApiKeyReturns429_ReturnsValidOptimistic()
    {
        // Arrange
        FakeGlobalSettingsQueries queries = new()
        {
            Settings = new GlobalSettingsSummary("ApiKey", 1, 120, false, false, null, null, null, null, null, false, true, 60, false, false, false, false, false, false, ImageBuildStatus.Idle, null, false),
            AuthEnvVar = ("ANTHROPIC_API_KEY", "sk-any-key"),
        };
        FakeAnthropicApiHandler handler = new(HttpStatusCode.TooManyRequests);
        AnthropicAuthValidator sut = BuildSut(handler: handler, queries: queries);

        // Act
        AuthValidationResult result = await sut.ValidateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeTrue(),
            () => result.PassedOptimistically.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenApiKeyNetworkError_ReturnsValidOptimistic()
    {
        // Arrange
        FakeGlobalSettingsQueries queries = new()
        {
            Settings = new GlobalSettingsSummary("ApiKey", 1, 120, false, false, null, null, null, null, null, false, true, 60, false, false, false, false, false, false, ImageBuildStatus.Idle, null, false),
            AuthEnvVar = ("ANTHROPIC_API_KEY", "sk-any-key"),
        };
        FakeAnthropicApiHandler handler = new(throwNetworkError: true);
        AnthropicAuthValidator sut = BuildSut(handler: handler, queries: queries);

        // Act
        AuthValidationResult result = await sut.ValidateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeTrue(),
            () => result.PassedOptimistically.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenApiKeyRequestTimesOut_ReturnsValidOptimistic()
    {
        // Arrange
        FakeGlobalSettingsQueries queries = new()
        {
            Settings = new GlobalSettingsSummary("ApiKey", 1, 120, false, false, null, null, null, null, null, false, true, 60, false, false, false, false, false, false, ImageBuildStatus.Idle, null, false),
            AuthEnvVar = ("ANTHROPIC_API_KEY", "sk-any-key"),
        };
        FakeAnthropicApiHandler handler = new(throwTimeoutError: true);
        AnthropicAuthValidator sut = BuildSut(handler: handler, queries: queries);

        // Act
        AuthValidationResult result = await sut.ValidateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeTrue(),
            () => result.PassedOptimistically.ShouldBeTrue());
    }
}

internal sealed class FakeAnthropicApiHandler : DelegatingHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly bool _throwNetworkError;
    private readonly bool _throwTimeoutError;

    public FakeAnthropicApiHandler(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        bool throwNetworkError = false,
        bool throwTimeoutError = false)
    {
        _statusCode = statusCode;
        _throwNetworkError = throwNetworkError;
        _throwTimeoutError = throwTimeoutError;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_throwNetworkError)
        {
            throw new HttpRequestException("Simulated network error");
        }

        if (_throwTimeoutError)
        {
            throw new TaskCanceledException("timeout", null, new CancellationTokenSource().Token);
        }

        return Task.FromResult(new HttpResponseMessage(_statusCode));
    }
}

internal sealed class FakeGlobalSettingsQueries : IGlobalSettingsQueries
{
    public GlobalSettingsSummary? Settings { get; init; }
    public (string Key, string Value)? AuthEnvVar { get; init; }
    public int MaxConcurrent { get; init; } = 1;
    public int TimeoutMinutes { get; init; } = 120;

    public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Settings);

    public Task<(string Key, string Value)?> GetAuthEnvironmentVariableAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(AuthEnvVar);

    public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken) =>
        Task.FromResult(MaxConcurrent);

    public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(TimeoutMinutes);

    public Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<(string?, string?)>((null, null));

    public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new DispatchPauseState(null, false, true));

    public Task<int> GetDefaultCooldownMinutesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(60);

    public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ImageBuildStatus.Idle);

    public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
