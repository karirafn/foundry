using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.PackageSources;

namespace Foundry.UnitTests.Fakes.Monitoring;

/// <summary>Stored verdicts keyed by source URL and probe credential; anything not stored is Unchecked.</summary>
internal sealed class StubPackageSourceVerdicts : IPackageSourceVerdicts
{
    private readonly Dictionary<(string Url, ProbeCredential Credential), PackageSourceVerdict> _verdicts = [];

    public List<(Uri Url, ProbeCredential Credential)> Reads { get; } = [];

    public StubPackageSourceVerdicts With(string url, ProbeCredential credential, PackageSourceVerdict verdict)
    {
        _verdicts[(new Uri(url).AbsoluteUri, credential)] = verdict;
        return this;
    }

    public Task<PackageSourceVerdict> VerdictForAsync(
        Uri sourceUrl,
        ProbeCredential credential,
        CancellationToken cancellationToken)
    {
        Reads.Add((sourceUrl, credential));
        PackageSourceVerdict verdict = _verdicts.TryGetValue((sourceUrl.AbsoluteUri, credential), out PackageSourceVerdict? stored)
            ? stored
            : new PackageSourceVerdict.Unchecked();
        return Task.FromResult(verdict);
    }
}
