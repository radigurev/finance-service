using Finance.Accounts.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Accounts.DBModel.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="Account"/> entity.
/// </summary>
public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    /// <summary>Configures the table, columns, indexes, and relationships for <see cref="Account"/>.</summary>
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts", schema: "accounts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Code).IsRequired().HasMaxLength(20);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Type).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.CountryCode).IsRequired().HasMaxLength(3);
        builder.Property(a => a.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(a => a.CreatedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(a => a.UpdatedAt);

        builder.HasIndex(a => new { a.CountryCode, a.Code }).IsUnique();
        builder.HasIndex(a => a.ParentId);

        builder.HasOne(a => a.Parent)
            .WithMany(a => a.Children)
            .HasForeignKey(a => a.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
