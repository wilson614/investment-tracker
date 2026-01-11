using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// Service for fetching market index/ETF prices for CAPE adjustment
/// - US/Global markets: Sina for real-time, Stooq for historical
/// - Taiwan: TWSE for both real-time and historical
/// Falls back to database for historical prices if external sources unavailable
/// </summary>
public class IndexPriceService : IIndexPriceService
{
    private readonly ISinaEtfPriceService _sinaEtfPriceService;
    private readonly IStooqHistoricalPriceService _stooqHistoricalPriceService;
    private readonly ITwseIndexPriceService _twseIndexPriceService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<IndexPriceService> _logger;

    // All supported markets for CAPE adjustment
    public static readonly IReadOnlyCollection<string> SupportedMarkets = new[]
    {
        "All Country",              // VWRA - Vanguard FTSE All-World
        "US Large",                 // VUAA - Vanguard S&P 500
        "US Small",                 // XRSU - Xtrackers Russell 2000
        "Taiwan",                   // TWII - Taiwan Weighted Index
        "Emerging Markets",         // VFEM - Vanguard FTSE Emerging Markets
        "Europe",                   // VEUA - Vanguard FTSE Developed Europe
        "Japan",                    // VJPA - Vanguard FTSE Japan
        "China",                    // HCHA - HSBC MSCI China
        "Developed Markets Large",  // VHVE - Vanguard FTSE Developed World
        "Developed Markets Small",  // WSML - iShares MSCI World Small Cap
        "Dev ex US Large",          // EXUS - Vanguard FTSE Developed ex US
    };

