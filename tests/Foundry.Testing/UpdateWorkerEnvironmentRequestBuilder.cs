namespace Foundry.Testing;

/// <summary>
/// Test data builder for the <c>PUT /api/settings/worker-environment</c> request body.
/// Defaults to a valid body with no mounts and no host hooks source.
/// </summary>
public sealed class UpdateWorkerEnvironmentRequestBuilder
{
    private readonly List<object> _mounts = [];
    private int _memoryLimitMb = 4096;
    private double _cpuLimit = 4.0;
    private int _pidsLimit = 1024;
    private string _hostSettingsPath = string.Empty;
    private string[] _excludedHookCommands = [];

    public UpdateWorkerEnvironmentRequestBuilder WithMemoryLimitMb(int memoryLimitMb)
    {
        _memoryLimitMb = memoryLimitMb;
        return this;
    }

    public UpdateWorkerEnvironmentRequestBuilder WithCpuLimit(double cpuLimit)
    {
        _cpuLimit = cpuLimit;
        return this;
    }

    public UpdateWorkerEnvironmentRequestBuilder WithPidsLimit(int pidsLimit)
    {
        _pidsLimit = pidsLimit;
        return this;
    }

    public UpdateWorkerEnvironmentRequestBuilder WithMount(string containerPath, string hostPath, bool readOnly = true)
    {
        _mounts.Add(new { containerPath, hostPath, readOnly });
        return this;
    }

    public UpdateWorkerEnvironmentRequestBuilder WithHostSettingsPath(string hostSettingsPath)
    {
        _hostSettingsPath = hostSettingsPath;
        return this;
    }

    public UpdateWorkerEnvironmentRequestBuilder WithExcludedHookCommands(params string[] excludedHookCommands)
    {
        _excludedHookCommands = excludedHookCommands;
        return this;
    }

    public object Build() => new
    {
        memoryLimitMb = _memoryLimitMb,
        cpuLimit = _cpuLimit,
        pidsLimit = _pidsLimit,
        mounts = _mounts.ToArray(),
        hostSettingsPath = _hostSettingsPath,
        excludedHookCommands = _excludedHookCommands,
    };
}
