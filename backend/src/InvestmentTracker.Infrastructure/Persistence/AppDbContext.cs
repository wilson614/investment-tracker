using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core database context for the Investment Tracker application.
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

        // Apply all configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global query filters for multi-tenancy
        ConfigureQueryFilters(modelBuilder);
    }

    private void ConfigureQueryFilters(ModelBuilder modelBuilder)
    {
        // Portfolio filter by user
        modelBuilder.Entity<Portfolio>()
            .HasQueryFilter(p => p.IsActive &&
                (_currentUserService == null || p.UserId == _currentUserService.UserId));

        // CurrencyLedger filter by user
        modelBuilder.Entity<CurrencyLedger>()
            .HasQueryFilter(cl => cl.IsActive &&
                (_currentUserService == null || cl.UserId == _currentUserService.UserId));

        // StockTransaction soft delete filter
        modelBuilder.Entity<StockTransaction>()
            .HasQueryFilter(st => !st.IsDeleted);

        // CurrencyTransaction soft delete filter
        modelBuilder.Entity<CurrencyTransaction>()
            .HasQueryFilter(ct => !ct.IsDeleted);

        // User active filter
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
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
