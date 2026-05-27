using System.Collections.ObjectModel;

namespace Foundry.WebApi.Modules.Monitoring.Features;

public sealed class MonitoringOptions
{
    public int DefaultPollIntervalSeconds { get; set; } = 30;

    // CA2227/CA1002: Collection<T> with a public setter is required for IConfiguration.Bind() to populate these lists.
#pragma warning disable CA2227, CA1002
    public List<AccountOption> Accounts { get; set; } = [];

    public List<RepositoryOption> Repositories { get; set; } = [];
#pragma warning restore CA2227, CA1002
}

public sealed class AccountOption
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string SecretKeyName { get; set; } = string.Empty;
}

public sealed class RepositoryOption
{
    public string Slug { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public int? PollIntervalSeconds { get; set; }

    public bool IsActive { get; set; } = true;
}
