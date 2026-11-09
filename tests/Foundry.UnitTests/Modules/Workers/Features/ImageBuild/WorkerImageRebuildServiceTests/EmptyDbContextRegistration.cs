using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildServiceTests;

/// <summary>
/// Registers a model-less <see cref="DbContext"/> for tests that capture dispatched events directly: the service saves
/// after each dispatch to harvest the outbox, and with nothing tracked that save never touches the database.
/// </summary>
internal static class EmptyDbContextRegistration
{
    public static IServiceCollection AddEmptyDbContext(this IServiceCollection services) =>
        services.AddScoped(_ => new DbContext(
            new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options));
}
