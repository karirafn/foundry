using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.PackageSourceProxy;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSourceProxy.ProxiedPackageSourceTests;

public sealed class RewriteJson
{
    private static readonly PackageSourceId SourceId = PackageSourceId.From(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));

    private static readonly ProxiedPackageSource Source = new(
        SourceId,
        new Uri("https://gitlab.com/api/v4/groups/42/-/packages/nuget/index.json"),
        new Uri("http://host.docker.internal:5287/package-sources/"));

    [Fact]
    public void AnUpstreamUrlUnderTheDirectory_IsRepointedAtTheProxyKeepingPathAndQuery()
    {
        // Arrange
        string json = """{"@id":"https://gitlab.com/api/v4/groups/42/-/packages/nuget/query?q=a&take=1"}""";

        // Act
        string rewritten = Rewrite(json);

        // Assert
        rewritten.ShouldBe(
            """{"@id":"http://host.docker.internal:5287/package-sources/0f8fad5b-d9cb-469f-a165-70867728950e/api/v4/groups/42/-/packages/nuget/query?q=a&take=1"}""");
    }

    [Theory]
    [InlineData("https://gitlab.com/api/v4/projects/1")]
    [InlineData("http://gitlab.com/api/v4/groups/42/-/packages/nuget/query")]
    [InlineData("https://gitlab.example/api/v4/groups/42/-/packages/nuget/query")]
    [InlineData("SearchQueryService/3.5.0")]
    public void AnyOtherString_IsLeftAlone(string value)
    {
        // Arrange
        string json = $$"""{"value":"{{value}}"}""";

        // Act
        string rewritten = Rewrite(json);

        // Assert
        rewritten.ShouldBe(json);
    }

    [Fact]
    public void ABodyThatIsNotJson_IsNotRewritten()
    {
        // Act
        byte[]? rewritten = Source.RewriteJson(Encoding.UTF8.GetBytes("<html>not json</html>"));

        // Assert
        rewritten.ShouldBeNull();
    }

    [Fact]
    public void NestedArraysAndNonStringValues_KeepTheirShape()
    {
        // Arrange
        string json = """{"count":2,"ok":true,"none":null,"items":[{"@id":"https://gitlab.com/api/v4/groups/42/-/packages/nuget/a.json"},1.5]}""";

        // Act
        string rewritten = Rewrite(json);

        // Assert
        rewritten.ShouldBe(
            """{"count":2,"ok":true,"none":null,"items":[{"@id":"http://host.docker.internal:5287/package-sources/0f8fad5b-d9cb-469f-a165-70867728950e/api/v4/groups/42/-/packages/nuget/a.json"},1.5]}""");
    }

    private static string Rewrite(string json) =>
        Encoding.UTF8.GetString(Source.RewriteJson(Encoding.UTF8.GetBytes(json)).ShouldNotBeNull());
}
