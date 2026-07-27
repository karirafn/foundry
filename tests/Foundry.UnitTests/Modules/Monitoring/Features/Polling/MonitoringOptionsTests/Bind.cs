using Foundry.Modules.Monitoring.Features.Polling;

using Microsoft.Extensions.Configuration;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Polling.MonitoringOptionsTests;

public sealed class Bind
{
    private static MonitoringOptions BuildOptions(Dictionary<string, string?> config)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        MonitoringOptions options = new();
        configuration.GetSection("Monitoring").Bind(options);
        return options;
    }

    [Fact]
    public void WhenDefaultPollIntervalSecondsConfigured_BindsCorrectly()
    {
        // Arrange
        Dictionary<string, string?> config = new()
        {
            ["Monitoring:DefaultPollIntervalSeconds"] = "60",
        };

        // Act
        MonitoringOptions options = BuildOptions(config);

        // Assert
        options.DefaultPollIntervalSeconds.ShouldBe(60);
    }

    [Fact]
    public void WhenNoDefaultPollIntervalConfigured_DefaultsToThirty()
    {
        // Arrange
        Dictionary<string, string?> config = new();

        // Act
        MonitoringOptions options = BuildOptions(config);

        // Assert
        options.DefaultPollIntervalSeconds.ShouldBe(30);
    }
}
