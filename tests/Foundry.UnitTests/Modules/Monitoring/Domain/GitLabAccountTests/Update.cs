using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.GitLabAccountTests;

public sealed class Update
{
    [Fact]
    public void WhenTokenIsProvided_UpdatesAllProperties()
    {
        // Arrange
        GitLabAccount account = GitLabAccount.Create("original-name", "original-token", BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        BaseUrl newBaseUrl = BaseUrl.Create("https://gitlab.example.com").ValueOrThrow();

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
        GitLabAccount account = GitLabAccount.Create("original-name", "original-token", BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        BaseUrl newBaseUrl = BaseUrl.Create("https://gitlab.example.com").ValueOrThrow();

        // Act
        account.Update("new-name", null, newBaseUrl);

        // Assert
        account.Token.ShouldBe("original-token");
    }
}
