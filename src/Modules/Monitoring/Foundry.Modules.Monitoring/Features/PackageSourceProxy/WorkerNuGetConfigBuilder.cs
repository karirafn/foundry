using System.Xml;
using System.Xml.Linq;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.PackageSourceProxy;

/// <summary>
/// Builds the <c>nuget.config</c> a worker restores with (ADR 0112): the repository's root config with every proxied
/// Package Source re-pointed at the Package Source Proxy, and its credentials replaced by the run's Run Source Token.
/// Everything else in the config is copied as it is.
/// </summary>
internal static class WorkerNuGetConfigBuilder
{
    /// <summary>The Basic username a worker presents; the proxy ignores it, the token is the password.</summary>
    internal const string Username = "foundry-run";

    internal const string InvalidConfigCode = "PackageSource.WorkerConfigInvalid";

    private static readonly XmlWriterSettings WriterSettings = new()
    {
        // Without a declaration the file reads as UTF-8, whatever encoding the writer itself uses.
        OmitXmlDeclaration = true,
        Indent = true,
    };

    /// <summary>A Package Source the repository declares, with the id the proxy serves it under.</summary>
    internal sealed record ProxiedSource(DeclaredPackageSource Declared, PackageSourceId SourceId);

    /// <param name="rootConfigText">The repository's stored root <c>nuget.config</c> text.</param>
    /// <param name="sources">The declared Package Sources to proxy.</param>
    /// <param name="proxyBaseUrl">The proxy's base URL as a worker sees it, ending in <c>/package-sources/</c>.</param>
    /// <param name="runSourceToken">The run's Run Source Token.</param>
    public static Result<string> Build(
        string rootConfigText,
        IReadOnlyList<ProxiedSource> sources,
        Uri proxyBaseUrl,
        string runSourceToken)
    {
        ArgumentNullException.ThrowIfNull(rootConfigText);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(proxyBaseUrl);
        ArgumentException.ThrowIfNullOrEmpty(runSourceToken);

        XDocument document;
        try
        {
            document = NuGetConfigParser.Load(rootConfigText);
        }
        catch (XmlException ex)
        {
            return Result<string>.Fail(new Error(InvalidConfigCode, $"The root nuget.config is not well-formed XML: {ex.Message}"));
        }

        if (document.Root is not { } configuration)
        {
            return Result<string>.Fail(new Error(InvalidConfigCode, "The root nuget.config has no root element."));
        }

        Dictionary<string, ProxiedSource> byKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProxiedSource source in sources)
        {
            byKey[source.Declared.Key] = source;
        }

        foreach (XElement add in configuration.Elements("packageSources").Elements("add"))
        {
            if ((string?)add.Attribute("key") is not { } key || !byKey.TryGetValue(key, out ProxiedSource? source))
            {
                continue;
            }

            ProxiedPackageSource proxied = new(source.SourceId, source.Declared.ServiceIndexUrl, proxyBaseUrl);
            add.SetAttributeValue("value", proxied.ServiceIndexProxyUrl);
            // The proxy listens on plain HTTP on the Docker host; NuGet refuses such a source without this.
            add.SetAttributeValue("allowInsecureConnections", "true");
        }

        ReplaceCredentials(configuration, byKey.Values, runSourceToken);

        using StringWriter output = new();
        using (XmlWriter writer = XmlWriter.Create(output, WriterSettings))
        {
            document.Save(writer);
        }

        return Result<string>.Ok(output.ToString());
    }

    private static void ReplaceCredentials(XElement configuration, IEnumerable<ProxiedSource> sources, string token)
    {
        XElement credentials = new(
            "packageSourceCredentials",
            sources.Select(source => new XElement(
                // NuGet stores a source key that is not a valid element name (a space, say) encoded.
                XmlConvert.EncodeLocalName(source.Declared.Key),
                new XElement("add", new XAttribute("key", "Username"), new XAttribute("value", Username)),
                new XElement("add", new XAttribute("key", "ClearTextPassword"), new XAttribute("value", token)))));

        List<XElement> existing = configuration.Elements("packageSourceCredentials").ToList();
        if (existing.Count == 0)
        {
            configuration.Add(credentials);
            return;
        }

        existing[0].ReplaceWith(credentials);
        foreach (XElement extra in existing.Skip(1))
        {
            extra.Remove();
        }
    }
}
