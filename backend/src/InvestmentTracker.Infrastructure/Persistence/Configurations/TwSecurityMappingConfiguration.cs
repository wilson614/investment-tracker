using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class TwSecurityMappingConfiguration : IEntityTypeConfiguration<TwSecurityMapping>
{
    public void Configure(EntityTypeBuilder<TwSecurityMapping> builder)
    {
        builder.ToTable("tw_security_mappings");

        builder.HasKey(e => e.Ticker);

        builder.Property(e => e.Ticker)
            .HasColumnName("ticker")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.SecurityName)
            .HasColumnName("security_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Isin)
            .HasColumnName("isin")
            .HasMaxLength(20);

        builder.Property(e => e.Market)
            .HasColumnName("market")
            .HasMaxLength(10);

        builder.Property(e => e.Currency)
            .HasColumnName("currency")
            .HasMaxLength(5);

        builder.Property(e => e.Source)
            .HasColumnName("source")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.LastSyncedAt)
            .HasColumnName("last_synced_at")
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // Index for name-based resolution in import pipeline
        builder.HasIndex(e => e.SecurityName)
            .HasDatabaseName("ix_tw_security_mappings_security_name");
    }
}
