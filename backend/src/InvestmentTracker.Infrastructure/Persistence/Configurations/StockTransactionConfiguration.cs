using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class StockTransactionConfiguration : IEntityTypeConfiguration<StockTransaction>
{
    public void Configure(EntityTypeBuilder<StockTransaction> builder)
    {
        builder.ToTable("stock_transactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TransactionDate)
            .IsRequired();

        builder.Property(t => t.Ticker)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(t => t.TransactionType)
            .IsRequired();

        builder.Property(t => t.Shares)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(t => t.PricePerShare)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(t => t.ExchangeRate)
            .IsRequired(false)
            .HasPrecision(18, 6);

        builder.Property(t => t.Fees)
            .IsRequired()
            .HasPrecision(18, 2)
            .HasDefaultValue(0m);

        builder.Property(t => t.FundSource)
            .IsRequired()
            .HasDefaultValue(FundSource.None);

        builder.Property(t => t.Notes)
            .HasMaxLength(500);

        builder.Property(t => t.Market)
            .IsRequired()
            .HasDefaultValue(StockMarket.US);

        builder.Property(t => t.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .IsRequired();

        // Indexes
        builder.HasIndex(t => t.PortfolioId)
            .HasDatabaseName("IX_StockTransaction_PortfolioId");

        builder.HasIndex(t => t.Ticker)
            .HasDatabaseName("IX_StockTransaction_Ticker");

        builder.HasIndex(t => t.TransactionDate)
            .HasDatabaseName("IX_StockTransaction_TransactionDate");

        // Relationships
        builder.HasOne(t => t.CurrencyLedger)
            .WithMany()
            .HasForeignKey(t => t.CurrencyLedgerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
