using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundry.Shared.Infrastructure.DataProtection;

/// <summary>A table column whose values an <see cref="IDataProtectedConverter"/> encrypts.</summary>
public sealed record EncryptedColumn(string Table, string Column, string KeyColumn, string ProtectorPurpose)
{
    public static IReadOnlyList<EncryptedColumn> DiscoverIn(IModel model)
    {
        List<EncryptedColumn> columns = [];

        foreach (IEntityType entityType in model.GetEntityTypes())
        {
            string? table = entityType.GetTableName();
            if (table is null)
            {
                continue;
            }

            StoreObjectIdentifier store = StoreObjectIdentifier.Table(table, entityType.GetSchema());

            foreach (IProperty property in entityType.GetDeclaredProperties())
            {
                if (property.GetValueConverter() is not IDataProtectedConverter converter)
                {
                    continue;
                }

                string column = property.GetColumnName(store)!;
                columns.Add(new EncryptedColumn(table, column, KeyColumnOf(entityType, store), converter.ProtectorPurpose));
            }
        }

        return columns
            .DistinctBy(column => (column.Table, column.Column))
            .ToList();
    }

    private static string KeyColumnOf(IEntityType entityType, StoreObjectIdentifier store)
    {
        IKey? primaryKey = entityType.FindPrimaryKey();
        if (primaryKey is null || primaryKey.Properties.Count != 1)
        {
            throw new InvalidOperationException(
                $"Table '{store.Name}' holds an encrypted column but has no single-column primary key, " +
                "so its rows cannot be rewritten one by one.");
        }

        return primaryKey.Properties[0].GetColumnName(store)!;
    }
}
