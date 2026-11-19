using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Startup;

/// <summary>
/// Pins the shipped log levels of EF Core's SQL command and migration categories: their Information
/// entries made up nearly all of the host's log volume, while failures are logged at Error.
/// </summary>
public sealed class EfCoreLogLevelTests : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    // Creating the HttpClient triggers WebApplicationFactory to boot the host.
    private readonly HttpClient _client;

    public EfCoreLogLevelTests()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command")]
    [InlineData("Microsoft.EntityFrameworkCore.Migrations")]
    public void WhenHostStarts_EfCoreCategoryDoesNotLogInformation(string category)
    {
        // Arrange
        ILoggerFactory loggerFactory = _factory.Services.GetRequiredService<ILoggerFactory>();

        // Act
        ILogger logger = loggerFactory.CreateLogger(category);

        // Assert
        logger.IsEnabled(LogLevel.Information).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command")]
    [InlineData("Microsoft.EntityFrameworkCore.Migrations")]
    public void WhenHostStarts_EfCoreCategoryLogsWarning(string category)
    {
        // Arrange
        ILoggerFactory loggerFactory = _factory.Services.GetRequiredService<ILoggerFactory>();

        // Act
        ILogger logger = loggerFactory.CreateLogger(category);

        // Assert
        logger.IsEnabled(LogLevel.Warning).ShouldBeTrue();
    }
}
