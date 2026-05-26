using Microsoft.EntityFrameworkCore;

namespace Foundry.WebApi.Shared.Persistence;

public sealed class FoundryDbContext(DbContextOptions<FoundryDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FoundryDbContext).Assembly);
    }
}
