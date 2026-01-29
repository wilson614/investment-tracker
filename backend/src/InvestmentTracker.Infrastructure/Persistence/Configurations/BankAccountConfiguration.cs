using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.ToTable("bank_accounts");

        builder.HasKey(ba => ba.Id);

        builder.Property(ba => ba.BankName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ba => ba.TotalAssets)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(ba => ba.InterestRate)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(ba => ba.InterestCap)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(ba => ba.Note)
            .HasMaxLength(500);

        builder.Property(ba => ba.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(ba => ba.CreatedAt)
            .IsRequired();

        builder.Property(ba => ba.UpdatedAt)
            .IsRequired();

        // Indexes
        builder.HasIndex(ba => ba.UserId)
            .HasDatabaseName("IX_BankAccount_UserId");
    }
}
