using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Modules.Workers.Features.Roles.Models;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Roles.UpdateImplementDemandModelTests;

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

    private async Task<Result<ImplementDemandModel>> SaveAsync(
        string model,
        string effort = "",
        string demand = "routine",
        IClaudeModelCatalog? catalog = null)
    {
        await using FoundryDbContext db = CreateDbContext();
        UpdateImplementDemandModel.Handler sut = new(db, catalog ?? new StubClaudeModelCatalog(Listed));
        return await sut.HandleAsync(
            new UpdateImplementDemandModel.Command(demand, model, effort),
            TestContext.Current.CancellationToken);
    }

    private async Task<ImplementTierSettings?> StoredAsync(ImplementDemand demand)
    {
        await using FoundryDbContext db = CreateDbContext();
        return await db.Set<ImplementTierSettings>().FindAsync([demand], TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenModelAndEffortAreValid_PersistsTheTrimmedValuesForThatDemandOnly()
    {
        // Act
        Result<ImplementDemandModel> result = await SaveAsync($"  {ListedModel} ", " high ", demand: "routine");

        // Assert
        result.ShouldBeOfType<Result<ImplementDemandModel>.Success>().Value
            .ShouldBe(new ImplementDemandModel(ImplementDemand.Routine, ListedModel, "high"));
        ImplementTierSettings stored = (await StoredAsync(ImplementDemand.Routine)).ShouldNotBeNull();
        stored.ShouldSatisfyAllConditions(
            () => stored.Model.ShouldBe(ListedModel),
            () => stored.Effort.ShouldBe("high"));
        (await StoredAsync(ImplementDemand.Demanding)).ShouldBeNull();
    }

    [Fact]
    public async Task WhenAnExistingSettingIsSaved_ItIsUpdated()
    {
        // Arrange
        await SaveAsync("opus", "xhigh", demand: "demanding");

        // Act
        Result<ImplementDemandModel> result = await SaveAsync(string.Empty, string.Empty, demand: "demanding");

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ImplementTierSettings stored = (await StoredAsync(ImplementDemand.Demanding)).ShouldNotBeNull();
        stored.ShouldSatisfyAllConditions(
            () => stored.Model.ShouldBeEmpty(),
            () => stored.Effort.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenOnlyTheEffortIsSet_PersistsIt()
    {
        // Act
        Result<ImplementDemandModel> result = await SaveAsync(string.Empty, "low");

        // Assert
        result.ShouldBeOfType<Result<ImplementDemandModel>.Success>().Value
            .ShouldBe(new ImplementDemandModel(ImplementDemand.Routine, string.Empty, "low"));
    }

    [Fact]
    public async Task WhenModelIsAnAlias_PersistsItWithoutConsultingTheList()
    {
        // Arrange
        StubClaudeModelCatalog catalog = new(ClaudeModelList.Empty);

        // Act
        Result<ImplementDemandModel> result = await SaveAsync("sonnet", catalog: catalog);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        catalog.GetCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenModelIsNeitherAnAliasNorListed_RejectsItAndSavesNothing()
    {
        // Act
        Result<ImplementDemandModel> result = await SaveAsync("claude-typo", "high");

        // Assert
        result.ShouldBeOfType<Result<ImplementDemandModel>.Failure>().Error.Code
            .ShouldBe(RoleModelValidation.ModelNotListedCode);
        (await StoredAsync(ImplementDemand.Routine)).ShouldBeNull();
    }

    [Fact]
    public async Task WhenModelIsTooLong_RejectsIt()
    {
        // Act
        Result<ImplementDemandModel> result = await SaveAsync(new string('m', WorkerRoleSettings.MaxModelLength + 1));

        // Assert
        result.ShouldBeOfType<Result<ImplementDemandModel>.Failure>().Error.Code
            .ShouldBe(RoleModelValidation.ModelTooLongCode);
    }

    [Theory]
    [InlineData("max")]
    [InlineData("HIGH")]
    [InlineData("extreme")]
    public async Task WhenEffortIsOutsideTheClosedSet_RejectsItAndSavesNothing(string effort)
    {
        // Act
        Result<ImplementDemandModel> result = await SaveAsync("sonnet", effort);

        // Assert
        Error error = result.ShouldBeOfType<Result<ImplementDemandModel>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(UpdateImplementDemandModel.EffortInvalidCode),
            () => error.Message.ShouldContain(effort));
        (await StoredAsync(ImplementDemand.Routine)).ShouldBeNull();
    }

    [Theory]
    [InlineData("urgent")]
    [InlineData("Routine")]
    public async Task WhenDemandIsUnknown_ReturnsANotFoundError(string demand)
    {
        // Act
        Result<ImplementDemandModel> result = await SaveAsync("sonnet", demand: demand);

        // Assert
        Error error = result.ShouldBeOfType<Result<ImplementDemandModel>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(UpdateImplementDemandModel.DemandNotFoundCode),
            () => error.Kind.ShouldBe(ErrorKind.NotFound));
    }
}
