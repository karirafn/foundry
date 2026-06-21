using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Entities;

public static class RepositorySlugErrors
{
    public static readonly Error InvalidFormat = new(
        "RepositorySlug.InvalidFormat",
        "Repository slug must be in the format 'owner/name' with non-empty segments.");
}
