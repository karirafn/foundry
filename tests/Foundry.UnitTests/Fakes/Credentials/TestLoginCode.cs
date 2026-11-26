using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Testing;

namespace Foundry.UnitTests.Fakes.Credentials;

/// <summary>Builds a well-formed <see cref="LoginCode"/> (<c>&lt;code&gt;#&lt;state&gt;</c>) for tests.</summary>
internal static class TestLoginCode
{
    internal static LoginCode Of(string code) => LoginCode.Create($"{code}#state").ValueOrThrow();
}
