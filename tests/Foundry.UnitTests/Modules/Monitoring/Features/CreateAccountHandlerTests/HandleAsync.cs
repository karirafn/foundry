using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.CreateAccountHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private CreateAccount.Handler BuildHandler(INamespaceDeriver namespaceDeriver)
    {
        return new CreateAccount.Handler(_dbContext, new StubValidateTokenHandler(), namespaceDeriver);
    }

    [Fact]
    public async Task WhenDeriverReturnsDerived_SetsNamespacesOnCredential()
    {
        // Arrange
        Namespace ns = Namespace.Create("octocat").ValueOrThrow();
        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Derived([ns]);
        CreateAccount.Handler handler = BuildHandler(new StubNamespaceDeriver(outcome));
        CreateAccount.Command command = new("github", "https://github.com", "ghp_test");

        // Act
        Result<CredentialSummary> result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        CredentialSummary summary = result.ShouldBeOfType<Result<CredentialSummary>.Success>().Value;
        Credential? stored = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(
                c => c.Id == CredentialId.From(summary.Id),
                TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.Namespaces.Count.ShouldBe(1);
        stored.Namespaces.ShouldContain(n => n.Value == "octocat");
    }

    [Fact]
    public async Task WhenDeriverReturnsUnavailable_SetsEmptyNamespaces()
    {
        // Arrange
        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Unavailable();
        CreateAccount.Handler handler = BuildHandler(new StubNamespaceDeriver(outcome));
        CreateAccount.Command command = new("github", "https://github.com", "ghp_test");

        // Act
        Result<CredentialSummary> result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        CredentialSummary summary = result.ShouldBeOfType<Result<CredentialSummary>.Success>().Value;
        Credential? stored = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(
                c => c.Id == CredentialId.From(summary.Id),
                TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.Namespaces.ShouldBeEmpty();
    }

    private sealed class StubValidateTokenHandler
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken)
        {
            ValidateToken.Response response = new(
                IsValid: true,
                IsAuthFailure: false,
                ScopesVerified: true,
                MissingScopes: [],
                AccountName: "octocat");
            return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
        }
    }

    private sealed class StubNamespaceDeriver(NamespaceDerivationOutcome outcome) : INamespaceDeriver
    {
        public Task<NamespaceDerivationOutcome> DeriveAsync(
            Credential credential,
            CancellationToken cancellationToken) =>
            Task.FromResult(outcome);
    }
}
