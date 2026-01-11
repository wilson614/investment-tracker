using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.Persistence;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// Service for calculating Market YTD benchmark returns
/// Uses IndexPriceSnapshot table with YearMonth format YYYY01 for Jan 1 prices
/// Auto-fetches previous year's year-end prices from Stooq/TWSE when missing
/// </summary>
public class MarketYtdService : IMarketYtdService
{
    private readonly AppDbContext _dbContext;
    private readonly IStockPriceService _stockPriceService;
    private readonly IStooqHistoricalPriceService _stooqService;
    private readonly ITwseDividendService _dividendService;
    private readonly ITwseRateLimiter _twseRateLimiter;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MarketYtdService> _logger;

    // Benchmark definitions: MarketKey -> (Symbol, Name, Market)
    // Note: "Taiwan 0050" is different from "Taiwan" (used for CAPE with TWII index)
    private static readonly Dictionary<string, (string Symbol, string Name, StockMarket Market)> Benchmarks = new()
    {
        ["All Country"] = ("VWRA", "Vanguard FTSE All-World", StockMarket.UK),
        ["US Large"] = ("VUAA", "Vanguard S&P 500", StockMarket.UK),
        ["US Small"] = ("XRSU", "Xtrackers Russell 2000", StockMarket.UK),
        ["Developed Markets Large"] = ("VHVE", "Vanguard FTSE Developed World", StockMarket.UK),
        ["Developed Markets Small"] = ("WSML", "iShares MSCI World Small Cap", StockMarket.UK),
        ["Dev ex US Large"] = ("EXUS", "Vanguard FTSE Developed ex US", StockMarket.UK),
        ["Emerging Markets"] = ("VFEM", "Vanguard FTSE Emerging Markets", StockMarket.UK),
        ["Europe"] = ("VEUA", "Vanguard FTSE Developed Europe", StockMarket.UK),
        ["Japan"] = ("VJPA", "Vanguard FTSE Japan", StockMarket.UK),
        ["China"] = ("HCHA", "HSBC MSCI China A", StockMarket.UK),
        ["Taiwan 0050"] = ("0050", "元大台灣50", StockMarket.TW),
    };

    public MarketYtdService(
        AppDbContext dbContext,
        IStockPriceService stockPriceService,
        IStooqHistoricalPriceService stooqService,
        ITwseDividendService dividendService,
        ITwseRateLimiter twseRateLimiter,
        HttpClient httpClient,
        ILogger<MarketYtdService> logger)
    {
        _dbContext = dbContext;
        _stockPriceService = stockPriceService;
        _stooqService = stooqService;
        _dividendService = dividendService;
        _twseRateLimiter = twseRateLimiter;
        _httpClient = httpClient;
        _logger = logger;
    }

    public static IReadOnlyDictionary<string, (string Symbol, string Name, StockMarket Market)> SupportedBenchmarks => Benchmarks;

    public async Task<MarketYtdComparisonDto> GetYtdComparisonAsync(CancellationToken cancellationToken = default)
    {
        var year = DateTime.UtcNow.Year;
        var previousYear = year - 1;
        var yearEndYearMonth = $"{previousYear}12";  // Use previous year December as baseline

        // Load year-end reference prices from database (e.g., 202512 for 2026 YTD)
        // Use GroupBy to handle potential duplicates gracefully
        var yearEndPrices = await _dbContext.IndexPriceSnapshots
            .Where(s => s.YearMonth == yearEndYearMonth && Benchmarks.Keys.Contains(s.MarketKey))
            .GroupBy(s => s.MarketKey)
            .Select(g => new { MarketKey = g.Key, Price = g.First().Price })
            .ToDictionaryAsync(x => x.MarketKey, x => x.Price, cancellationToken);

        // Auto-fetch missing year-end prices from previous year
        var missingMarkets = Benchmarks.Keys.Where(k => !yearEndPrices.ContainsKey(k)).ToList();
        if (missingMarkets.Count > 0)
        {
            _logger.LogInformation("Missing {Year}/12 year-end prices for {Markets}, fetching from external sources...", previousYear, string.Join(", ", missingMarkets));
            await PopulateMissingYearEndPricesAsync(previousYear, missingMarkets, yearEndPrices, cancellationToken);
        }

        // Fetch current prices for all benchmarks
        var benchmarkResults = new List<MarketYtdReturnDto>();

        foreach (var (marketKey, benchmark) in Benchmarks)
        {
            var result = await GetBenchmarkYtdAsync(marketKey, benchmark, yearEndPrices, cancellationToken);
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
        // Simply delegate to GetYtdComparisonAsync which handles auto-fetching
        return await GetYtdComparisonAsync(cancellationToken);
    }

    private async Task<MarketYtdReturnDto> GetBenchmarkYtdAsync(
        string marketKey,
        (string Symbol, string Name, StockMarket Market) benchmark,
        Dictionary<string, decimal> yearEndPrices,
        CancellationToken cancellationToken)
    {
        var hasYearEndPrice = yearEndPrices.TryGetValue(marketKey, out var yearEndPrice);
        var year = DateTime.UtcNow.Year;

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
                    Jan1Price = hasYearEndPrice ? yearEndPrice : null,
                    CurrentPrice = null,
                    YtdReturnPercent = null,
                    Error = "Unable to fetch current price"
                };
            }

