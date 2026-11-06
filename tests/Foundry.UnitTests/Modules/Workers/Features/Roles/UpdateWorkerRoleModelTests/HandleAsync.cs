using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Modules.Workers.Features.Roles.Models;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Roles.UpdateWorkerRoleModelTests;

public sealed class HandleAsync : IAsyncLifetime
{
    private const string ListedModel = "claude-opus-4-5-20251101";

    private static readonly ClaudeModelList Listed = new(
        [new ClaudeModel(ListedModel, "Claude Opus 4.5", "active")],
        DateTimeOffset.UnixEpoch,
        null);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        await using FoundryDbContext setup = CreateDbContext();
        await setup.Database.EnsureCreatedAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    private async Task<Result<WorkerRoleModelResponse>> SaveAsync(
        string model,
        string role = "implement",
        IClaudeModelCatalog? catalog = null)
    {
        await using FoundryDbContext db = CreateDbContext();
        UpdateWorkerRoleModel.Handler sut = new(db, catalog ?? new StubClaudeModelCatalog(Listed));
        return await sut.HandleAsync(new UpdateWorkerRoleModel.Command(role, model), TestContext.Current.CancellationToken);
    }

    private async Task<string?> StoredModelAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        WorkerRoleSettings? settings = await db.Set<WorkerRoleSettings>()
            .FindAsync([WorkerRole.Implement], TestContext.Current.CancellationToken);
        return settings?.Model;
    }

    [Fact]
    public async Task WhenModelIsListed_PersistsIt()
    {
        // Act
        Result<WorkerRoleModelResponse> result = await SaveAsync(ListedModel);

        // Assert
        result.ShouldBeOfType<Result<WorkerRoleModelResponse>.Success>().Value
            .ShouldBe(new WorkerRoleModelResponse("implement", ListedModel));
        (await StoredModelAsync()).ShouldBe(ListedModel);
    }

    [Theory]
    [InlineData("fable")]
    [InlineData("opus")]
    [InlineData("sonnet")]
    [InlineData("haiku")]
    [InlineData("opus[1m]")]
    [InlineData("sonnet[1m]")]
    public async Task WhenModelIsAnAlias_PersistsItWithoutConsultingTheList(string alias)
    {
        // Arrange
        StubClaudeModelCatalog catalog = new(ClaudeModelList.Empty);

        // Act
        Result<WorkerRoleModelResponse> result = await SaveAsync(alias, catalog: catalog);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await StoredModelAsync()).ShouldBe(alias);
        catalog.GetCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenModelIsEmpty_ClearsTheSetting()
    {
        // Arrange
        await SaveAsync(ListedModel);

        // Act
        Result<WorkerRoleModelResponse> result = await SaveAsync("   ");

        // Assert
        result.ShouldBeOfType<Result<WorkerRoleModelResponse>.Success>().Value.Model.ShouldBeEmpty();
        (await StoredModelAsync()).ShouldBe(string.Empty);
    }

    [Fact]
    public async Task WhenModelIsNeitherAnAliasNorListed_RejectsItWithAFieldKeyedError()
    {
        // Act
        Result<WorkerRoleModelResponse> result = await SaveAsync("claude-typo");

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerRoleModelResponse>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(UpdateWorkerRoleModel.ModelNotListedCode),
            () => error.Message.ShouldContain("claude-typo"));
        (await StoredModelAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheListCouldNotBeRefreshed_TheRejectionSaysSo()
    {
        // Arrange
        StubClaudeModelCatalog catalog = new(ClaudeModelList.Empty with { RefreshError = "The Models API answered HTTP 401." });

        // Act
        Result<WorkerRoleModelResponse> result = await SaveAsync(ListedModel, catalog: catalog);

        // Assert
        Error error = result.ShouldBeOfType<Result<WorkerRoleModelResponse>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(UpdateWorkerRoleModel.ModelNotListedCode),
            () => error.Message.ShouldContain("HTTP 401"));
    }

    [Fact]
    public async Task WhenModelIsTooLong_RejectsIt()
    {
        // Act
        Result<WorkerRoleModelResponse> result = await SaveAsync(new string('m', WorkerRoleSettings.MaxModelLength + 1));

        // Assert
        result.ShouldBeOfType<Result<WorkerRoleModelResponse>.Failure>().Error.Code
            .ShouldBe(UpdateWorkerRoleModel.ModelTooLongCode);
    }

    [Fact]
    public async Task WhenRoleIsUnknown_ReturnsRoleNotFound()
    {
        // Act
        Result<WorkerRoleModelResponse> result = await SaveAsync("opus", role: "triage");

        // Assert
        result.ShouldBeOfType<Result<WorkerRoleModelResponse>.Failure>().Error.Code
            .ShouldBe(UpdateWorkerRoleModel.RoleNotFoundCode);
    }
}
