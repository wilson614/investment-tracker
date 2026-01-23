using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class MonthlyNetWorthSnapshotConfiguration : IEntityTypeConfiguration<MonthlyNetWorthSnapshot>
{
    public void Configure(EntityTypeBuilder<MonthlyNetWorthSnapshot> builder)
    {
        builder.ToTable("monthly_net_worth_snapshots");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.PortfolioId)
            .IsRequired();

        // Store DateOnly as date column
        builder.Property(s => s.Month)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(s => s.TotalValueHome)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(s => s.TotalContributions)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(s => s.DataSource)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(s => s.CalculatedAt)
            .IsRequired();

        builder.Property(s => s.PositionDetails)
            .HasColumnType("text");

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        // Unique constraint: one snapshot per portfolio per month
        builder.HasIndex(s => new { s.PortfolioId, s.Month })
            .IsUnique()
            .HasDatabaseName("IX_MonthlyNetWorthSnapshots_PortfolioId_Month");

        // Relationships
        builder.HasOne(s => s.Portfolio)
            .WithMany()
            .HasForeignKey(s => s.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
