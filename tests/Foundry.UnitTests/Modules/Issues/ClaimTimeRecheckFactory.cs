using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.UnitTests.Modules.Issues;

/// <summary>
/// Builds a <see cref="ClaimTimeRecheck"/> whose blocker transitions run through a real
/// <see cref="Foundry.Modules.Issues.Features.Dependencies.IssueDependencyApplier"/> over the test database. Without
/// <paramref name="branchWorkQueries"/>, every issue branch reads as absent, so Fresh candidates stay Fresh.
/// </summary>
internal static class ClaimTimeRecheckFactory
{
    public static ClaimTimeRecheck Create(
        DbContext db,
        IIssueBlockerQueries issueBlockerQueries,
        IDomainEventDispatcher dispatcher,
        IPostExitProviderQueries? branchWorkQueries = null,
        ILogger<ClaimTimeRecheck>? logger = null) =>
        new(
            issueBlockerQueries,
            IssueDependencyApplierFactory.Create(db, dispatcher),
            branchWorkQueries ?? new StubBranchWorkQueries(),
            new FreshBranchAdopter(db, dispatcher, TimeProvider.System),
            logger ?? NullLogger<ClaimTimeRecheck>.Instance);
}
