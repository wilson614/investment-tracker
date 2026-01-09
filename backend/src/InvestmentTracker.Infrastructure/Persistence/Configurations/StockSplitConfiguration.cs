using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class StockSplitConfiguration : IEntityTypeConfiguration<StockSplit>
{
    public void Configure(EntityTypeBuilder<StockSplit> builder)
    {
        builder.ToTable("stock_splits");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Symbol)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(s => s.Market)
            .IsRequired();

        builder.Property(s => s.SplitDate)
            .IsRequired();

        builder.Property(s => s.SplitRatio)
            .IsRequired()
            .HasPrecision(10, 4);

        builder.Property(s => s.Description)
            .HasMaxLength(100);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        // Unique constraint: one split per symbol/market/date
        builder.HasIndex(s => new { s.Symbol, s.Market, s.SplitDate })
            .IsUnique()
            .HasDatabaseName("IX_StockSplit_Symbol_Market_SplitDate");

        // Index for efficient lookup by symbol/market
        builder.HasIndex(s => new { s.Symbol, s.Market })
            .HasDatabaseName("IX_StockSplit_Symbol_Market");
    }
}
