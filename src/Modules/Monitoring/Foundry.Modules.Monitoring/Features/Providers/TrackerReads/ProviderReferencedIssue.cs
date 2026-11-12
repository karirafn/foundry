using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Providers.TrackerReads;

/// <summary>What a provider reports about one same-project issue, before its relation is attached.</summary>
internal sealed record ProviderReferencedIssue(
    string Title,
    bool IsOpen,
    IReadOnlyList<string> Labels,
    ClosingMergeRequestState ClosingMergeRequest,
    string Body);
