using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Features.Providers.TrackerReads;

/// <summary>A same-project issue selected for reading, with how the dispatched issue relates to it.</summary>
internal sealed record IssueReference(int Number, IssueRelation Relation);
