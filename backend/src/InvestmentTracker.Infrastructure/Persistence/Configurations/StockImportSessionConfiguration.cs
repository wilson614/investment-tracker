using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class StockImportSessionConfiguration : IEntityTypeConfiguration<StockImportSession>
{
    public void Configure(EntityTypeBuilder<StockImportSession> builder)
    {
        builder.ToTable("stock_import_sessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.SessionId)
            .IsRequired();

        builder.Property(s => s.UserId)
            .IsRequired();

        builder.Property(s => s.PortfolioId)
            .IsRequired();

        builder.Property(s => s.ExecutionStatus)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(s => s.SessionSnapshotJson)
            .IsRequired();

        builder.Property(s => s.ExecutionResultJson)
            .IsRequired(false);

        builder.Property(s => s.Message)
            .HasMaxLength(500);

        builder.Property(s => s.StartedAtUtc)
            .IsRequired(false);

        builder.Property(s => s.CompletedAtUtc)
            .IsRequired(false);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        builder.HasIndex(s => s.SessionId)
            .IsUnique()
            .HasDatabaseName("IX_StockImportSession_SessionId");

        builder.HasIndex(s => s.UserId)
            .HasDatabaseName("IX_StockImportSession_UserId");

        builder.HasIndex(s => s.PortfolioId)
            .HasDatabaseName("IX_StockImportSession_PortfolioId");

        builder.HasOne<Portfolio>()
            .WithMany()
            .HasForeignKey(s => s.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
