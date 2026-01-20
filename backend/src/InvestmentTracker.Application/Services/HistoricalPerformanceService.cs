using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Application.Services;

/// <summary>
/// 歷史年度績效計算服務。
/// 計算任一年度（2020+）的 XIRR 與總報酬。
/// </summary>
public class HistoricalPerformanceService(
    IPortfolioRepository portfolioRepository,
    IStockTransactionRepository transactionRepository,
    PortfolioCalculator portfolioCalculator,
    ICurrentUserService currentUserService,
    ILogger<HistoricalPerformanceService> logger)
    : IHistoricalPerformanceService
{
    /// <summary>
    /// 取得可計算績效的年份清單。
    /// </summary>
    public async Task<AvailableYearsDto> GetAvailableYearsAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", portfolioId);

        if (portfolio.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        var transactions = await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var validTransactions = transactions.Where(t => !t.IsDeleted).ToList();

        if (validTransactions.Count == 0)
        {
            return new AvailableYearsDto
            {
                Years = [],
                EarliestYear = null,
                CurrentYear = DateTime.UtcNow.Year
            };
        }

        var earliestYear = validTransactions.Min(t => t.TransactionDate.Year);
        var currentYear = DateTime.UtcNow.Year;

        // Generate list of years from earliest to current
        var years = Enumerable.Range(earliestYear, currentYear - earliestYear + 1)
            .OrderDescending()
            .ToList();

        return new AvailableYearsDto
        {
            Years = years,
            EarliestYear = earliestYear,
            CurrentYear = currentYear
        };
    }

    /// <summary>
    /// 計算指定年度的績效。
    /// </summary>
    public async Task<YearPerformanceDto> CalculateYearPerformanceAsync(
        Guid portfolioId,
        CalculateYearPerformanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", portfolioId);

        if (portfolio.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        var year = request.Year;
        var currentYear = DateTime.UtcNow.Year;
        var isYtd = year == currentYear;
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = isYtd ? DateTime.UtcNow.Date : new DateTime(year, 12, 31);
        // For asking user prices, use previous year end (Dec 31 of year-1)
        var priceReferenceDate = new DateTime(year - 1, 12, 31);

        logger.LogInformation("Calculating performance for portfolio {PortfolioId}, year {Year}", portfolioId, year);

        var allTransactions = await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);

        // Filter to valid transactions only
        var validTransactions = allTransactions.Where(t => t is { IsDeleted: false, HasExchangeRate: true }).ToList();

        // Transactions up to year end (for year-end positions)
        var transactionsUpToYearEnd = validTransactions
            .Where(t => t.TransactionDate <= yearEnd)
            .ToList();

        // Transactions up to year start (for year-start positions)
        var transactionsUpToYearStart = validTransactions
            .Where(t => t.TransactionDate < yearStart)
            .ToList();

        // Transactions within the year
        var yearTransactions = validTransactions
            .Where(t => t.TransactionDate >= yearStart && t.TransactionDate <= yearEnd)
            .OrderBy(t => t.TransactionDate)
            .ToList();

        // Calculate positions at year end (NO split adjustment - use historical share counts)
        // Historical prices from APIs are already in their original (pre-split) values,
        // so we need to use the actual share counts at that time, not split-adjusted counts.
        // Filter out positions with 0 shares (stocks that were completely sold)
        var yearEndPositions = portfolioCalculator.RecalculateAllPositions(transactionsUpToYearEnd)
            .Where(p => p.TotalShares > 0)
            .ToList();

        // Calculate positions at year start (NO split adjustment)
        var yearStartPositions = portfolioCalculator.RecalculateAllPositions(transactionsUpToYearStart)
            .Where(p => p.TotalShares > 0)
            .ToList();

        // Check for missing prices
        var missingPrices = new List<MissingPriceDto>();
        var yearEndPrices = request.YearEndPrices != null
            ? new Dictionary<string, YearEndPriceInfo>(request.YearEndPrices, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, YearEndPriceInfo>(StringComparer.OrdinalIgnoreCase);

        // Year-start prices: use provided YearStartPrices, or fall back to YearEndPrices
        var yearStartPrices = request.YearStartPrices != null
            ? new Dictionary<string, YearEndPriceInfo>(request.YearStartPrices, StringComparer.OrdinalIgnoreCase)
            : yearEndPrices;

        // Check year-end prices for positions
        foreach (var position in yearEndPositions)
        {
            if (!yearEndPrices.ContainsKey(position.Ticker))
            {
                missingPrices.Add(new MissingPriceDto
                {
                    Ticker = position.Ticker,
                    Date = yearEnd,
                    PriceType = "YearEnd"
                });
            }
        }

        // Check year-start prices for positions that existed at year start
        foreach (var position in yearStartPositions)
        {
            if (!yearStartPrices.ContainsKey(position.Ticker))
            {
                missingPrices.Add(new MissingPriceDto
                {
                    Ticker = position.Ticker,
                    Date = priceReferenceDate,
                    PriceType = "YearStart"
                });
            }
        }

        // If missing prices, return partial result
        if (missingPrices.Count > 0)
        {
            logger.LogWarning("Missing {Count} prices for year {Year} performance calculation", missingPrices.Count, year);

            return new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = portfolio.BaseCurrency,
                MissingPrices = missingPrices.DistinctBy(p => (p.Ticker, p.PriceType)).ToList(),
                CashFlowCount = 0
            };
        }

        // ===== Calculate Source Currency (e.g., USD) Performance =====
        var cashFlowsSource = new List<CashFlow>();

        // Get USD/TWD rates for converting Taiwan stock prices to USD
        // Year-end rate is the primary source (always has non-Taiwan stocks if portfolio has any)
        var usdToTwdRateEnd = GetUsdToTwdRate(yearEndPrices);
        // Year-start rate: try yearStartPrices first, fall back to yearEndPrices if only Taiwan stocks at year-start
        var usdToTwdRateStart = GetUsdToTwdRate(yearStartPrices, usdToTwdRateEnd);

        logger.LogInformation("=== Year {Year} Performance Calculation Debug ===", year);
        logger.LogInformation("USD/TWD Rate Start: {RateStart}, End: {RateEnd}", usdToTwdRateStart, usdToTwdRateEnd);
        logger.LogInformation("Year Start Positions: {Count}, Year End Positions: {Count2}", yearStartPositions.Count, yearEndPositions.Count);

        foreach (var pos in yearStartPositions)
        {
            var hasPrice = yearStartPrices.TryGetValue(pos.Ticker, out var priceInfo);
            logger.LogInformation("  YearStart Position: {Ticker} x {Shares} shares, Price={Price}, ExRate={ExRate}, HasPrice={HasPrice}",
                pos.Ticker, pos.TotalShares, priceInfo?.Price ?? 0, priceInfo?.ExchangeRate ?? 0, hasPrice);
        }

        foreach (var pos in yearEndPositions)
        {
            var hasPrice = yearEndPrices.TryGetValue(pos.Ticker, out var priceInfo);
            logger.LogInformation("  YearEnd Position: {Ticker} x {Shares} shares, Price={Price}, ExRate={ExRate}, HasPrice={HasPrice}",
                pos.Ticker, pos.TotalShares, priceInfo?.Price ?? 0, priceInfo?.ExchangeRate ?? 0, hasPrice);
        }

        // Year-start portfolio value in source currency
        var startValueSource = 0m;
        foreach (var position in yearStartPositions)
        {
            if (yearStartPrices.TryGetValue(position.Ticker, out var priceInfo))
            {
                // Taiwan stocks: convert TWD price to USD using year-start rate
                var priceInSource = IsTaiwanTicker(position.Ticker)
                    ? priceInfo.Price / usdToTwdRateStart
                    : priceInfo.Price;
                startValueSource += position.TotalShares * priceInSource;
            }
        }

        logger.LogInformation("  Total startValueSource (USD): {StartValue}", startValueSource);

        if (startValueSource > 0)
        {
            cashFlowsSource.Add(new CashFlow(-startValueSource, yearStart));
            logger.LogInformation("  Added year-start cash flow: {Amount} on {Date}", -startValueSource, yearStart);
        }

        // Build a lookup of USD→TWD rates from non-Taiwan stock transactions (for converting Taiwan stock amounts to USD)
        // Key: transaction date, Value: exchange rate from that day's non-Taiwan stock transaction
        var dailyUsdToTwdRates = yearTransactions
            .Where(t => t is { IsTaiwanStock: false, ExchangeRate: > 0 })
            .GroupBy(t => t.TransactionDate.Date)
            .ToDictionary(g => g.Key, g => g.First().ExchangeRate!.Value);

        // Helper function to get USD→TWD rate for a specific date
        // Falls back to year-end rate if no transaction on that date
        decimal GetUsdToTwdRateForDate(DateTime date)
        {
            if (dailyUsdToTwdRates.TryGetValue(date.Date, out var rate))
                return rate;
            // Fallback: find closest date before this date
            var closestDate = dailyUsdToTwdRates.Keys
                .Where(d => d <= date.Date)
                .OrderByDescending(d => d)
                .FirstOrDefault();
            if (closestDate != default && dailyUsdToTwdRates.TryGetValue(closestDate, out var closestRate))
                return closestRate;
            // Final fallback: use year-end rate
            return usdToTwdRateEnd;
        }

        // Transactions in source currency
        logger.LogInformation("  Processing {Count} transactions in year:", yearTransactions.Count);
        foreach (var tx in yearTransactions)
        {
            if (tx.TransactionType == TransactionType.Buy)
            {
                decimal costInSource;
                if (tx.IsTaiwanStock)
                {
                    // Taiwan stocks: TotalCostSource is in TWD, convert to USD using rate from that date
                    var rateForDate = GetUsdToTwdRateForDate(tx.TransactionDate);
                    costInSource = tx.TotalCostSource / rateForDate;
                    logger.LogInformation("    BUY {Ticker}: {Shares} shares @ {Price} TWD, TotalCostTWD={TotalCost}, RateUsed={Rate}, CostInUSD={CostUSD}, Date={Date}",
                        tx.Ticker, tx.Shares, tx.PricePerShare, tx.TotalCostSource, rateForDate, costInSource, tx.TransactionDate);
                }
                else
                {
                    // Non-Taiwan stocks: TotalCostSource is already in USD
                    costInSource = tx.TotalCostSource;
                    logger.LogInformation("    BUY {Ticker}: {Shares} shares @ {Price} USD, TotalCostUSD={TotalCost}, TxExRate={ExRate}, Date={Date}",
                        tx.Ticker, tx.Shares, tx.PricePerShare, tx.TotalCostSource, tx.ExchangeRate, tx.TransactionDate);
                }
                cashFlowsSource.Add(new CashFlow(-costInSource, tx.TransactionDate));
            }
            else if (tx.TransactionType == TransactionType.Sell)
            {
                var proceeds = (tx.Shares * tx.PricePerShare) - tx.Fees;
                decimal proceedsInSource;
                if (tx.IsTaiwanStock)
                {
                    // Taiwan stocks: proceeds are in TWD, convert to USD using rate from that date
                    var rateForDate = GetUsdToTwdRateForDate(tx.TransactionDate);
                    proceedsInSource = proceeds / rateForDate;
                    logger.LogInformation("    SELL {Ticker}: {Shares} shares @ {Price} TWD, ProceedsTWD={Proceeds}, RateUsed={Rate}, ProceedsInUSD={ProceedsUSD}, Date={Date}",
                        tx.Ticker, tx.Shares, tx.PricePerShare, proceeds, rateForDate, proceedsInSource, tx.TransactionDate);
                }
                else
                {
                    // Non-Taiwan stocks: proceeds are already in USD
                    proceedsInSource = proceeds;
                    logger.LogInformation("    SELL {Ticker}: {Shares} shares @ {Price} USD, ProceedsUSD={Proceeds}, TxExRate={ExRate}, Date={Date}",
                        tx.Ticker, tx.Shares, tx.PricePerShare, proceeds, tx.ExchangeRate, tx.TransactionDate);
                }
                cashFlowsSource.Add(new CashFlow(proceedsInSource, tx.TransactionDate));
            }
        }

        // Year-end portfolio value in source currency
        var endValueSource = 0m;
        foreach (var position in yearEndPositions)
        {
            if (yearEndPrices.TryGetValue(position.Ticker, out var priceInfo))
            {
                // Taiwan stocks: convert TWD price to USD using year-end rate
                var priceInSource = IsTaiwanTicker(position.Ticker)
                    ? priceInfo.Price / usdToTwdRateEnd
                    : priceInfo.Price;
                endValueSource += position.TotalShares * priceInSource;
            }
        }

        logger.LogInformation("  Total endValueSource (USD): {EndValue}", endValueSource);

        if (endValueSource > 0)
        {
            cashFlowsSource.Add(new CashFlow(endValueSource, yearEnd));
            logger.LogInformation("  Added year-end cash flow: {Amount} on {Date}", endValueSource, yearEnd);
        }

        // Log all cash flows before XIRR calculation
        logger.LogInformation("  === All Source Currency Cash Flows ({Count} total) ===", cashFlowsSource.Count);
        foreach (var cf in cashFlowsSource.OrderBy(c => c.Date))
        {
            logger.LogInformation("    {Date}: {Amount:N2} USD", cf.Date.ToString("yyyy-MM-dd"), cf.Amount);
        }

        // Net contributions in source currency (using transaction-date rates for Taiwan stocks)
        var netContributionsSource = yearTransactions
                                         .Where(t => t.TransactionType == TransactionType.Buy)
                                         .Sum(t => t.IsTaiwanStock ? t.TotalCostSource / GetUsdToTwdRateForDate(t.TransactionDate) : t.TotalCostSource)
                                     - yearTransactions
                                         .Where(t => t.TransactionType == TransactionType.Sell)
                                         .Sum(t =>
                                         {
                                             var proceeds = (t.Shares * t.PricePerShare) - t.Fees;
                                             return t.IsTaiwanStock ? proceeds / GetUsdToTwdRateForDate(t.TransactionDate) : proceeds;
                                         });

        // Calculate source currency XIRR
        double? xirrSource = null;
        if (cashFlowsSource.Count >= 2)
        {
            xirrSource = portfolioCalculator.CalculateXirr(cashFlowsSource);
        }

        // Calculate source currency total return
        double? totalReturnSource = null;
        if (startValueSource > 0)
        {
            totalReturnSource = (double)((endValueSource - startValueSource - netContributionsSource) / startValueSource) * 100;
        }
        else if (netContributionsSource > 0)
        {
            totalReturnSource = (double)((endValueSource - netContributionsSource) / netContributionsSource) * 100;
        }

        // ===== Calculate Home Currency (e.g., TWD) Performance =====
        var cashFlowsHome = new List<CashFlow>();

        logger.LogInformation("  === Home Currency Calculation ===");

        // Year-start portfolio value in home currency
        var startValueHome = 0m;
        foreach (var position in yearStartPositions)
        {
            if (yearStartPrices.TryGetValue(position.Ticker, out var priceInfo))
            {
                var positionValue = position.TotalShares * priceInfo.Price * priceInfo.ExchangeRate;
                logger.LogInformation("    YearStart Home: {Ticker} x {Shares} @ {Price} * ExRate {ExRate} = {Value} TWD",
                    position.Ticker, position.TotalShares, priceInfo.Price, priceInfo.ExchangeRate, positionValue);
                startValueHome += positionValue;
            }
        }
        logger.LogInformation("  Total startValueHome: {Value} TWD", startValueHome);

        if (startValueHome > 0)
        {
            cashFlowsHome.Add(new CashFlow(-startValueHome, yearStart));
        }

        // Transactions in home currency
        foreach (var tx in yearTransactions)
        {
            if (tx.TransactionType == TransactionType.Buy)
            {
                cashFlowsHome.Add(new CashFlow(-tx.TotalCostHome!.Value, tx.TransactionDate));
            }
            else if (tx.TransactionType == TransactionType.Sell)
            {
                var proceeds = (tx.Shares * tx.PricePerShare * tx.ExchangeRate!.Value) - (tx.Fees * tx.ExchangeRate!.Value);
                cashFlowsHome.Add(new CashFlow(proceeds, tx.TransactionDate));
            }
        }

        // Year-end portfolio value in home currency
        var endValueHome = 0m;
        foreach (var position in yearEndPositions)
        {
            if (yearEndPrices.TryGetValue(position.Ticker, out var priceInfo))
            {
                endValueHome += position.TotalShares * priceInfo.Price * priceInfo.ExchangeRate;
            }
        }

        if (endValueHome > 0)
        {
            cashFlowsHome.Add(new CashFlow(endValueHome, yearEnd));
        }

        // Net contributions in home currency
        var netContributionsHome = yearTransactions
                                       .Where(t => t.TransactionType == TransactionType.Buy)
                                       .Sum(t => t.TotalCostHome ?? 0)
                                   - yearTransactions
                                       .Where(t => t.TransactionType == TransactionType.Sell)
                                       .Sum(t => (t.Shares * t.PricePerShare * (t.ExchangeRate ?? 1)) - (t.Fees * (t.ExchangeRate ?? 1)));

        // Calculate home currency XIRR
        double? xirrHome = null;
        if (cashFlowsHome.Count >= 2)
        {
            xirrHome = portfolioCalculator.CalculateXirr(cashFlowsHome);
        }

        // Calculate home currency total return
        double? totalReturnHome = null;
        if (startValueHome > 0)
        {
            totalReturnHome = (double)((endValueHome - startValueHome - netContributionsHome) / startValueHome) * 100;
        }
        else if (netContributionsHome > 0)
        {
            totalReturnHome = (double)((endValueHome - netContributionsHome) / netContributionsHome) * 100;
        }

        logger.LogInformation("Year {Year} performance: Source XIRR={XirrSource}%, Home XIRR={XirrHome}%",
            year, xirrSource.HasValue ? xirrSource.Value * 100 : null, xirrHome.HasValue ? xirrHome.Value * 100 : null);

        return new YearPerformanceDto
        {
            Year = year,
            // Home currency
            Xirr = xirrHome,
            XirrPercentage = xirrHome.HasValue ? xirrHome.Value * 100 : null,
            TotalReturnPercentage = totalReturnHome,
            StartValueHome = startValueHome > 0 ? startValueHome : null,
            EndValueHome = endValueHome > 0 ? endValueHome : null,
            NetContributionsHome = netContributionsHome,
            // Source currency
            SourceCurrency = portfolio.BaseCurrency,
            XirrSource = xirrSource,
            XirrPercentageSource = xirrSource.HasValue ? xirrSource.Value * 100 : null,
            TotalReturnPercentageSource = totalReturnSource,
            StartValueSource = startValueSource > 0 ? startValueSource : null,
            EndValueSource = endValueSource > 0 ? endValueSource : null,
            NetContributionsSource = netContributionsSource,
            // Common
            CashFlowCount = cashFlowsSource.Count,
            TransactionCount = yearTransactions.Count,
            MissingPrices = []
        };
    }

    /// <summary>
    /// 判斷是否為台股代號（以數字開頭）。
    /// </summary>
    private static bool IsTaiwanTicker(string ticker) =>
        !string.IsNullOrEmpty(ticker) && char.IsDigit(ticker[0]);

    /// <summary>
    /// 從價格資訊中取得 USD/TWD 匯率。
    /// 使用非台股的 ExchangeRate，若無則使用 fallback 值，最後預設 30。
    /// </summary>
    private static decimal GetUsdToTwdRate(Dictionary<string, YearEndPriceInfo> prices, decimal fallback = 30m)
    {
        var nonTaiwanEntry = prices.FirstOrDefault(p => !IsTaiwanTicker(p.Key));
        if (nonTaiwanEntry.Value?.ExchangeRate > 0)
        {
            return nonTaiwanEntry.Value.ExchangeRate;
        }
        return fallback > 0 ? fallback : 30m;
    }
}
