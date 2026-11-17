using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ChangedPathTests;

public sealed class Create
{
    [Fact]
    public void WhenThePathIsEmpty_Throws()
    {
        // Act / Assert
        Should.Throw<ArgumentException>(() => new ChangedPath(ChangeStatus.Added, string.Empty));
    }

    [Fact]
    public void WhenTheStatusIsUnknown_Throws()
    {
        // Act / Assert
        Should.Throw<ArgumentOutOfRangeException>(() => new ChangedPath((ChangeStatus)42, "src/a.cs"));
    }
}
