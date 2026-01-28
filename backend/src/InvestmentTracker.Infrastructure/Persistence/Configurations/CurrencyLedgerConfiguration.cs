using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class CurrencyLedgerConfiguration : IEntityTypeConfiguration<CurrencyLedger>
{
    public void Configure(EntityTypeBuilder<CurrencyLedger> builder)
    {
        builder.ToTable("currency_ledgers");

        builder.HasKey(cl => cl.Id);

        builder.Property(cl => cl.CurrencyCode)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(cl => cl.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cl => cl.HomeCurrency)
            .IsRequired()
            .HasMaxLength(3)
            .HasDefaultValue("TWD");

        builder.Property(cl => cl.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(cl => cl.CreatedAt)
            .IsRequired();

        builder.Property(cl => cl.UpdatedAt)
            .IsRequired();

        // Indexes
        builder.HasIndex(cl => cl.UserId)
            .HasDatabaseName("IX_CurrencyLedger_UserId");

        // Note: Unique constraint on UserId+CurrencyCode removed to support 1:1 Portfolio-Ledger binding
        // where each portfolio needs its own dedicated ledger

        // Relationships
        builder.HasMany(cl => cl.Transactions)
            .WithOne(ct => ct.CurrencyLedger)
            .HasForeignKey(ct => ct.CurrencyLedgerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
