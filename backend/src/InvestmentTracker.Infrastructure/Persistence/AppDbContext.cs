using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Persistence;

/// <summary>
/// 投資追蹤應用程式的 Entity Framework Core 資料庫上下文
/// </summary>
public class AppDbContext : DbContext
{
    private readonly ICurrentUserService? _currentUserService;

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<CurrencyLedger> CurrencyLedgers => Set<CurrencyLedger>();
    public DbSet<CurrencyTransaction> CurrencyTransactions => Set<CurrencyTransaction>();
    public DbSet<CapeDataSnapshot> CapeDataSnapshots => Set<CapeDataSnapshot>();
    public DbSet<IndexPriceSnapshot> IndexPriceSnapshots => Set<IndexPriceSnapshot>();
    public DbSet<StockSplit> StockSplits => Set<StockSplit>();
    public DbSet<EuronextQuoteCache> EuronextQuoteCaches => Set<EuronextQuoteCache>();
    public DbSet<EtfClassification> EtfClassifications => Set<EtfClassification>();
    public DbSet<HistoricalYearEndData> HistoricalYearEndData => Set<HistoricalYearEndData>();
    public DbSet<HistoricalExchangeRateCache> HistoricalExchangeRateCaches => Set<HistoricalExchangeRateCache>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUserService currentUserService)
        : base(options)
    {
        _currentUserService = currentUserService;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 從組件載入所有 Entity Configuration
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // 設定多租戶 Query Filter
        ConfigureQueryFilters(modelBuilder);
    }

    private void ConfigureQueryFilters(ModelBuilder modelBuilder)
    {
        // Portfolio：依使用者過濾 + 啟用狀態
        modelBuilder.Entity<Portfolio>()
            .HasQueryFilter(p => p.IsActive &&
                (_currentUserService == null || p.UserId == _currentUserService.UserId));

        // CurrencyLedger：依使用者過濾 + 啟用狀態
        modelBuilder.Entity<CurrencyLedger>()
            .HasQueryFilter(cl => cl.IsActive &&
                (_currentUserService == null || cl.UserId == _currentUserService.UserId));

        // StockTransaction：軟刪除過濾
        modelBuilder.Entity<StockTransaction>()
            .HasQueryFilter(st => !st.IsDeleted);

        // CurrencyTransaction：軟刪除過濾
        modelBuilder.Entity<CurrencyTransaction>()
            .HasQueryFilter(ct => !ct.IsDeleted);

        // User：啟用狀態過濾
        modelBuilder.Entity<User>()
            .HasQueryFilter(u => u.IsActive);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    private void UpdateTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IHasTimestamps>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
