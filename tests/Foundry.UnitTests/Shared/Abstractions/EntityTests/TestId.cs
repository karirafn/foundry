namespace Foundry.UnitTests.Shared.Abstractions.EntityTests;

using Foundry.Shared;

internal readonly record struct TestId(Guid Value) : IStronglyTypedId<TestId>
{
    public static TestId From(Guid value) => new(value);
}
