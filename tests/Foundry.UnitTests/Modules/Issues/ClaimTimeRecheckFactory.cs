using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.UnitTests.Modules.Issues;

/// <summary>
/// Builds a <see cref="ClaimTimeRecheck"/> whose blocker transitions run through a real
/// <see cref="Foundry.Modules.Issues.Features.Dependencies.IssueDependencyApplier"/> over the test database.
/// </summary>
internal static class ClaimTimeRecheckFactory
{
    public static ClaimTimeRecheck Create(
        DbContext db,
        IIssueBlockerQueries issueBlockerQueries,
        IDomainEventDispatcher dispatcher) =>
        new(
            issueBlockerQueries,
            IssueDependencyApplierFactory.Create(db, dispatcher),
            NullLogger<ClaimTimeRecheck>.Instance);
}
