using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class CurrencyTransactionConfiguration : IEntityTypeConfiguration<CurrencyTransaction>
{
    public void Configure(EntityTypeBuilder<CurrencyTransaction> builder)
    {
        builder.ToTable("currency_transactions");

        builder.HasKey(ct => ct.Id);

        builder.Property(ct => ct.TransactionDate)
            .IsRequired();

        builder.Property(ct => ct.TransactionType)
            .IsRequired();

        builder.Property(ct => ct.ForeignAmount)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(ct => ct.HomeAmount)
            .HasPrecision(18, 2);

        builder.Property(ct => ct.ExchangeRate)
            .HasPrecision(18, 6);

        builder.Property(ct => ct.Notes)
            .HasMaxLength(500);

        builder.Property(ct => ct.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(ct => ct.CreatedAt)
            .IsRequired();

        builder.Property(ct => ct.UpdatedAt)
            .IsRequired();

        // Indexes
        builder.HasIndex(ct => ct.CurrencyLedgerId)
            .HasDatabaseName("IX_CurrencyTransaction_CurrencyLedgerId");

        builder.HasIndex(ct => ct.TransactionDate)
            .HasDatabaseName("IX_CurrencyTransaction_TransactionDate");

        // Relationships
        builder.HasOne(ct => ct.RelatedStockTransaction)
            .WithMany()
            .HasForeignKey(ct => ct.RelatedStockTransactionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
