using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class InstallmentConfiguration : IEntityTypeConfiguration<Installment>
{
    public void Configure(EntityTypeBuilder<Installment> builder)
    {
        builder.ToTable("installments");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.CreditCardId)
            .IsRequired();

        builder.Property(i => i.UserId)
            .IsRequired();

        builder.Property(i => i.Description)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(i => i.TotalAmount)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(i => i.NumberOfInstallments)
            .IsRequired();

        builder.Property(i => i.RemainingInstallments)
            .IsRequired();

        builder.Property(i => i.MonthlyPayment)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(i => i.StartDate)
            .IsRequired();

        builder.Property(i => i.Status)
            .IsRequired();

        builder.Property(i => i.Note)
            .IsRequired(false)
            .HasMaxLength(500);

        builder.Property(i => i.CreatedAt)
            .IsRequired();

        builder.Property(i => i.UpdatedAt)
            .IsRequired();

        builder.HasIndex(i => i.CreditCardId)
            .HasDatabaseName("IX_Installment_CreditCardId");

        builder.HasIndex(i => i.UserId)
            .HasDatabaseName("IX_Installment_UserId");

        builder.HasOne<CreditCard>()
            .WithMany()
            .HasForeignKey(i => i.CreditCardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
