using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class FundAllocationConfiguration : IEntityTypeConfiguration<FundAllocation>
{
    public void Configure(EntityTypeBuilder<FundAllocation> builder)
    {
        builder.ToTable("fund_allocations");

        builder.HasKey(fa => fa.Id);

        builder.Property(fa => fa.UserId)
            .IsRequired();

        builder.Property(fa => fa.Purpose)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(fa => fa.Amount)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(fa => fa.IsDisposable)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(fa => fa.Note)
            .HasMaxLength(500);

        builder.Property(fa => fa.CreatedAt)
            .IsRequired();

        builder.Property(fa => fa.UpdatedAt)
            .IsRequired();

        // Unique constraint: one purpose per user
        builder.HasIndex(fa => new { fa.UserId, fa.Purpose })
            .IsUnique()
            .HasDatabaseName("IX_FundAllocation_UserId_Purpose");

        // Index for efficient lookup by user
        builder.HasIndex(fa => fa.UserId)
            .HasDatabaseName("IX_FundAllocation_UserId");

        // Relationships
        builder.HasOne(fa => fa.User)
            .WithMany()
            .HasForeignKey(fa => fa.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
