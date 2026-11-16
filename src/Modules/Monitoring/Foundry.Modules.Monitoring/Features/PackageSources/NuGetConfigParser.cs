using System.Xml;
using System.Xml.Linq;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// Reads a repository's declared Package Sources out of its root <c>nuget.config</c>: every
/// <c>&lt;add key value&gt;</c> under <c>&lt;packageSources&gt;</c> that is a remote source other than nuget.org,
/// minus the keys <c>&lt;disabledPackageSources&gt;</c> disables.
/// </summary>
internal static class NuGetConfigParser
{
    private const char ByteOrderMark = '\uFEFF';

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        // A repository controls this text, so no DTD or external entity is ever resolved.
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    public static RootNuGetConfig Parse(string blobSha, string text)
    {
        XDocument document;
        try
        {
            using StringReader stringReader = new(text.TrimStart(ByteOrderMark));
            using XmlReader xmlReader = XmlReader.Create(stringReader, ReaderSettings);
            document = XDocument.Load(xmlReader);
        }
        catch (XmlException ex)
        {
            return new RootNuGetConfig.Invalid(blobSha, text, ex.Message);
        }

        XElement? configuration = document.Root;
        if (configuration is null)
        {
            return new RootNuGetConfig.Declared(blobSha, text, []);
        }

        HashSet<string> disabledKeys = DisabledKeys(configuration);
        List<DeclaredPackageSource> sources = DeclaredSources(configuration)
            .Where(source => !disabledKeys.Contains(source.Key))
            .ToList();

        return new RootNuGetConfig.Declared(blobSha, text, sources);
    }

    private static IEnumerable<DeclaredPackageSource> DeclaredSources(XElement configuration)
    {
        // NuGet reads source keys case-insensitively; a later entry with the same key replaces an earlier one.
        Dictionary<string, DeclaredPackageSource?> byKey = new(StringComparer.OrdinalIgnoreCase);
        List<string> order = [];

        foreach (XElement element in configuration.Elements("packageSources").Elements())
        {
            if (element.Name.LocalName == "clear")
            {
                byKey.Clear();
                order.Clear();
                continue;
            }

            if (element.Name.LocalName != "add")
            {
                continue;
            }

            string? key = (string?)element.Attribute("key");
            if (key is null)
            {
                continue;
            }

            if (!byKey.ContainsKey(key))
            {
                order.Add(key);
            }

            byKey[key] = DeclaredPackageSource.Create(key, (string?)element.Attribute("value")) is
                Result<DeclaredPackageSource>.Success success
                ? success.Value
                : null;
        }

        foreach (string key in order)
        {
            if (byKey[key] is { } source)
            {
                yield return source;
            }
        }
    }

    private static HashSet<string> DisabledKeys(XElement configuration)
    {
        HashSet<string> disabled = new(StringComparer.OrdinalIgnoreCase);

        foreach (XElement add in configuration.Elements("disabledPackageSources").Elements("add"))
        {
            string? key = (string?)add.Attribute("key");
            string? value = (string?)add.Attribute("value");

            if (key is not null && string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                disabled.Add(key);
            }
        }

        return disabled;
    }
}
