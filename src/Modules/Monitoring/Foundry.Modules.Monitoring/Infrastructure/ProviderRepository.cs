namespace Foundry.Modules.Monitoring.Infrastructure;

internal sealed record ProviderRepository(string Slug, bool IsPrivate, bool CanPush);
