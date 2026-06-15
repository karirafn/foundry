using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.IntegrationTests.Modules.Monitoring;

internal static class RepositorySeeder
{
    // Seeds a MonitoredRepository directly via DbContext — no POST endpoint exists yet.
    internal static async Task<Guid> SeedRepositoryAsync(
        FoundryWebAppFactory factory,
        Guid accountId,
        string slug = "owner/repo",
        int? pollIntervalSeconds = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        Result<RepositorySlug> slugResult = RepositorySlug.Create(slug);
        RepositorySlug repositorySlug = ((Result<RepositorySlug>.Success)slugResult).Value;

        TimeSpan? pollInterval = pollIntervalSeconds.HasValue
            ? TimeSpan.FromSeconds(pollIntervalSeconds.Value)
            : null;

        MonitoredRepository repository = MonitoredRepository.Create(
            repositorySlug,
            AccountId.From(accountId),
            pollInterval);

        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repository.Id.Value;
    }
}
