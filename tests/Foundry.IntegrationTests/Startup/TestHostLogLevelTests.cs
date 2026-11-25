using Foundry.IntegrationTests.Smoke;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Startup;

/// <summary>
/// Pins the test host's console floor at Warning: every host boot printed its Information startup lines,
/// which made up most of the CI test log. Other providers, such as <see cref="CapturedLogs"/>, keep Information.
/// </summary>
public sealed class TestHostLogLevelTests : IAsyncDisposable
{
    private const string FoundryCategory = "Foundry.IntegrationTests.Startup.TestHostLogLevelTests";
    private const string InformationMessage = "captured at information";

    private readonly CapturedLogs _capturedLogs = new();
    private readonly FoundryWebAppFactory _factory;

    // Creating the HttpClient triggers WebApplicationFactory to boot the host.
    private readonly HttpClient _client;

    public TestHostLogLevelTests()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services => services.AddSingleton<ILoggerProvider>(_capturedLogs));
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _capturedLogs.Dispose();
    }

    [Fact]
    public void WhenHostStarts_ConsoleProviderHasWarningFloor()
    {
        // Arrange
        IOptions<LoggerFilterOptions> filterOptions = _factory.Services.GetRequiredService<IOptions<LoggerFilterOptions>>();

        // Act
        IList<LoggerFilterRule> rules = filterOptions.Value.Rules;

        // Assert
        rules.ShouldContain(rule =>
            rule.ProviderName == typeof(ConsoleLoggerProvider).FullName
            && rule.CategoryName == null
            && rule.LogLevel == LogLevel.Warning
            && rule.Filter == null);
    }

    [Fact]
    public void WhenFoundryCategoryLogsInformation_CapturedLogsRecordsIt()
    {
        // Arrange
        ILogger logger = _factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(FoundryCategory);

        // Act
        logger.LogInformation(InformationMessage);

        // Assert
        _capturedLogs.Entries.ShouldContain(entry =>
            entry.Category == FoundryCategory
            && entry.Level == LogLevel.Information
            && entry.Message == InformationMessage);
    }
}
