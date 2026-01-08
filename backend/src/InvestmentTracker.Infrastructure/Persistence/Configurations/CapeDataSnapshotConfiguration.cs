using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class CapeDataSnapshotConfiguration : IEntityTypeConfiguration<CapeDataSnapshot>
{
    public void Configure(EntityTypeBuilder<CapeDataSnapshot> builder)
    {
        builder.ToTable("CapeDataSnapshots");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.DataDate)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(c => c.ItemsJson)
            .IsRequired();

        builder.Property(c => c.FetchedAt)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        // Index on DataDate for quick lookups
        builder.HasIndex(c => c.DataDate)
            .IsUnique();
    }
}
