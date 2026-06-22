using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitLabAccountTests;

public sealed class Update
{
    private static BaseUrl MakeBaseUrl(string url) =>
        ((Result<BaseUrl>.Success)BaseUrl.Create(url)).Value;

    [Fact]
    public void WhenTokenIsProvided_UpdatesAllProperties()
    {
        // Arrange
        GitLabAccount account = GitLabAccount.Create("original-name", "original-token", MakeBaseUrl("https://gitlab.com"));
        BaseUrl newBaseUrl = MakeBaseUrl("https://gitlab.example.com");

        // Act
        account.Update("new-name", "new-token", newBaseUrl);

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe("new-name"),
            () => account.Token.ShouldBe("new-token"),
            () => account.BaseUrl.Value.ShouldBe(new Uri("https://gitlab.example.com")));
    }

    [Fact]
    public void WhenTokenIsNull_KeepsExistingToken()
    {
        // Arrange
        GitLabAccount account = GitLabAccount.Create("original-name", "original-token", MakeBaseUrl("https://gitlab.com"));
        BaseUrl newBaseUrl = MakeBaseUrl("https://gitlab.example.com");

        // Act
        account.Update("new-name", null, newBaseUrl);

        // Assert
        account.Token.ShouldBe("original-token");
    }
}
