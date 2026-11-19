using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.PullRequestTextTests;

internal static class Descriptions
{
    public static PullRequestDescription Create(
        string[]? changes = null,
        string[]? verification = null,
        string? cause = null,
        string[]? attention = null,
        string? notes = null) =>
        PullRequestDescription.TryCreate(new RunPullRequestDescription(
            cause,
            changes ?? ["Retries webhook delivery with backoff."],
            verification ?? ["dotnet test: 12 passed."],
            attention ?? [],
            notes))!;
}
