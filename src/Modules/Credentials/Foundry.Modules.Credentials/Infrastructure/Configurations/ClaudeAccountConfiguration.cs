using Foundry.Modules.Credentials.Domain;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Credentials.Infrastructure.Configurations;

public sealed class ClaudeAccountConfiguration : IEntityTypeConfiguration<ClaudeAccount>
{
    public void Configure(EntityTypeBuilder<ClaudeAccount> builder)
    {
        builder.ToTable("claude_account");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasConversion(new StronglyTypedIdValueConverter<ClaudeAccountId>())
            .HasColumnName("id");

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at");
    }
}
