namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure;

internal sealed class ThrowingHandler(Exception exception) : DelegatingHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        throw exception;
    }
}
