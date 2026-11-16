using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.PackageSourceTests;

/// <summary>
/// Seeds a GitLab account covering <c>group</c>, a repository <c>group/app</c> that declares
/// <see cref="SourceUrl"/>, and the stored source with a Readable verdict for the account. The probe is the only
/// writer of package sources, so the state is seeded through the DbContext.
/// </summary>
internal static class PackageSourceSeeder
{
    internal const string SourceUrl = "https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json";
    internal const string AccountName = "deploy-bot";
    internal const string Slug = "group/app";

    internal static readonly DateTimeOffset CheckedAt = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    internal sealed record Seeded(Guid SourceId, Guid RepositoryId);

    internal static async Task<Seeded> SeedAsync(FoundryWebAppFactory factory)
    {
        Guid accountId = await AccountSeeder.SeedGitLabAccountAsync(factory, name: AccountName);
        await AccountSeeder.SetOwnerNamespacesAsync(factory, accountId, "group");

        using IServiceScope scope = factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create(Slug).ValueOrThrow(),
            "gitlab.com",
            pollInterval: null).ValueOrThrow();
        repository.RecordRootListing(
            new RootNuGetConfig.Declared(
                "sha",
                "<configuration />",
                [DeclaredPackageSource.Create("group", SourceUrl).ValueOrThrow()]),
            listingETag: null);
        dbContext.Add(repository);

        PackageSource source = PackageSource.Create(new Uri(SourceUrl));
        source.RecordProbe(
            new ProbeCredential.Account(CredentialId.From(accountId)),
            tokenVersion: 0,
            new PackageSourceVerdict.Readable(),
            CheckedAt);
        dbContext.Add(source);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return new Seeded(source.Id.Value, repository.Id.Value);
    }
}
