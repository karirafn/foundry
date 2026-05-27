using Foundry.WebApi.Modules.Monitoring.Features;

using Microsoft.Extensions.Configuration;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Features.MonitoringOptionsTests;

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

    [Fact]
    public void WhenAccountsConfigured_BindsAccountList()
    {
        // Arrange
        Dictionary<string, string?> config = new()
        {
            ["Monitoring:Accounts:0:Name"] = "my-org",
            ["Monitoring:Accounts:0:Type"] = "GitHub",
            ["Monitoring:Accounts:0:BaseUrl"] = "https://api.github.com",
            ["Monitoring:Accounts:0:SecretKeyName"] = "GITHUB_TOKEN",
        };

        // Act
        MonitoringOptions options = BuildOptions(config);

        // Assert
        options.Accounts.Count.ShouldBe(1);
        AccountOption account = options.Accounts[0];
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe("my-org"),
            () => account.Type.ShouldBe("GitHub"),
            () => account.BaseUrl.ShouldBe("https://api.github.com"),
            () => account.SecretKeyName.ShouldBe("GITHUB_TOKEN"));
    }

    [Fact]
    public void WhenRepositoriesConfigured_BindsRepositoryList()
    {
        // Arrange
        Dictionary<string, string?> config = new()
        {
            ["Monitoring:Repositories:0:Slug"] = "owner/repo",
            ["Monitoring:Repositories:0:AccountName"] = "my-org",
            ["Monitoring:Repositories:0:PollIntervalSeconds"] = "120",
            ["Monitoring:Repositories:0:IsActive"] = "true",
        };

        // Act
        MonitoringOptions options = BuildOptions(config);

        // Assert
        options.Repositories.Count.ShouldBe(1);
        RepositoryOption repo = options.Repositories[0];
        repo.ShouldSatisfyAllConditions(
            () => repo.Slug.ShouldBe("owner/repo"),
            () => repo.AccountName.ShouldBe("my-org"),
            () => repo.PollIntervalSeconds.ShouldBe(120),
            () => repo.IsActive.ShouldBeTrue());
    }

    [Fact]
    public void WhenRepositoryIsActiveNotConfigured_DefaultsToTrue()
    {
        // Arrange
        Dictionary<string, string?> config = new()
        {
            ["Monitoring:Repositories:0:Slug"] = "owner/repo",
            ["Monitoring:Repositories:0:AccountName"] = "my-org",
        };

        // Act
        MonitoringOptions options = BuildOptions(config);

        // Assert
        options.Repositories[0].IsActive.ShouldBeTrue();
    }

    [Fact]
    public void WhenRepositoryPollIntervalNotConfigured_IsNull()
    {
        // Arrange
        Dictionary<string, string?> config = new()
        {
            ["Monitoring:Repositories:0:Slug"] = "owner/repo",
            ["Monitoring:Repositories:0:AccountName"] = "my-org",
        };

        // Act
        MonitoringOptions options = BuildOptions(config);

        // Assert
        options.Repositories[0].PollIntervalSeconds.ShouldBeNull();
    }
}
