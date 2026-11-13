using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

namespace Foundry.Testing;

public sealed class EligibleRepositoryBuilder
{
    private Guid _id = Guid.NewGuid();
    private int _position;
    private int _maxConcurrentWorkers = MonitoredRepository.DefaultMaxConcurrentWorkers;
    private string _host = "github.com";

    public EligibleRepositoryBuilder WithId(Guid value) { _id = value; return this; }

    public EligibleRepositoryBuilder WithPosition(int value) { _position = value; return this; }

    public EligibleRepositoryBuilder WithMaxConcurrentWorkers(int value) { _maxConcurrentWorkers = value; return this; }

    public EligibleRepositoryBuilder WithHost(string value) { _host = value; return this; }

    public EligibleRepository Build() => new(_id, _position, _maxConcurrentWorkers, _host);
}
