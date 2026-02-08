using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class FixedDepositConfiguration : IEntityTypeConfiguration<FixedDeposit>
{
    public void Configure(EntityTypeBuilder<FixedDeposit> builder)
    {
        builder.ToTable("fixed_deposits");

        builder.HasKey(fd => fd.Id);

        builder.Property(fd => fd.UserId)
            .IsRequired();

        builder.Property(fd => fd.BankAccountId)
            .IsRequired();

        builder.Property(fd => fd.Principal)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(fd => fd.AnnualInterestRate)
            .IsRequired()
            .HasPrecision(5, 2);

        builder.Property(fd => fd.TermMonths)
            .IsRequired();

        builder.Property(fd => fd.StartDate)
            .IsRequired();

        builder.Property(fd => fd.MaturityDate)
            .IsRequired();

        builder.Property(fd => fd.ExpectedInterest)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(fd => fd.ActualInterest)
            .IsRequired(false)
            .HasPrecision(18, 2);

        builder.Property(fd => fd.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(fd => fd.Status)
            .IsRequired();

        builder.Property(fd => fd.Note)
            .IsRequired(false)
            .HasMaxLength(500);

        builder.Property(fd => fd.CreatedAt)
            .IsRequired();

        builder.Property(fd => fd.UpdatedAt)
            .IsRequired();

        builder.HasIndex(fd => fd.UserId)
            .HasDatabaseName("IX_FixedDeposit_UserId");

        builder.HasIndex(fd => fd.BankAccountId)
            .HasDatabaseName("IX_FixedDeposit_BankAccountId");

        builder.HasOne(fd => fd.BankAccount)
            .WithMany()
            .HasForeignKey(fd => fd.BankAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
