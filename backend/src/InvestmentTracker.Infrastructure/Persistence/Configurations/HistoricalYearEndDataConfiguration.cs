using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

public class HistoricalYearEndDataConfiguration : IEntityTypeConfiguration<HistoricalYearEndData>
{
    public void Configure(EntityTypeBuilder<HistoricalYearEndData> builder)
    {
        builder.ToTable("historical_year_end_data");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        builder.Property(e => e.DataType)
            .IsRequired();

        builder.Property(e => e.Ticker)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.Year)
            .IsRequired();

        builder.Property(e => e.Value)
            .IsRequired()
            .HasPrecision(18, 6);

        builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(e => e.ActualDate)
            .IsRequired();

        builder.Property(e => e.Source)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.FetchedAt)
            .IsRequired();

        // Unique index to prevent duplicate entries for same ticker/year/datatype
        builder.HasIndex(e => new { e.DataType, e.Ticker, e.Year })
            .IsUnique()
            .HasDatabaseName("IX_HistoricalYearEndData_DataType_Ticker_Year");
    }
}
