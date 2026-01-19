using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvestmentTracker.Infrastructure.Persistence.Configurations;

/// <summary>
/// 月底指數價格快照的資料庫配置
/// </summary>
public class IndexPriceSnapshotConfiguration : IEntityTypeConfiguration<IndexPriceSnapshot>
{
    public void Configure(EntityTypeBuilder<IndexPriceSnapshot> builder)
    {
        builder.ToTable("index_price_snapshots");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.MarketKey)
            .IsRequired();

        builder.Property(s => s.YearMonth)
            .IsRequired();

        builder.Property(s => s.Price);

        builder.Property(s => s.RecordedAt)
            .IsRequired();

        builder.Property(s => s.IsNotAvailable)
            .IsRequired();

        // 依 MarketKey + YearMonth 建立唯一索引
        builder.HasIndex(s => new { s.MarketKey, s.YearMonth })
            .IsUnique();
    }
}
