using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class EuronextQuoteCacheConfiguration : IEntityTypeConfiguration<EuronextQuoteCache>
{
    public void Configure(EntityTypeBuilder<EuronextQuoteCache> builder)
    {
        builder.ToTable("euronext_quote_cache");

        builder.HasKey(e => new { e.Isin, e.Mic });

        builder.Property(e => e.Isin)
            .IsRequired()
            .HasMaxLength(12);

        builder.Property(e => e.Mic)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.Price)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(e => e.FetchedAt)
            .IsRequired();

        builder.Property(e => e.MarketTime);

        builder.Property(e => e.IsStale)
            .IsRequired()
            .HasDefaultValue(false);
    }
}