    public IndexPriceService(
        ISinaEtfPriceService sinaEtfPriceService,
        IStooqHistoricalPriceService stooqHistoricalPriceService,
        ITwseIndexPriceService twseIndexPriceService,
        AppDbContext dbContext,
        ILogger<IndexPriceService> logger)
    {
        _sinaEtfPriceService = sinaEtfPriceService;
        _stooqHistoricalPriceService = stooqHistoricalPriceService;
        _twseIndexPriceService = twseIndexPriceService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IndexPriceData?> GetIndexPricesAsync(
        string marketKey,
        DateTime referenceDate,
        CancellationToken cancellationToken = default)
    {
        // Check if this market is supported
        if (!SupportedMarkets.Contains(marketKey))
        {
            _logger.LogDebug("Market {Market} is not supported for CAPE adjustment", marketKey);
            return null;
        }

        try
        {
            decimal? currentPrice;
            decimal? referencePrice;
            var referenceYearMonth = GetReferenceYearMonth(referenceDate);

            if (marketKey == "Taiwan")
            {
                // Taiwan uses TWSE for both real-time and historical
                // Use fallback method that tries historical data if real-time fails
                currentPrice = await _twseIndexPriceService.GetCurrentPriceWithFallbackAsync(cancellationToken);
                referencePrice = await GetTaiwanReferencePriceAsync(referenceDate, referenceYearMonth, cancellationToken);
            }
            else
            {
                // US/Global markets use Sina + Stooq
                currentPrice = await _sinaEtfPriceService.GetCurrentPriceAsync(marketKey, cancellationToken);
                referencePrice = await GetReferencePriceAsync(marketKey, referenceDate, referenceYearMonth, cancellationToken);
            }

            if (currentPrice == null)
            {
                _logger.LogWarning("Failed to get current price for {Market}", marketKey);
                return null;
            }

            if (referencePrice == null)
            {
                _logger.LogWarning(
                    "No reference price found for {Market} at {YearMonth}.",
                    marketKey, referenceYearMonth);
                return null;
            }

            _logger.LogDebug(
                "Got prices for {Market}: current={Current}, reference={Reference} ({YearMonth})",
                marketKey, currentPrice, referencePrice, referenceYearMonth);

            return new IndexPriceData(
                marketKey,
                marketKey,
                currentPrice.Value,
                referencePrice.Value,
                referenceDate,
                DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching index prices for {Market}", marketKey);
            return null;
        }
    }

    private static string GetReferenceYearMonth(DateTime referenceDate)
    {
        return $"{referenceDate.Year}{referenceDate.Month:D2}";
    }

    /// <summary>
    /// Get Taiwan reference price - try TWSE first, then fallback to database
    /// </summary>
    private async Task<decimal?> GetTaiwanReferencePriceAsync(
        DateTime referenceDate,
        string yearMonth,
        CancellationToken cancellationToken)
    {
        // First check database for cached/manual entry
        var dbPrice = await GetDatabasePriceAsync("Taiwan", yearMonth, cancellationToken);
        if (dbPrice != null)
        {
            return dbPrice;
        }

        // Try to fetch from TWSE
        var twsePrice = await _twseIndexPriceService.GetMonthEndPriceAsync(
            referenceDate.Year,
            referenceDate.Month,
            cancellationToken);

        if (twsePrice != null)
        {
            // Cache in database for future use
            await SavePriceToDatabase("Taiwan", yearMonth, twsePrice.Value, cancellationToken);
            return twsePrice;
        }

        return null;
    }

    /// <summary>
    /// Get reference price for US/Global markets - try Stooq first, then fallback to database
    /// </summary>
    private async Task<decimal?> GetReferencePriceAsync(
        string marketKey,
        DateTime referenceDate,
        string yearMonth,
        CancellationToken cancellationToken)
    {
        // First check database for cached/manual entry
        var dbPrice = await GetDatabasePriceAsync(marketKey, yearMonth, cancellationToken);
        if (dbPrice != null)
        {
            return dbPrice;
        }

        // Try to fetch from Stooq
        var stooqPrice = await _stooqHistoricalPriceService.GetMonthEndPriceAsync(
            marketKey,
            referenceDate.Year,
            referenceDate.Month,
            cancellationToken);

        if (stooqPrice != null)
        {
            // Cache in database for future use
            await SavePriceToDatabase(marketKey, yearMonth, stooqPrice.Value, cancellationToken);
            return stooqPrice;
        }

        return null;
    }

    private async Task<decimal?> GetDatabasePriceAsync(
        string marketKey,
        string yearMonth,
        CancellationToken cancellationToken)
    {
        var snapshot = await _dbContext.IndexPriceSnapshots
            .FirstOrDefaultAsync(
                s => s.MarketKey == marketKey && s.YearMonth == yearMonth,
                cancellationToken);

        return snapshot?.Price;
    }

    private async Task SavePriceToDatabase(
        string marketKey,
        string yearMonth,
        decimal price,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _dbContext.IndexPriceSnapshots
                .FirstOrDefaultAsync(
                    s => s.MarketKey == marketKey && s.YearMonth == yearMonth,
                    cancellationToken);

            if (existing != null)
            {
                existing.Price = price;
                existing.RecordedAt = DateTime.UtcNow;
            }
            else
            {
                _dbContext.IndexPriceSnapshots.Add(new IndexPriceSnapshot
                {
                    MarketKey = marketKey,
                    YearMonth = yearMonth,
                    Price = price,
                    RecordedAt = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Cached reference price {Price} for {Market} {YearMonth}", price, marketKey, yearMonth);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            // Another concurrent request already inserted this record - that's fine
            _logger.LogDebug("Duplicate key ignored for {Market} {YearMonth} - already exists", marketKey, yearMonth);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache reference price for {Market} {YearMonth}", marketKey, yearMonth);
        }
    }

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        // PostgreSQL unique constraint violation: 23505
        // SQLite constraint violation: 19 (SQLITE_CONSTRAINT)
        return ex.InnerException?.Message?.Contains("23505") == true ||
               ex.InnerException?.Message?.Contains("UNIQUE constraint failed") == true;
    }
}
