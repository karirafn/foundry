using System.Text.Json;

using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Persistence.FoundryDbContextTests;

/// <summary>
/// Verifies that the EF model cache is keyed on the data-protection provider and logger factory,
/// so that contexts built with different providers do not silently share a stale model.
/// </summary>
public sealed class ModelCacheIsolation
{
    private static DbContextOptions<FoundryDbContext> BuildOptions(SqliteConnection connection)
        => new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(connection)
            .Options;

    /// <summary>
    /// Regression guard: two contexts constructed without an explicit provider must share the
    /// same default key ring, so a row seeded through the direct path decrypts through the DI path.
    /// </summary>
    [Fact]
    public async Task WhenTwoDefaultContextsShareSameProvider_ReadSucceeds()
    {
        // Arrange
        using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        DbContextOptions<FoundryDbContext> options = BuildOptions(connection);

        await using FoundryDbContext setupContext = new(options);
        await setupContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        // Seed via direct (no-provider) context.
        await using FoundryDbContext seedContext = new(options);
        ClaudeAccount seeded = ClaudeAccount.Create();
        seeded.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("sk-test-key")));
        seedContext.Set<ClaudeAccount>().Add(seeded);
        await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Build a DI-constructed context (no provider registered — uses the same shared default).
        ServiceCollection services = new();
        services.AddDbContext<FoundryDbContext>(opts => opts.UseSqlite(connection));
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        using IServiceScope scope = serviceProvider.CreateScope();

        // Act — read via DI-built context.
        FoundryDbContext diContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        ClaudeAccount? result = await diContext
            .Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        // Assert — decrypt must succeed and the auth mode must round-trip.
        ClaudeAccount read = result.ShouldNotBeNull();
        AuthMode.ApiKey apiKey = read.AuthMode.ShouldBeOfType<AuthMode.ApiKey>();
        ApiKeyCredential.Present present = apiKey.Credential.ShouldBeOfType<ApiKeyCredential.Present>();
        present.Value.ShouldBe("sk-test-key");
    }

    /// <summary>
    /// Ensures the model cache is NOT shared between two contexts with different providers:
    /// seeding with P1 and reading with P1 must decrypt successfully even after the cache
    /// was primed by a context with a different provider P0.
    /// This test is red on main because the model built with P0 is reused for P1.
    /// </summary>
    [Fact]
    public async Task WhenModelPrimedByP0_SeedAndReadWithP1_DecryptsWithP1()
    {
        // Arrange
        using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        IDataProtectionProvider p0 = DataProtectionProvider.Create("P0-" + Guid.NewGuid().ToString("N"));
        IDataProtectionProvider p1 = DataProtectionProvider.Create("P1-" + Guid.NewGuid().ToString("N"));

        // Prime the EF model cache with P0.
        await using FoundryDbContext primeContext = new(BuildOptions(connection), p0);
        await primeContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        // Seed through a context with P1.
        await using FoundryDbContext seedContext = new(BuildOptions(connection), p1);
        ClaudeAccount seeded = ClaudeAccount.Create();
        seeded.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("p1-secret-key")));
        seedContext.Set<ClaudeAccount>().Add(seeded);
        await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act — read back through another P1 context.
        await using FoundryDbContext readContext = new(BuildOptions(connection), p1);
        ClaudeAccount? result = await readContext
            .Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        // Assert — P1 must be able to decrypt what P1 encrypted.
        ClaudeAccount read = result.ShouldNotBeNull();
        AuthMode.ApiKey apiKey = read.AuthMode.ShouldBeOfType<AuthMode.ApiKey>();
        ApiKeyCredential.Present present = apiKey.Credential.ShouldBeOfType<ApiKeyCredential.Present>();
        present.Value.ShouldBe("p1-secret-key");
    }

    /// <summary>
    /// Ensures the model cache is keyed on the logger factory:
    /// when a context uses a fresh capturing logger factory, decrypt warnings from that context
    /// must appear in its own factory rather than a previously-cached one.
    /// This test is red on main because the cached model holds the original logger factory (null).
    /// </summary>
    [Fact]
    public async Task WhenModelPrimedWithNullLogger_NewContextWithCapturingFactory_ReceivesDecryptWarning()
    {
        // Arrange
        using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // P0 seeds the data; P1 will try to read it — the keys differ, so decrypt throws
        // CryptographicException, which EncryptedStringConverter converts to a logged warning.
        IDataProtectionProvider p0 = DataProtectionProvider.Create("Logger-P0-" + Guid.NewGuid().ToString("N"));
        IDataProtectionProvider p1 = DataProtectionProvider.Create("Logger-P1-" + Guid.NewGuid().ToString("N"));

        // Prime the cache with a null logger factory using P0.
        await using FoundryDbContext primeContext = new(BuildOptions(connection), p0, loggerFactory: null);
        await primeContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        // Seed a valid row encrypted by P0.
        ClaudeAccount seeded = ClaudeAccount.Create();
        seeded.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("orig-key")));
        primeContext.Set<ClaudeAccount>().Add(seeded);
        await primeContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Build a fresh capturing logger factory — must receive the warning on read.
        CapturingLoggerFactory capturingFactory = new();

        // Act — read back through a context using P1 and the capturing logger factory.
        // P1 cannot decrypt P0's ciphertext → CryptographicException → warning logged → returns ""
        // → DeserializeAuthMode("") throws JsonException from EF materialization.
        await using FoundryDbContext readContext = new(BuildOptions(connection), p1, capturingFactory);
        await Should.ThrowAsync<JsonException>(async () =>
            await readContext
                .Set<ClaudeAccount>()
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken));

        // Assert — the decrypt warning must be routed to the capturing factory, not lost.
        capturingFactory.Warnings.ShouldNotBeEmpty();
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings => _warnings;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_warnings);

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }

        private sealed class CapturingLogger(List<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning)
                {
                    warnings.Add(formatter(state, exception));
                }
            }
        }
    }
}
