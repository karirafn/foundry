using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// A claimable issue paired with its resolved repository dispatch info.
/// </summary>
internal sealed record DispatchCandidate(ClaimableIssue Issue, RepositoryDispatchInfo DispatchInfo);
