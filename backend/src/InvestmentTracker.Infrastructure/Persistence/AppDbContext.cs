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
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<CapeDataSnapshot> CapeDataSnapshots => Set<CapeDataSnapshot>();
    public DbSet<IndexPriceSnapshot> IndexPriceSnapshots => Set<IndexPriceSnapshot>();
    public DbSet<StockSplit> StockSplits => Set<StockSplit>();
    public DbSet<EuronextSymbolMapping> EuronextSymbolMappings => Set<EuronextSymbolMapping>();
    public DbSet<EtfClassification> EtfClassifications => Set<EtfClassification>();
    public DbSet<HistoricalYearEndData> HistoricalYearEndData => Set<HistoricalYearEndData>();
    public DbSet<HistoricalExchangeRateCache> HistoricalExchangeRateCaches => Set<HistoricalExchangeRateCache>();
    public DbSet<UserBenchmark> UserBenchmarks => Set<UserBenchmark>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<FundAllocation> FundAllocations => Set<FundAllocation>();
    public DbSet<MonthlyNetWorthSnapshot> MonthlyNetWorthSnapshots => Set<MonthlyNetWorthSnapshot>();
    public DbSet<TransactionPortfolioSnapshot> TransactionPortfolioSnapshots => Set<TransactionPortfolioSnapshot>();
    public DbSet<BenchmarkAnnualReturn> BenchmarkAnnualReturns => Set<BenchmarkAnnualReturn>();

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

        // MonthlyNetWorthSnapshot：透過 Portfolio 導覽屬性套用同樣的 user filter
        modelBuilder.Entity<MonthlyNetWorthSnapshot>()
            .HasQueryFilter(s => s.Portfolio.IsActive &&
                (_currentUserService == null || s.Portfolio.UserId == _currentUserService.UserId));

        // TransactionPortfolioSnapshot：透過 Portfolio 導覽屬性套用同樣的 user filter
        modelBuilder.Entity<TransactionPortfolioSnapshot>()
            .HasQueryFilter(s => s.Portfolio.IsActive &&
                (_currentUserService == null || s.Portfolio.UserId == _currentUserService.UserId));

        // CurrencyLedger：依使用者過濾 + 啟用狀態
        modelBuilder.Entity<CurrencyLedger>()
            .HasQueryFilter(cl => cl.IsActive &&
                (_currentUserService == null || cl.UserId == _currentUserService.UserId));

        // BankAccount：依使用者過濾 + 啟用狀態
        modelBuilder.Entity<BankAccount>()
            .HasQueryFilter(ba => ba.IsActive &&
                (_currentUserService == null || ba.UserId == _currentUserService.UserId));

        // FundAllocation：依使用者過濾
        modelBuilder.Entity<FundAllocation>()
            .HasQueryFilter(fa => _currentUserService == null || fa.UserId == _currentUserService.UserId);

        // StockTransaction：軟刪除過濾
        modelBuilder.Entity<StockTransaction>()
            .HasQueryFilter(st => !st.IsDeleted);

        // CurrencyTransaction：軟刪除過濾
        modelBuilder.Entity<CurrencyTransaction>()
            .HasQueryFilter(ct => !ct.IsDeleted);

        // User：啟用狀態過濾
        modelBuilder.Entity<User>()
            .HasQueryFilter(u => u.IsActive);

        // UserPreferences：需與 User 使用相同的過濾條件，避免 required navigation 警告
        modelBuilder.Entity<UserPreferences>()
            .HasQueryFilter(up => up.User.IsActive);

        // RefreshToken：需與 User 使用相同的過濾條件
        modelBuilder.Entity<RefreshToken>()
            .HasQueryFilter(rt => rt.User.IsActive);

        // UserBenchmark：需與 User 使用相同的過濾條件
        modelBuilder.Entity<UserBenchmark>()
            .HasQueryFilter(ub => ub.User.IsActive);
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
