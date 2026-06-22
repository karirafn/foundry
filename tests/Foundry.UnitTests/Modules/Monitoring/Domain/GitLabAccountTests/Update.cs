using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitLabAccountTests;

public sealed class Update
{
    [Fact]
    public void WhenTokenIsProvided_UpdatesAllProperties()
    {
        // Arrange
        GitLabAccount account = GitLabAccount.Create("original-name", "original-token", BaseUrlFactory.Create("https://gitlab.com"));
        BaseUrl newBaseUrl = BaseUrlFactory.Create("https://gitlab.example.com");

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
        GitLabAccount account = GitLabAccount.Create("original-name", "original-token", BaseUrlFactory.Create("https://gitlab.com"));
        BaseUrl newBaseUrl = BaseUrlFactory.Create("https://gitlab.example.com");

        // Act
        account.Update("new-name", null, newBaseUrl);

        // Assert
        account.Token.ShouldBe("original-token");
    }
}
