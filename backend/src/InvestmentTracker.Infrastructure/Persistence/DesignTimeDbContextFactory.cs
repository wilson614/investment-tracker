using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InvestmentTracker.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for generating PostgreSQL-compatible migrations.
/// This ensures migrations work correctly in production (PostgreSQL) environment.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Use PostgreSQL for migration generation to ensure compatibility with production
        optionsBuilder.UseNpgsql("Host=localhost;Database=investmenttracker_design;Username=postgres;Password=postgres");

        return new AppDbContext(optionsBuilder.Options);
    }
}
