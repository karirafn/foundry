using System.Xml.Linq;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.PackageSourceProxy;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSourceProxy.WorkerNuGetConfigBuilderTests;

public sealed class Build
{
    private const string Token = "run-source-token";

    private static readonly Uri ProxyBaseUrl = new("http://host.docker.internal:5287/package-sources/");

    private static readonly PackageSourceId GitLabId = PackageSourceId.From(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));

    private const string GitLabUrl = "https://gitlab.com/api/v4/groups/123/-/packages/nuget/index.json";

    private const string GitLabProxyUrl =
        "http://host.docker.internal:5287/package-sources/0f8fad5b-d9cb-469f-a165-70867728950e/api/v4/groups/123/-/packages/nuget/index.json";

    private const string TwoSourceConfig = """
        <?xml version="1.0" encoding="utf-8"?>
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
    public void APrivateSource_IsRepointedAtTheProxyAndAllowsPlainHttp()
    {
        // Act
        XElement configuration = BuildConfig(TwoSourceConfig, Proxied("gitlab", GitLabUrl, GitLabId));

        // Assert
        XElement gitlab = SourceAdd(configuration, "gitlab");
        ((string?)gitlab.Attribute("value")).ShouldBe(GitLabProxyUrl);
        ((string?)gitlab.Attribute("allowInsecureConnections")).ShouldBe("true");
    }

    [Fact]
    public void NuGetOrg_IsCopiedUnchanged()
    {
        // Act
        XElement configuration = BuildConfig(TwoSourceConfig, Proxied("gitlab", GitLabUrl, GitLabId));

        // Assert
        XElement nugetOrg = SourceAdd(configuration, "nuget.org");
        ((string?)nugetOrg.Attribute("value")).ShouldBe("https://api.nuget.org/v3/index.json");
        nugetOrg.Attribute("allowInsecureConnections").ShouldBeNull();
    }

    [Fact]
    public void TheClearAndThePackageSourceMapping_AreCopiedUnchanged()
    {
        // Act
        XElement configuration = BuildConfig(TwoSourceConfig, Proxied("gitlab", GitLabUrl, GitLabId));

        // Assert
        configuration.Element("packageSources")!.Elements().First().Name.LocalName.ShouldBe("clear");
        XElement original = XDocument.Parse(TwoSourceConfig).Root!.Element("packageSourceMapping")!;
        XNode.DeepEquals(configuration.Element("packageSourceMapping"), original).ShouldBeTrue();
    }

    [Fact]
    public void PlaceholderCredentials_AreReplacedByTheRunToken()
    {
        // Act
        XElement configuration = BuildConfig(TwoSourceConfig, Proxied("gitlab", GitLabUrl, GitLabId));

        // Assert
        XElement credentials = configuration.Elements("packageSourceCredentials").ShouldHaveSingleItem();
        XElement gitlab = credentials.Elements().ShouldHaveSingleItem();
        gitlab.Name.LocalName.ShouldBe("gitlab");
        CredentialValue(gitlab, "Username").ShouldBe("foundry-run");
        CredentialValue(gitlab, "ClearTextPassword").ShouldBe(Token);
        gitlab.Elements().Count().ShouldBe(2);
    }

    [Fact]
    public void ADisabledSource_IsLeftAsItWasAndGetsNoCredentials()
    {
        // Arrange
        const string config = """
            <configuration>
              <packageSources>
                <add key="gitlab" value="https://gitlab.com/api/v4/groups/123/-/packages/nuget/index.json" />
                <add key="old" value="https://old.example/nuget/index.json" />
              </packageSources>
              <disabledPackageSources>
                <add key="old" value="true" />
              </disabledPackageSources>
              <packageSourceCredentials>
                <old>
                  <add key="Username" value="someone" />
                  <add key="ClearTextPassword" value="%OLD_PASSWORD%" />
                </old>
              </packageSourceCredentials>
            </configuration>
            """;

        // Act
        XElement configuration = BuildConfig(config, Proxied("gitlab", GitLabUrl, GitLabId));

        // Assert
        XElement old = SourceAdd(configuration, "old");
        ((string?)old.Attribute("value")).ShouldBe("https://old.example/nuget/index.json");
        old.Attribute("allowInsecureConnections").ShouldBeNull();
        XElement original = XDocument.Parse(config).Root!.Element("disabledPackageSources")!;
        XNode.DeepEquals(configuration.Element("disabledPackageSources"), original).ShouldBeTrue();
        configuration.Element("packageSourceCredentials")!.Elements().Select(e => e.Name.LocalName).ShouldBe(["gitlab"]);
    }

    [Fact]
    public void ExtraSections_AreCopiedUnchanged()
    {
        // Arrange
        const string config = """
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="packages" />
              </config>
              <packageSources>
                <add key="gitlab" value="https://gitlab.com/api/v4/groups/123/-/packages/nuget/index.json" />
              </packageSources>
              <packageRestore>
                <add key="enabled" value="True" />
              </packageRestore>
              <trustedSigners>
                <author name="contoso"><certificate fingerprint="ABC" hashAlgorithm="SHA256" allowUntrustedRoot="false" /></author>
              </trustedSigners>
            </configuration>
            """;
        XElement original = XDocument.Parse(config).Root!;

        // Act
        XElement configuration = BuildConfig(config, Proxied("gitlab", GitLabUrl, GitLabId));

        // Assert
        foreach (string section in new[] { "config", "packageRestore", "trustedSigners" })
        {
            XNode.DeepEquals(configuration.Element(section), original.Element(section)).ShouldBeTrue(section);
        }
    }

    [Fact]
    public void AConfigWithoutCredentials_GainsOneEntryPerProxiedSource()
    {
        // Arrange
        const string config = """
            <configuration>
              <packageSources>
                <add key="gitlab" value="https://gitlab.com/api/v4/groups/123/-/packages/nuget/index.json" />
                <add key="My Feed" value="https://feeds.example/v3/index.json" />
              </packageSources>
            </configuration>
            """;
        PackageSourceId feedId = PackageSourceId.From(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"));

        // Act
        XElement configuration = BuildConfig(
            config,
            Proxied("gitlab", GitLabUrl, GitLabId),
            Proxied("My Feed", "https://feeds.example/v3/index.json", feedId));

        // Assert
        configuration.Element("packageSourceCredentials")!.Elements().Select(e => e.Name.LocalName)
            .ShouldBe(["gitlab", "My_x0020_Feed"]);
        ((string?)SourceAdd(configuration, "My Feed").Attribute("value"))
            .ShouldBe("http://host.docker.internal:5287/package-sources/7c9e6679-7425-40de-944b-e07fc1f90ae7/v3/index.json");
    }

    [Fact]
    public void ASourceKeyInADifferentCase_IsStillRepointed()
    {
        // Act
        XElement configuration = BuildConfig(TwoSourceConfig, Proxied("GitLab", GitLabUrl, GitLabId));

        // Assert
        ((string?)SourceAdd(configuration, "gitlab").Attribute("value")).ShouldBe(GitLabProxyUrl);
    }

    [Fact]
    public void TextThatIsNotXml_Fails()
    {
        // Act
        Result<string> result = WorkerNuGetConfigBuilder.Build(
            "<configuration>",
            [Proxied("gitlab", GitLabUrl, GitLabId)],
            ProxyBaseUrl,
            Token);

        // Assert
        result.ShouldBeOfType<Result<string>.Failure>();
    }

    private static XElement BuildConfig(string config, params WorkerNuGetConfigBuilder.ProxiedSource[] sources)
    {
        Result<string> result = WorkerNuGetConfigBuilder.Build(config, sources, ProxyBaseUrl, Token);
        string xml = result.ShouldBeOfType<Result<string>.Success>().Value;
        return XDocument.Parse(xml).Root!;
    }

    private static WorkerNuGetConfigBuilder.ProxiedSource Proxied(string key, string url, PackageSourceId id)
    {
        DeclaredPackageSource declared = DeclaredPackageSource.Create(key, url)
            .ShouldBeOfType<Result<DeclaredPackageSource>.Success>().Value;
        return new WorkerNuGetConfigBuilder.ProxiedSource(declared, id);
    }

    private static XElement SourceAdd(XElement configuration, string key) =>
        configuration.Element("packageSources")!.Elements("add").Single(add => (string?)add.Attribute("key") == key);

    private static string? CredentialValue(XElement source, string key) =>
        (string?)source.Elements("add").Single(add => (string?)add.Attribute("key") == key).Attribute("value");
}
