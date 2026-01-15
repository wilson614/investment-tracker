using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class EtfClassificationConfiguration : IEntityTypeConfiguration<EtfClassification>
{
    public void Configure(EntityTypeBuilder<EtfClassification> builder)
    {
        builder.ToTable("etf_classifications");

        builder.HasKey(e => new { e.Symbol, e.Market });

        builder.Property(e => e.Symbol)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.Market)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.Type)
            .IsRequired()
            .HasDefaultValue(EtfType.Unknown);

        builder.Property(e => e.UpdatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedByUserId);
    }
}
