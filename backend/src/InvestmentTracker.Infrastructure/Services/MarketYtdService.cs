using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Infrastructure.Persistence;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// Service for calculating Market YTD benchmark returns
/// Uses IndexPriceSnapshot table with YearMonth format YYYY01 for Jan 1 prices
/// </summary>
public class MarketYtdService : IMarketYtdService
{
    private readonly AppDbContext _dbContext;
    private readonly IStockPriceService _stockPriceService;
    private readonly ILogger<MarketYtdService> _logger;

    // Benchmark definitions: MarketKey -> (Symbol, Name, Market)
    private static readonly Dictionary<string, (string Symbol, string Name, StockMarket Market)> Benchmarks = new()
    {
        ["All Country"] = ("VWRA", "Vanguard FTSE All-World", StockMarket.UK),
        ["US Large"] = ("VUAA", "Vanguard S&P 500", StockMarket.UK),
        ["Taiwan"] = ("0050", "元大台灣50", StockMarket.TW),
        ["Emerging Markets"] = ("VFEM", "Vanguard FTSE Emerging Markets", StockMarket.UK),
    };

    public MarketYtdService(
        AppDbContext dbContext,
        IStockPriceService stockPriceService,
        ILogger<MarketYtdService> logger)
    {
        _dbContext = dbContext;
        _stockPriceService = stockPriceService;
        _logger = logger;
    }

    public static IReadOnlyDictionary<string, (string Symbol, string Name, StockMarket Market)> SupportedBenchmarks => Benchmarks;

    public async Task<MarketYtdComparisonDto> GetYtdComparisonAsync(CancellationToken cancellationToken = default)
    {
        var year = DateTime.UtcNow.Year;
        var jan1YearMonth = $"{year}01";

        // Load Jan 1 reference prices from database
        var jan1Prices = await _dbContext.IndexPriceSnapshots
            .Where(s => s.YearMonth == jan1YearMonth && Benchmarks.Keys.Contains(s.MarketKey))
            .ToDictionaryAsync(s => s.MarketKey, s => s.Price, cancellationToken);

        // Fetch current prices for all benchmarks
        var benchmarkResults = new List<MarketYtdReturnDto>();

        foreach (var (marketKey, benchmark) in Benchmarks)
        {
            var result = await GetBenchmarkYtdAsync(marketKey, benchmark, jan1Prices, cancellationToken);
            benchmarkResults.Add(result);
        }

        return new MarketYtdComparisonDto
        {
            Year = year,
            Benchmarks = benchmarkResults,
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<MarketYtdComparisonDto> RefreshYtdComparisonAsync(CancellationToken cancellationToken = default)
    {
        var year = DateTime.UtcNow.Year;
        var jan1YearMonth = $"{year}01";

        // Check if we have Jan 1 prices, if not try to seed them with current prices
        // (This is a fallback - ideally Jan 1 prices should be captured on Jan 1)
        var existingPrices = await _dbContext.IndexPriceSnapshots
            .Where(s => s.YearMonth == jan1YearMonth && Benchmarks.Keys.Contains(s.MarketKey))
            .Select(s => s.MarketKey)
            .ToListAsync(cancellationToken);

        foreach (var (marketKey, benchmark) in Benchmarks)
        {
            if (!existingPrices.Contains(marketKey))
            {
                try
                {
                    var quote = await _stockPriceService.GetQuoteAsync(benchmark.Market, benchmark.Symbol, cancellationToken);
                    if (quote != null)
                    {
                        // Use current price as Jan 1 reference (this will show ~0% YTD initially)
                        // In production, this should be populated with actual Jan 1 prices
                        _logger.LogInformation("Seeding Jan 1 price for {MarketKey} with current price {Price}", marketKey, quote.Price);
                        await StoreJan1PriceAsync(marketKey, quote.Price, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to seed Jan 1 price for {MarketKey}", marketKey);
                }
            }
        }

        return await GetYtdComparisonAsync(cancellationToken);
    }

    public async Task StoreJan1PriceAsync(string marketKey, decimal price, CancellationToken cancellationToken = default)
    {
        if (!Benchmarks.ContainsKey(marketKey))
        {
            throw new ArgumentException($"Unknown market key: {marketKey}");
        }

        var year = DateTime.UtcNow.Year;
        var yearMonth = $"{year}01";

        var existing = await _dbContext.IndexPriceSnapshots
            .FirstOrDefaultAsync(s => s.MarketKey == marketKey && s.YearMonth == yearMonth, cancellationToken);

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
        _logger.LogInformation("Stored Jan 1 price for {MarketKey}: {Price}", marketKey, price);
    }

    private async Task<MarketYtdReturnDto> GetBenchmarkYtdAsync(
        string marketKey,
        (string Symbol, string Name, StockMarket Market) benchmark,
        Dictionary<string, decimal> jan1Prices,
        CancellationToken cancellationToken)
    {
        var hasJan1Price = jan1Prices.TryGetValue(marketKey, out var jan1Price);

        try
        {
            var quote = await _stockPriceService.GetQuoteAsync(benchmark.Market, benchmark.Symbol, cancellationToken);

            if (quote == null)
            {
                return new MarketYtdReturnDto
                {
                    MarketKey = marketKey,
                    Symbol = benchmark.Symbol,
                    Name = benchmark.Name,
                    Jan1Price = hasJan1Price ? jan1Price : null,
                    CurrentPrice = null,
                    YtdReturnPercent = null,
                    Error = "Unable to fetch current price"
                };
            }

            decimal? ytdReturn = null;
            if (hasJan1Price && jan1Price > 0)
            {
                // YTD = ((Current - Jan1) / Jan1) * 100
                ytdReturn = ((quote.Price - jan1Price) / jan1Price) * 100;
            }

            return new MarketYtdReturnDto
            {
                MarketKey = marketKey,
                Symbol = benchmark.Symbol,
                Name = benchmark.Name,
                Jan1Price = hasJan1Price ? jan1Price : null,
                CurrentPrice = quote.Price,
                YtdReturnPercent = ytdReturn,
                FetchedAt = quote.FetchedAt,
                Error = hasJan1Price ? null : "Missing Jan 1 reference price"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get YTD for {MarketKey} ({Symbol})", marketKey, benchmark.Symbol);
            return new MarketYtdReturnDto
            {
                MarketKey = marketKey,
                Symbol = benchmark.Symbol,
                Name = benchmark.Name,
                Jan1Price = hasJan1Price ? jan1Price : null,
                Error = $"Error: {ex.Message}"
            };
        }
    }
}
