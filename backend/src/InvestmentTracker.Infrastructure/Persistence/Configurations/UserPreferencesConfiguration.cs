using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> builder)
    {
        builder.ToTable("user_preferences");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.UserId)
            .IsRequired();

        builder.Property(p => p.YtdBenchmarkPreferences)
            .HasMaxLength(1000);

        builder.Property(p => p.CapeRegionPreferences)
            .HasMaxLength(1000);

        builder.Property(p => p.DefaultPortfolioId);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .IsRequired();

        // Unique constraint: one preferences record per user
        builder.HasIndex(p => p.UserId)
            .IsUnique()
            .HasDatabaseName("IX_UserPreferences_UserId");

        // Relationships
        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.DefaultPortfolio)
            .WithMany()
            .HasForeignKey(p => p.DefaultPortfolioId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
