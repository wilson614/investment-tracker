using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class TransactionPortfolioSnapshotConfiguration : IEntityTypeConfiguration<TransactionPortfolioSnapshot>
{
    public void Configure(EntityTypeBuilder<TransactionPortfolioSnapshot> builder)
    {
        builder.ToTable("transaction_portfolio_snapshots");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.PortfolioId)
            .IsRequired();

        builder.Property(s => s.TransactionId)
            .IsRequired();

        builder.Property(s => s.SnapshotDate)
            .IsRequired();

        builder.Property(s => s.PortfolioValueBeforeHome)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(s => s.PortfolioValueAfterHome)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(s => s.PortfolioValueBeforeSource)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(s => s.PortfolioValueAfterSource)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        builder.HasIndex(s => new { s.PortfolioId, s.TransactionId })
            .IsUnique()
            .HasDatabaseName("IX_TransactionPortfolioSnapshots_PortfolioId_TransactionId");

        builder.HasIndex(s => new { s.PortfolioId, s.SnapshotDate })
            .HasDatabaseName("IX_TransactionPortfolioSnapshots_PortfolioId_SnapshotDate");

        builder.HasOne(s => s.Portfolio)
            .WithMany()
            .HasForeignKey(s => s.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
