using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Testing;

public sealed class MonitoredRepositoryBuilder
{
    private RepositorySlug _slug = RepositorySlug.Create("octocat/hello-world").ValueOrThrow();
    private string _host = "github.com";
    private TimeSpan? _pollInterval;
    private int _position;
    private int _maxConcurrentWorkers = MonitoredRepository.DefaultMaxConcurrentWorkers;

    public MonitoredRepositoryBuilder WithSlug(RepositorySlug value) { _slug = value; return this; }

    public MonitoredRepositoryBuilder WithHost(string value) { _host = value; return this; }

    public MonitoredRepositoryBuilder WithPollInterval(TimeSpan? value) { _pollInterval = value; return this; }

    public MonitoredRepositoryBuilder WithPosition(int value) { _position = value; return this; }

    public MonitoredRepositoryBuilder WithMaxConcurrentWorkers(int value) { _maxConcurrentWorkers = value; return this; }

    public MonitoredRepository Build() =>
        MonitoredRepository.Create(_slug, _host, _pollInterval, _position, _maxConcurrentWorkers).ValueOrThrow();
}