            // Fetch dividends for Taiwan stocks (0050)
            decimal dividendsPaid = 0;
            if (benchmark.Market == StockMarket.TW)
            {
                var dividends = await _dividendService.GetDividendsAsync(benchmark.Symbol, year, cancellationToken);
                // Only count dividends that have already been paid (ex-date <= today)
                var today = DateTime.UtcNow.Date;
                dividendsPaid = dividends
                    .Where(d => d.ExDividendDate.Date <= today)
                    .Sum(d => d.DividendAmount);

                if (dividendsPaid > 0)
                {
                    _logger.LogDebug("Found {Count} dividends for {Symbol} in {Year}, total: {Amount}",
                        dividends.Count, benchmark.Symbol, year, dividendsPaid);
                }
            }

            decimal? ytdPriceReturn = null;
            decimal? ytdTotalReturn = null;

            if (hasYearEndPrice && yearEndPrice > 0)
            {
                // Price return = ((Current - YearEnd) / YearEnd) * 100
                ytdPriceReturn = ((quote.Price - yearEndPrice) / yearEndPrice) * 100;

                // Total return = ((Current + Dividends - YearEnd) / YearEnd) * 100
                ytdTotalReturn = ((quote.Price + dividendsPaid - yearEndPrice) / yearEndPrice) * 100;
            }

            return new MarketYtdReturnDto
            {
                MarketKey = marketKey,
                Symbol = benchmark.Symbol,
                Name = benchmark.Name,
                Jan1Price = hasYearEndPrice ? yearEndPrice : null,
                CurrentPrice = quote.Price,
                DividendsPaid = dividendsPaid > 0 ? dividendsPaid : null,
                YtdReturnPercent = ytdPriceReturn,
                YtdTotalReturnPercent = ytdTotalReturn,
                FetchedAt = quote.FetchedAt,
                Error = hasYearEndPrice ? null : "Missing year-end reference price"
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
                Jan1Price = hasYearEndPrice ? yearEndPrice : null,
                Error = $"Error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Fetch missing year-end prices from Stooq (UK ETFs) or TWSE (Taiwan 0050)
    /// and store them in the database for future use
    /// </summary>
    private async Task PopulateMissingYearEndPricesAsync(
        int year,
        List<string> missingMarkets,
        Dictionary<string, decimal> yearEndPrices,
        CancellationToken cancellationToken)
    {
        var yearMonth = $"{year}12";  // Store as YYYYMM format (e.g., 202512)

        foreach (var marketKey in missingMarkets)
        {
            try
            {
                decimal? yearEndPrice = null;

                if (marketKey == "Taiwan 0050")
                {
                    // Fetch 0050 year-end price from TWSE
                    yearEndPrice = await FetchTwse0050YearEndPriceAsync(year, cancellationToken);
                }
                else
                {
                    // Fetch UK ETF year-end price from Stooq
                    yearEndPrice = await _stooqService.GetMonthEndPriceAsync(marketKey, year, 12, cancellationToken);
                }

                if (yearEndPrice.HasValue)
                {
                    // Check if record already exists (prevent duplicates from concurrent requests)
                    var exists = await _dbContext.IndexPriceSnapshots
                        .AnyAsync(s => s.MarketKey == marketKey && s.YearMonth == yearMonth, cancellationToken);

                    if (!exists)
                    {
                        _dbContext.IndexPriceSnapshots.Add(new IndexPriceSnapshot
                        {
                            MarketKey = marketKey,
                            YearMonth = yearMonth,
                            Price = yearEndPrice.Value,
                            RecordedAt = DateTime.UtcNow
                        });
                        _logger.LogInformation("Fetched and stored {Year}/12 year-end price for {MarketKey}: {Price}",
                            year, marketKey, yearEndPrice.Value);
                    }

                    yearEndPrices[marketKey] = yearEndPrice.Value;
                }
                else
                {
                    _logger.LogWarning("Could not fetch year-end price for {MarketKey}", marketKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch year-end price for {MarketKey}", marketKey);
            }
        }

        if (yearEndPrices.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Fetch 0050 ETF year-end closing price from TWSE
    /// </summary>
    private async Task<decimal?> FetchTwse0050YearEndPriceAsync(int year, CancellationToken cancellationToken)
    {
        try
        {
            // Wait for rate limit slot before making request
            await _twseRateLimiter.WaitForSlotAsync(cancellationToken);

            // TWSE API for individual stock historical data
            var url = $"https://www.twse.com.tw/exchangeReport/STOCK_DAY?response=json&date={year}1201&stockNo=0050";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TWSE returned {Status} for 0050 historical data", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = System.Text.Json.JsonDocument.Parse(content);

            if (!json.RootElement.TryGetProperty("data", out var dataArray) ||
                dataArray.GetArrayLength() == 0)
            {
                _logger.LogWarning("No 0050 historical data from TWSE for {Year}/12", year);
                return null;
            }

            // Get last trading day of December (last row)
            var lastRow = dataArray[dataArray.GetArrayLength() - 1];

            // Field index 6 is closing price (收盤價)
            var closeStr = lastRow[6].GetString();
            if (!string.IsNullOrEmpty(closeStr))
            {
                closeStr = closeStr.Replace(",", "");
                if (decimal.TryParse(closeStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var price))
                {
                    _logger.LogDebug("Got 0050 year-end price {Price} for {Year}/12 from TWSE", price, year);
                    return price;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching 0050 year-end price from TWSE for {Year}", year);
            return null;
        }
    }
}
