using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class CreditCardConfiguration : IEntityTypeConfiguration<CreditCard>
{
    public void Configure(EntityTypeBuilder<CreditCard> builder)
    {
        builder.ToTable("credit_cards");

        builder.HasKey(cc => cc.Id);

        builder.Property(cc => cc.UserId)
            .IsRequired();

        builder.Property(cc => cc.BankName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cc => cc.CardName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cc => cc.BillingCycleDay)
            .IsRequired();

        builder.Property(cc => cc.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(cc => cc.Note)
            .IsRequired(false)
            .HasMaxLength(500);

        builder.Property(cc => cc.CreatedAt)
            .IsRequired();

        builder.Property(cc => cc.UpdatedAt)
            .IsRequired();

        builder.HasIndex(cc => cc.UserId)
            .HasDatabaseName("IX_CreditCard_UserId");

        builder.HasMany<Installment>()
            .WithOne()
            .HasForeignKey(i => i.CreditCardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
