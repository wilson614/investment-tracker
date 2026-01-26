using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class BenchmarkAnnualReturnConfiguration : IEntityTypeConfiguration<BenchmarkAnnualReturn>
{
    public void Configure(EntityTypeBuilder<BenchmarkAnnualReturn> builder)
    {
        builder.ToTable("benchmark_annual_returns");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Symbol)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(b => b.Year)
            .IsRequired();

        builder.Property(b => b.TotalReturnPercent)
            .IsRequired()
            .HasPrecision(10, 4);

        builder.Property(b => b.PriceReturnPercent)
            .HasPrecision(10, 4);

        builder.Property(b => b.DataSource)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(b => b.FetchedAt)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .IsRequired();

        builder.HasIndex(b => new { b.Symbol, b.Year })
            .IsUnique()
            .HasDatabaseName("IX_BenchmarkAnnualReturns_Symbol_Year");
    }
}
