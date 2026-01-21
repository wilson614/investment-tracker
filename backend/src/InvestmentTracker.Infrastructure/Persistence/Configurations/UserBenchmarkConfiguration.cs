using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class UserBenchmarkConfiguration : IEntityTypeConfiguration<UserBenchmark>
{
    public void Configure(EntityTypeBuilder<UserBenchmark> builder)
    {
        builder.ToTable("user_benchmarks");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.UserId)
            .IsRequired();

        builder.Property(b => b.Ticker)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(b => b.Market)
            .IsRequired();

        builder.Property(b => b.DisplayName)
            .HasMaxLength(100);

        builder.Property(b => b.CreatedAt)
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .IsRequired();

        // Unique constraint: one benchmark per user/ticker/market
        builder.HasIndex(b => new { b.UserId, b.Ticker, b.Market })
            .IsUnique()
            .HasDatabaseName("IX_UserBenchmark_UserId_Ticker_Market");

        // Index for efficient lookup by user
        builder.HasIndex(b => b.UserId)
            .HasDatabaseName("IX_UserBenchmark_UserId");

        // Relationships
        builder.HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
