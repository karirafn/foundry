namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ValueObjectTests;

using Foundry.WebApi.Shared.Abstractions;

internal sealed class TestValueObject : ValueObject
{
    private readonly string _first;
    private readonly int _second;

    internal TestValueObject(string first, int second)
    {
        _first = first;
        _second = second;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return _first;
        yield return _second;
    }
}
