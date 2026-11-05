using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Foundry.Shared.Infrastructure.DataProtection;

/// <summary>
/// Rewrites every Data Protection column from the legacy discriminator to the current one in a
/// single transaction. It works on raw column text because the EF converters cannot express
/// "readable only under the legacy discriminator".
/// </summary>
public sealed partial class DataProtectedColumnReEncryptor(
    IDataProtectionProvider current,
    IDataProtectionProvider? legacy,
    ILogger<DataProtectedColumnReEncryptor> logger)
{
    public async Task ReEncryptAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        ISqlGenerationHelper sql = dbContext.GetService<ISqlGenerationHelper>();

        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (EncryptedColumn column in EncryptedColumn.DiscoverIn(dbContext.Model))
        {
            await ReEncryptColumnAsync(dbContext, sql, column, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ReEncryptColumnAsync(
        DbContext dbContext,
        ISqlGenerationHelper sql,
        EncryptedColumn column,
        CancellationToken cancellationToken)
    {
        string table = sql.DelimitIdentifier(column.Table);
        string valueColumn = sql.DelimitIdentifier(column.Column);
        string keyColumn = sql.DelimitIdentifier(column.KeyColumn);
        string select =
            $"SELECT {keyColumn} AS {nameof(StoredRow.RowId)}, {valueColumn} AS {nameof(StoredRow.StoredValue)} " +
            $"FROM {table} WHERE {valueColumn} IS NOT NULL AND {valueColumn} <> ''";
        string update = $"UPDATE {table} SET {valueColumn} = {{0}} WHERE {keyColumn} = {{1}} AND {valueColumn} = {{2}}";

        IDataProtector currentProtector = current.CreateProtector(column.ProtectorPurpose);
        IDataProtector? legacyProtector = legacy?.CreateProtector(column.ProtectorPurpose);

        List<StoredRow> rows = await dbContext.Database
            .SqlQueryRaw<StoredRow>(select)
            .ToListAsync(cancellationToken);

        int rewritten = 0;
        foreach (StoredRow row in rows)
        {
            ReEncryptionOutcome outcome = ReEncryptionOutcome.Evaluate(row.StoredValue, currentProtector, legacyProtector);

            if (outcome is ReEncryptionOutcome.Rewritten rewrite)
            {
                int affected = await dbContext.Database.ExecuteSqlRawAsync(
                    update,
                    [rewrite.NewStoredValue, row.RowId, row.StoredValue],
                    cancellationToken);

                if (affected == 0)
                {
                    LogChangedConcurrently(column.Table, column.Column, row.RowId);
                    continue;
                }

                rewritten++;
            }
            else if (outcome is ReEncryptionOutcome.Undecryptable)
            {
                LogUndecryptable(column.Table, column.Column, row.RowId);
            }
        }

        if (rewritten > 0)
        {
            LogRewritten(rewritten, column.Table, column.Column);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Table}.{Column} row {RowId} decrypts under neither the current nor the legacy Data Protection discriminator and was left unchanged")]
    private partial void LogUndecryptable(string table, string column, string rowId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Table}.{Column} row {RowId} changed after it was read and was left unchanged")]
    private partial void LogChangedConcurrently(string table, string column, string rowId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-encrypted {Count} value(s) in {Table}.{Column}")]
    private partial void LogRewritten(int count, string table, string column);

    private sealed class StoredRow
    {
        public string RowId { get; init; } = string.Empty;

        public string StoredValue { get; init; } = string.Empty;
    }
}
