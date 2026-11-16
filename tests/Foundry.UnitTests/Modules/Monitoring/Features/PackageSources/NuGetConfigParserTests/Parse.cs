using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.PackageSources;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSources.NuGetConfigParserTests;

public sealed class Parse
{
    private const string BlobSha = "3f786850e387550fdab836ed7e6dc881de23001b";

    private const string TwoSourceConfig = """
        <configuration>
          <packageSources>
            <clear />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
            <add key="gitlab" value="https://gitlab.com/api/v4/groups/123/-/packages/nuget/index.json" />
          </packageSources>
          <packageSourceMapping>
            <packageSource key="nuget.org"><package pattern="*" /></packageSource>
            <packageSource key="gitlab"><package pattern="Contoso*" /></packageSource>
          </packageSourceMapping>
          <packageSourceCredentials>
            <gitlab>
              <add key="Username" value="%PACKAGE_REGISTRY_USERNAME%" />
              <add key="ClearTextPassword" value="%PACKAGE_REGISTRY_PASSWORD%" />
            </gitlab>
          </packageSourceCredentials>
        </configuration>
        """;

    [Fact]
    public void WhenConfigDeclaresNuGetOrgAndAPrivateSource_DeclaresOnlyThePrivateSource()
    {
        // Act
        RootNuGetConfig config = NuGetConfigParser.Parse(BlobSha, TwoSourceConfig);

        // Assert
        RootNuGetConfig.Declared declared = config.ShouldBeOfType<RootNuGetConfig.Declared>();
        DeclaredPackageSource source = declared.Sources.ShouldHaveSingleItem();
        source.Key.ShouldBe("gitlab");
        source.ServiceIndexUrl.ShouldBe(new Uri("https://gitlab.com/api/v4/groups/123/-/packages/nuget/index.json"));
    }

    [Fact]
    public void WhenConfigParses_KeepsTheBlobShaAndRawText()
    {
        // Act
        RootNuGetConfig config = NuGetConfigParser.Parse(BlobSha, TwoSourceConfig);

        // Assert
        RootNuGetConfig.Declared declared = config.ShouldBeOfType<RootNuGetConfig.Declared>();
        declared.BlobSha.ShouldBe(BlobSha);
        declared.RawText.ShouldBe(TwoSourceConfig);
    }

    [Fact]
    public void WhenConfigDeclaresOnlyNuGetOrg_DeclaresNoSources()
    {
        // Arrange
        const string text = """
            <configuration>
              <packageSources>
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
              </packageSources>
            </configuration>
            """;

        // Act
        RootNuGetConfig config = NuGetConfigParser.Parse(BlobSha, text);

        // Assert
        config.ShouldBeOfType<RootNuGetConfig.Declared>().Sources.ShouldBeEmpty();
    }

    [Fact]
    public void WhenASourceIsDisabled_LeavesItOut()
    {
        // Arrange
        const string text = """
            <configuration>
              <packageSources>
                <add key="internal" value="https://pkgs.example.com/v3/index.json" />
                <add key="Other" value="https://other.example.com/v3/index.json" />
              </packageSources>
              <disabledPackageSources>
                <add key="INTERNAL" value="true" />
                <add key="Other" value="false" />
              </disabledPackageSources>
            </configuration>
            """;

        // Act
        RootNuGetConfig config = NuGetConfigParser.Parse(BlobSha, text);

        // Assert
        DeclaredPackageSource source = config.ShouldBeOfType<RootNuGetConfig.Declared>().Sources.ShouldHaveSingleItem();
        source.Key.ShouldBe("Other");
    }

    [Theory]
    [InlineData("./packages")]
    [InlineData("C:\\packages")]
    [InlineData("/var/packages")]
    [InlineData("%FEED_URL%")]
    [InlineData("file:///var/packages")]
    public void WhenASourceIsNotARemoteUrl_LeavesItOut(string value)
    {
        // Arrange
        string text = $"""
            <configuration>
              <packageSources>
                <add key="local" value="{value}" />
              </packageSources>
            </configuration>
            """;

        // Act
        RootNuGetConfig config = NuGetConfigParser.Parse(BlobSha, text);

        // Assert
        config.ShouldBeOfType<RootNuGetConfig.Declared>().Sources.ShouldBeEmpty();
    }

    [Fact]
    public void WhenNuGetOrgHostDiffersOnlyInCase_LeavesItOut()
    {
        // Arrange
        const string text = """
            <configuration>
              <packageSources>
                <add key="nuget" value="https://API.NuGet.org/v3/index.json" />
              </packageSources>
            </configuration>
            """;

        // Act
        RootNuGetConfig config = NuGetConfigParser.Parse(BlobSha, text);

        // Assert
        config.ShouldBeOfType<RootNuGetConfig.Declared>().Sources.ShouldBeEmpty();
    }

    [Fact]
    public void WhenClearFollowsASource_DropsTheSourcesBeforeIt()
    {
        // Arrange
        const string text = """
            <configuration>
              <packageSources>
                <add key="before" value="https://before.example.com/v3/index.json" />
                <clear />
                <add key="after" value="https://after.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """;

        // Act
        RootNuGetConfig config = NuGetConfigParser.Parse(BlobSha, text);

        // Assert
        config.ShouldBeOfType<RootNuGetConfig.Declared>().Sources.ShouldHaveSingleItem().Key.ShouldBe("after");
    }

    [Fact]
    public void WhenConfigHasNoPackageSourcesSection_DeclaresNoSources()
    {
        // Act
        RootNuGetConfig config = NuGetConfigParser.Parse(BlobSha, "<configuration />");

        // Assert
        config.ShouldBeOfType<RootNuGetConfig.Declared>().Sources.ShouldBeEmpty();
    }

    [Fact]
    public void WhenXmlIsMalformed_RecordsTheConfigAsInvalidWithTheParserMessage()
    {
        // Arrange
        const string text = "<configuration><packageSources></configuration>";

        // Act
        RootNuGetConfig config = NuGetConfigParser.Parse(BlobSha, text);

        // Assert
        RootNuGetConfig.Invalid invalid = config.ShouldBeOfType<RootNuGetConfig.Invalid>();
        invalid.Message.ShouldNotBeNullOrWhiteSpace();
        invalid.BlobSha.ShouldBe(BlobSha);
        invalid.RawText.ShouldBe(text);
    }

    [Fact]
    public void WhenXmlDeclaresADocumentType_RecordsTheConfigAsInvalid()
    {
        // Arrange
        const string text = """
            <?xml version="1.0"?>
            <!DOCTYPE configuration [<!ENTITY x "https://evil.example.com/v3/index.json">]>
            <configuration><packageSources><add key="x" value="&x;" /></packageSources></configuration>
            """;

        // Act
        RootNuGetConfig config = NuGetConfigParser.Parse(BlobSha, text);

        // Assert
        config.ShouldBeOfType<RootNuGetConfig.Invalid>();
    }
}
