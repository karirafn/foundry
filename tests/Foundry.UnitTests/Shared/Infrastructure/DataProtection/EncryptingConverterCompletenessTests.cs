using System.Reflection;

using Foundry.Shared.Infrastructure.DataProtection;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.DataProtection;

public sealed class EncryptingConverterCompletenessTests
{
    private const string AssemblyPrefix = "Foundry.";
    private const int KnownEncryptingConverterCount = 3;

    private const BindingFlags ConstructorFlags =
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;

    [Fact]
    public void EveryConverterTakingADataProtectionProvider_ImplementsTheMarker()
    {
        // Arrange
        List<Type> encryptingConverters = FoundryAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false } && type.IsAssignableTo(typeof(ValueConverter)))
            .Where(AcceptsDataProtectionProvider)
            .ToList();

        // Act
        List<string> unmarked = encryptingConverters
            .Where(type => !type.IsAssignableTo(typeof(IDataProtectedConverter)))
            .Select(type => type.FullName!)
            .ToList();

        // Assert
        encryptingConverters.Count.ShouldBeGreaterThanOrEqualTo(KnownEncryptingConverterCount);
        unmarked.ShouldBeEmpty();
    }

    private static bool AcceptsDataProtectionProvider(Type type)
    {
        return type
            .GetConstructors(ConstructorFlags)
            .Any(constructor => constructor
                .GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(IDataProtectionProvider)));
    }

    private static IEnumerable<Assembly> FoundryAssemblies()
    {
        Assembly webApi = typeof(FoundryDbContext).Assembly;
        IEnumerable<Assembly> referenced = webApi
            .GetReferencedAssemblies()
            .Where(name => name.Name!.StartsWith(AssemblyPrefix, StringComparison.Ordinal))
            .Select(Assembly.Load);

        return referenced.Prepend(webApi);
    }
}
