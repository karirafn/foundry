using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Issues.Features.Dependencies;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;

namespace Foundry.UnitTests.Modules.Issues;

/// <summary>
/// Builds an <see cref="IssueDependencyApplier"/> over a real <see cref="IssueQueries"/> so cycle
/// detection reads the committed dependency graph from the test database.
/// </summary>
internal static class IssueDependencyApplierFactory
{
    public static IssueDependencyApplier Create(DbContext db, IDomainEventDispatcher dispatcher) =>
        new(
            db,
            new IssueQueries(
                db,
                new NullRepositorySlugQueries(),
                new NullRepositoryEligibilityQuery(),
                new NullWorkerRunQueries(),
                new InFlightWorkerCountQuery(db),
                new NullGlobalSettingsQueries()),
            dispatcher, TimeProvider.System);
}
