using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class EuronextSymbolMappingConfiguration : IEntityTypeConfiguration<EuronextSymbolMapping>
{
    public void Configure(EntityTypeBuilder<EuronextSymbolMapping> builder)
    {
        builder.ToTable("euronext_symbol_mappings");

        builder.HasKey(e => e.Ticker);

        builder.Property(e => e.Ticker)
            .HasColumnName("ticker")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.Isin)
            .HasColumnName("isin")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.Mic)
            .HasColumnName("mic")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(200);

        builder.Property(e => e.Currency)
            .HasColumnName("currency")
            .HasMaxLength(5)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // Index on ISIN+MIC for quote lookups
        builder.HasIndex(e => new { e.Isin, e.Mic })
            .HasDatabaseName("ix_euronext_symbol_mappings_isin_mic");
    }
}
