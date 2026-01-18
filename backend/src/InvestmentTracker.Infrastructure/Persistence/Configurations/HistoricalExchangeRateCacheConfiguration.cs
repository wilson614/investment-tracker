using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class HistoricalExchangeRateCacheConfiguration : IEntityTypeConfiguration<HistoricalExchangeRateCache>
{
    public void Configure(EntityTypeBuilder<HistoricalExchangeRateCache> builder)
    {
        builder.ToTable("historical_exchange_rate_cache");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        builder.Property(e => e.CurrencyPair)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.RequestedDate)
            .IsRequired();

        builder.Property(e => e.Rate)
            .IsRequired()
            .HasPrecision(18, 6);

        builder.Property(e => e.ActualDate)
            .IsRequired();

        builder.Property(e => e.Source)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.FetchedAt)
            .IsRequired();

        // Unique index: one cache entry per currency pair + requested date
        builder.HasIndex(e => new { e.CurrencyPair, e.RequestedDate })
            .IsUnique()
            .HasDatabaseName("IX_HistoricalExchangeRateCache_CurrencyPair_RequestedDate");
    }
}
