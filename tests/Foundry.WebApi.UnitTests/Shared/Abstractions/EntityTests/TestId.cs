namespace Foundry.WebApi.UnitTests.Shared.Abstractions.EntityTests;

using Foundry.WebApi.Shared.Abstractions;

internal readonly record struct TestId(Guid Value) : IStronglyTypedId<TestId>
{
    public static TestId From(Guid value) => new(value);
}
