using System.Collections.Concurrent;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
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
    ICurrencyLedgerRepository currencyLedgerRepository,
    PortfolioCalculator portfolioCalculator,
    ICurrentUserService currentUserService,
    IHistoricalYearEndDataService historicalYearEndDataService,
    ITransactionDateExchangeRateService txDateFxService,
    ITransactionPortfolioSnapshotService txSnapshotService,
    ReturnCashFlowStrategyProvider cashFlowStrategyProvider,
    IReturnCalculator returnCalculator,
    ILogger<HistoricalPerformanceService> logger)
    : IHistoricalPerformanceService
{
    private const int MinimumReliableCoverageDays = 90;
    private const string XirrReliabilityHigh = "High";
    private const string XirrReliabilityMedium = "Medium";
    private const string XirrReliabilityLow = "Low";
    private const string XirrReliabilityUnavailable = "Unavailable";
    private const string ReturnDisplayDegradeReasonNoOpeningBaseline = "LOW_CONFIDENCE_NO_OPENING_BASELINE";
    private const string ReturnDisplayDegradeReasonLowCoverage = "LOW_CONFIDENCE_LOW_COVERAGE";
    private const string ReturnDisplayDegradeReasonNoOpeningBaselineAndLowCoverage = "LOW_CONFIDENCE_NO_OPENING_BASELINE_AND_LOW_COVERAGE";
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

        var earliestTransactionDateInYear = yearTransactions.Count > 0
            ? yearTransactions.Min(t => t.TransactionDate.Date)
            : (DateTime?)null;

        var sourceCurrency = portfolio.BaseCurrency.ToUpperInvariant();
        var homeCurrency = portfolio.HomeCurrency.ToUpperInvariant();

        var boundLedger = await currencyLedgerRepository.GetByIdWithTransactionsAsync(
            portfolio.BoundCurrencyLedgerId,
            cancellationToken);

        IReadOnlyList<CurrencyLedger> ledgers = [];
        IReadOnlyList<CurrencyTransaction> currencyTransactions = [];

        if (boundLedger is { IsActive: true } && boundLedger.UserId == portfolio.UserId)
        {
            ledgers = [boundLedger];
            currencyTransactions = boundLedger.Transactions.ToList();
        }

        var baselineReferenceDate = yearStart.AddDays(-1);
        var hasLedgerBaseline = boundLedger is { IsActive: true }
                                && boundLedger.UserId == portfolio.UserId
                                && boundLedger.Transactions.Any(t => !t.IsDeleted && t.TransactionDate.Date <= baselineReferenceDate.Date);

        var hasOpeningBaseline = transactionsUpToYearStart.Count > 0 || hasLedgerBaseline;
        DateTime? coverageStartDate = hasOpeningBaseline ? yearStart : earliestTransactionDateInYear;
        var coverageDays = CalculateCoverageDays(coverageStartDate, yearEnd);
        var usesPartialHistoryAssumption = !hasOpeningBaseline && earliestTransactionDateInYear.HasValue;

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
            : new Dictionary<string, YearEndPriceInfo>(StringComparer.OrdinalIgnoreCase);

        // Build ticker -> market lookup from transactions
        var tickerMarketLookup = validTransactions
            .GroupBy(t => t.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Market, StringComparer.OrdinalIgnoreCase);

        var yearStartYear = year - 1;

        // 合併 year-end / year-start 缺價代號，避免重複抓價與重複 FX 查詢
        var uniqueMissingTickers = yearEndPositions
            .Where(p => !yearEndPrices.ContainsKey(p.Ticker))
            .Select(p => p.Ticker)
            .Concat(
                yearStartPositions
                    .Where(p => !yearStartPrices.ContainsKey(p.Ticker))
                    .Select(p => p.Ticker))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fxRateCache = new ConcurrentDictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        async Task<decimal> ResolveExchangeRateAsync(string currency, int targetYear)
        {
            if (string.Equals(currency, homeCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return 1m;
            }

            var fxKey = $"{currency}->{homeCurrency}:{targetYear}";
            if (fxRateCache.TryGetValue(fxKey, out var cachedRate))
            {
                return cachedRate;
            }

            var rateResult = await historicalYearEndDataService.GetOrFetchYearEndExchangeRateAsync(
                currency,
                homeCurrency,
                targetYear,
                cancellationToken);

            var resolvedRate = rateResult?.Rate ?? 1m;
            fxRateCache.TryAdd(fxKey, resolvedRate);
            return resolvedRate;
        }

        foreach (var ticker in uniqueMissingTickers)
        {
            tickerMarketLookup.TryGetValue(ticker, out var market);
            var currency = GetCurrencyForMarket(market);

            if (!yearEndPrices.ContainsKey(ticker))
            {
                try
                {
                    var yearEndPriceResult = await historicalYearEndDataService.GetOrFetchYearEndPriceAsync(
                        ticker,
                        year,
                        market,
                        cancellationToken);

                    if (yearEndPriceResult != null)
                    {
                        var exchangeRate = await ResolveExchangeRateAsync(currency, year);
                        yearEndPrices[ticker] = new YearEndPriceInfo
                        {
                            Price = yearEndPriceResult.Price,
                            ExchangeRate = exchangeRate
                        };

                        logger.LogInformation("Auto-fetched year-end price for {Ticker}/{Year}", ticker, year);
                    }
                }
                catch (Exception ex)
                {
                    // Log and continue - this ticker will be added to missingPrices later
                    logger.LogWarning(ex, "Failed to fetch year-end price for {Ticker}/{Year}, will be added to missing prices", ticker, year);
                }
            }

            if (!yearStartPrices.ContainsKey(ticker))
            {
                try
                {
                    var yearStartPriceResult = await historicalYearEndDataService.GetOrFetchYearEndPriceAsync(
                        ticker,
                        yearStartYear,
                        market,
                        cancellationToken);

                    if (yearStartPriceResult != null)
                    {
                        var exchangeRate = await ResolveExchangeRateAsync(currency, yearStartYear);
                        yearStartPrices[ticker] = new YearEndPriceInfo
                        {
                            Price = yearStartPriceResult.Price,
                            ExchangeRate = exchangeRate
                        };

                        logger.LogInformation("Auto-fetched year-start price for {Ticker}/{Year}", ticker, yearStartYear);
                    }
                }
                catch (Exception ex)
                {
                    // Log and continue - this ticker will be added to missingPrices later
                    logger.LogWarning(ex, "Failed to fetch year-start price for {Ticker}/{Year}, will be added to missing prices", ticker, yearStartYear);
                }
            }
        }

        // Check year-end prices for positions (after auto-fetch)
        foreach (var position in yearEndPositions)
        {
            if (!yearEndPrices.ContainsKey(position.Ticker))
            {
                int? marketValue = tickerMarketLookup.TryGetValue(position.Ticker, out var positionMarket)
                    ? (int)positionMarket
                    : null;
                missingPrices.Add(new MissingPriceDto
                {
                    Ticker = position.Ticker,
                    Date = yearEnd,
                    PriceType = "YearEnd",
                    Market = marketValue
                });
            }
        }

        // Check year-start prices for positions that existed at year start
        foreach (var position in yearStartPositions)
        {
            if (!yearStartPrices.ContainsKey(position.Ticker))
            {
                int? marketValue = tickerMarketLookup.TryGetValue(position.Ticker, out var positionMarket)
                    ? (int)positionMarket
                    : null;
                missingPrices.Add(new MissingPriceDto
                {
                    Ticker = position.Ticker,
                    Date = priceReferenceDate,
                    PriceType = "YearStart",
                    Market = marketValue
                });
            }
        }

        // If missing prices, return partial result
        if (missingPrices.Count > 0)
        {
            logger.LogWarning("Missing {Count} prices for year {Year} performance calculation", missingPrices.Count, year);

            var reliabilityOnMissingPrices = ResolveXirrReliability(
                hasOpeningBaseline,
                usesPartialHistoryAssumption,
                coverageDays,
                hasXirrValue: false);
            var degradeSignalOnMissingPrices = ResolveReturnDisplayDegradeSignal(
                reliabilityOnMissingPrices,
                hasOpeningBaseline,
                coverageDays);

            return new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = sourceCurrency,
                MissingPrices = missingPrices.DistinctBy(p => (p.Ticker, p.PriceType)).ToList(),
                CashFlowCount = 0,
                TransactionCount = yearTransactions.Count,
                EarliestTransactionDateInYear = earliestTransactionDateInYear,
                CoverageStartDate = coverageStartDate,
                CoverageDays = coverageDays,
                HasOpeningBaseline = hasOpeningBaseline,
                UsesPartialHistoryAssumption = usesPartialHistoryAssumption,
                XirrReliability = reliabilityOnMissingPrices,
                ShouldDegradeReturnDisplay = degradeSignalOnMissingPrices.ShouldDegrade,
                ReturnDisplayDegradeReasonCode = degradeSignalOnMissingPrices.ReasonCode,
                ReturnDisplayDegradeReasonMessage = degradeSignalOnMissingPrices.ReasonMessage
            };
        }

        // ===== Calculate Source Currency (portfolio.BaseCurrency) Performance =====
        var cashFlowsSource = new List<CashFlow>();

        string GetTickerCurrency(string ticker)
        {
            if (tickerMarketLookup.TryGetValue(ticker, out var market))
                return GetCurrencyForMarket(market);

            return IsTaiwanTicker(ticker) ? "TWD" : "USD";
        }

        async Task<decimal> GetSourceToHomeFallbackRateAsync(DateTime date, decimal fallbackRate = 1m)
        {
            if (string.Equals(sourceCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase))
                return 1m;

            var fxResult = await txDateFxService.GetOrFetchAsync(sourceCurrency, homeCurrency, date, cancellationToken);
            if (fxResult is { Rate: > 0 })
                return fxResult.Rate;

            return fallbackRate > 0 ? fallbackRate : 1m;
        }

        var sourceToHomeFallbackEnd = await GetSourceToHomeFallbackRateAsync(yearEnd);
        var sourceToHomeRateEnd = GetSourceToHomeRate(
            yearEndPrices,
            sourceCurrency,
            homeCurrency,
            GetTickerCurrency,
            sourceToHomeFallbackEnd);

        var sourceToHomeFallbackStart = await GetSourceToHomeFallbackRateAsync(priceReferenceDate, sourceToHomeRateEnd);
        var sourceToHomeRateStart = GetSourceToHomeRate(
            yearStartPrices,
            sourceCurrency,
            homeCurrency,
            GetTickerCurrency,
            sourceToHomeFallbackStart);

        // Build source→home daily rate lookup from transactions already denominated in source currency.
        var dailySourceToHomeRates = yearTransactions
            .Where(t => string.Equals(t.Currency.ToString(), sourceCurrency, StringComparison.OrdinalIgnoreCase)
                        && t.ExchangeRate is > 0)
            .GroupBy(t => t.TransactionDate.Date)
            .ToDictionary(g => g.Key, g => g.First().ExchangeRate!.Value);

        decimal GetSourceToHomeRateForDate(DateTime date)
        {
            if (string.Equals(sourceCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase))
                return 1m;

            if (dailySourceToHomeRates.TryGetValue(date.Date, out var rate))
                return rate;

            var closestDate = dailySourceToHomeRates.Keys
                .Where(d => d <= date.Date)
                .OrderByDescending(d => d)
                .FirstOrDefault();

            if (closestDate != default && dailySourceToHomeRates.TryGetValue(closestDate, out var closestRate))
                return closestRate;

            return sourceToHomeRateEnd > 0 ? sourceToHomeRateEnd : sourceToHomeRateStart;
        }

        decimal ConvertAmountToSource(
            decimal amount,
            string fromCurrency,
            DateTime date,
            decimal? fromToHomeRate = null)
        {
            if (string.Equals(fromCurrency, sourceCurrency, StringComparison.OrdinalIgnoreCase))
                return amount;

            var sourceToHomeRate = GetSourceToHomeRateForDate(date);

            if (string.Equals(fromCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return sourceToHomeRate > 0 ? amount / sourceToHomeRate : 0m;
            }

            if (fromToHomeRate is > 0)
            {
                if (string.Equals(sourceCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase))
                    return amount * fromToHomeRate.Value;

                return sourceToHomeRate > 0
                    ? amount * fromToHomeRate.Value / sourceToHomeRate
                    : 0m;
            }

            return 0m;
        }

        logger.LogDebug("=== Year {Year} Performance Calculation Debug ===", year);
        logger.LogDebug("{SourceCurrency}/{HomeCurrency} Rate Start: {RateStart}, End: {RateEnd}",
            sourceCurrency, homeCurrency, sourceToHomeRateStart, sourceToHomeRateEnd);
        logger.LogDebug("Year Start Positions: {Count}, Year End Positions: {Count2}", yearStartPositions.Count, yearEndPositions.Count);

        foreach (var pos in yearStartPositions)
        {
            var hasPrice = yearStartPrices.TryGetValue(pos.Ticker, out var priceInfo);
            logger.LogDebug("  YearStart Position: {Ticker} x {Shares} shares, Price={Price}, ExRate={ExRate}, HasPrice={HasPrice}",
                pos.Ticker, pos.TotalShares, priceInfo?.Price ?? 0, priceInfo?.ExchangeRate ?? 0, hasPrice);
        }

        foreach (var pos in yearEndPositions)
        {
            var hasPrice = yearEndPrices.TryGetValue(pos.Ticker, out var priceInfo);
            logger.LogDebug("  YearEnd Position: {Ticker} x {Shares} shares, Price={Price}, ExRate={ExRate}, HasPrice={HasPrice}",
                pos.Ticker, pos.TotalShares, priceInfo?.Price ?? 0, priceInfo?.ExchangeRate ?? 0, hasPrice);
        }

        // Year-start portfolio value in source currency
        var startValueSource = 0m;
        foreach (var position in yearStartPositions)
        {
            if (yearStartPrices.TryGetValue(position.Ticker, out var priceInfo))
            {
                var tickerCurrency = GetTickerCurrency(position.Ticker);
                var priceInSource = ConvertAmountToSource(
                    priceInfo.Price,
                    tickerCurrency,
                    yearStart,
                    priceInfo.ExchangeRate);

                startValueSource += position.TotalShares * priceInSource;
            }
        }

        logger.LogDebug("  Total startValueSource ({SourceCurrency}): {StartValue}", sourceCurrency, startValueSource);

        if (startValueSource > 0)
        {
            cashFlowsSource.Add(new CashFlow(-startValueSource, yearStart));
            logger.LogDebug("  Added year-start cash flow: {Amount} on {Date}", -startValueSource, yearStart);
        }

        // Transactions in source currency
        logger.LogDebug("  Processing {Count} transactions in year:", yearTransactions.Count);
        foreach (var tx in yearTransactions)
        {
            var txCurrency = tx.Currency.ToString();

            switch (tx.TransactionType)
            {
                case TransactionType.Buy:
                {
                    var costInSource = ConvertAmountToSource(
                        tx.TotalCostSource,
                        txCurrency,
                        tx.TransactionDate,
                        tx.ExchangeRate);

                    logger.LogDebug(
                        "    BUY {Ticker}: {Shares} shares @ {Price} {TxCurrency}, TotalCost={TotalCost}, CostInSource={CostInSource} {SourceCurrency}, Date={Date}",
                        tx.Ticker,
                        tx.Shares,
                        tx.PricePerShare,
                        txCurrency,
                        tx.TotalCostSource,
                        costInSource,
                        sourceCurrency,
                        tx.TransactionDate);

                    cashFlowsSource.Add(new CashFlow(-costInSource, tx.TransactionDate));
                    break;
                }
                case TransactionType.Sell:
                {
                    var proceeds = tx.NetProceedsSource;
                    var proceedsInSource = ConvertAmountToSource(
                        proceeds,
                        txCurrency,
                        tx.TransactionDate,
                        tx.ExchangeRate);

                    logger.LogDebug(
                        "    SELL {Ticker}: {Shares} shares @ {Price} {TxCurrency}, Proceeds={Proceeds}, ProceedsInSource={ProceedsInSource} {SourceCurrency}, Date={Date}",
                        tx.Ticker,
                        tx.Shares,
                        tx.PricePerShare,
                        txCurrency,
                        proceeds,
                        proceedsInSource,
                        sourceCurrency,
                        tx.TransactionDate);

                    cashFlowsSource.Add(new CashFlow(proceedsInSource, tx.TransactionDate));
                    break;
                }
            }
        }

        // Year-end portfolio value in source currency
        var endValueSource = 0m;
        foreach (var position in yearEndPositions)
        {
            if (!yearEndPrices.TryGetValue(position.Ticker, out var priceInfo)) continue;

            var tickerCurrency = GetTickerCurrency(position.Ticker);
            var priceInSource = ConvertAmountToSource(
                priceInfo.Price,
                tickerCurrency,
                yearEnd,
                priceInfo.ExchangeRate);

            endValueSource += position.TotalShares * priceInSource;
        }

        logger.LogDebug("  Total endValueSource ({SourceCurrency}): {EndValue}", sourceCurrency, endValueSource);

        if (endValueSource > 0)
        {
            cashFlowsSource.Add(new CashFlow(endValueSource, yearEnd));
            logger.LogDebug("  Added year-end cash flow: {Amount} on {Date}", endValueSource, yearEnd);
        }

        // Log all cash flows before XIRR calculation
        logger.LogDebug("  === All Source Currency Cash Flows ({Count} total) ===", cashFlowsSource.Count);
        foreach (var cf in cashFlowsSource.OrderBy(c => c.Date))
        {
            logger.LogDebug("    {Date}: {Amount:N2} {SourceCurrency}", cf.Date.ToString("yyyy-MM-dd"), cf.Amount, sourceCurrency);
        }

        // Calculate source currency XIRR
        double? rawXirrSource = null;
        if (cashFlowsSource.Count >= 2)
        {
            rawXirrSource = portfolioCalculator.CalculateXirr(cashFlowsSource);
        }

        // ===== US1: Cash Flow Strategy (StockTransaction vs CurrencyLedger) =====
        var cashFlowStrategy = cashFlowStrategyProvider.GetStrategy(
            portfolio,
            validTransactions,
            ledgers,
            currencyTransactions);

        var cashFlowEvents = cashFlowStrategy.GetCashFlowEvents(
            portfolio,
            yearStart,
            yearEnd,
            validTransactions,
            ledgers,
            currencyTransactions);

        var cashFlowEventIds = cashFlowEvents
            .Select(e => e.TransactionId)
            .ToHashSet();

        // Ensure snapshots exist for the year (on-demand backfill)
        await txSnapshotService.BackfillSnapshotsAsync(portfolioId, yearStart, yearEnd, cancellationToken);

        var snapshots = await txSnapshotService.GetSnapshotsAsync(portfolioId, yearStart, yearEnd, cancellationToken);

        var cashFlowEventSnapshots = snapshots
            .Where(s => cashFlowEventIds.Contains(s.TransactionId))
            .OrderBy(s => s.SnapshotDate)
            .ThenBy(s => s.CreatedAt)
            .ToList();

        // 驗算：同日多筆現金流事件，快照必須可以串接（下一筆 before 應等於前一筆 after），否則 TWR 可能被多乘錯誤因子。
        // 這裡只做最小防呆：若偵測到「同日重複但 before/after 完全相同」的情況，代表快照仍是舊格式或資料壞掉。
        for (var i = 1; i < cashFlowEventSnapshots.Count; i++)
        {
            var prev = cashFlowEventSnapshots[i - 1];
            var cur = cashFlowEventSnapshots[i];

            if (prev.SnapshotDate.Date != cur.SnapshotDate.Date)
                continue;

            if (cur.PortfolioValueBeforeHome == prev.PortfolioValueBeforeHome
                && cur.PortfolioValueAfterHome == prev.PortfolioValueAfterHome
                && cur.PortfolioValueBeforeSource == prev.PortfolioValueBeforeSource
                && cur.PortfolioValueAfterSource == prev.PortfolioValueAfterSource)
            {
                logger.LogWarning(
                    "Detected duplicated same-day snapshots for portfolio {PortfolioId} on {Date}. Consider rebuilding snapshots.",
                    portfolioId,
                    cur.SnapshotDate.Date);
                break;
            }
        }

        async Task<decimal> ConvertAmountAsync(string fromCurrency, string toCurrency, DateTime date, decimal amount)
        {
            var normalizedFromCurrency = fromCurrency.ToUpperInvariant();
            var normalizedToCurrency = toCurrency.ToUpperInvariant();

            if (string.Equals(normalizedFromCurrency, normalizedToCurrency, StringComparison.OrdinalIgnoreCase))
                return amount;

            if (string.Equals(sourceCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase)
                && string.Equals(normalizedFromCurrency, sourceCurrency, StringComparison.OrdinalIgnoreCase)
                && string.Equals(normalizedToCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return amount;
            }

            if (string.Equals(normalizedFromCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase)
                && string.Equals(normalizedToCurrency, sourceCurrency, StringComparison.OrdinalIgnoreCase))
            {
                var rate = GetSourceToHomeRateForDate(date);
                return rate > 0 ? amount / rate : 0m;
            }

            if (string.Equals(normalizedFromCurrency, sourceCurrency, StringComparison.OrdinalIgnoreCase)
                && string.Equals(normalizedToCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase))
            {
                var rate = GetSourceToHomeRateForDate(date);
                return amount * rate;
            }

            var fx = await txDateFxService.GetOrFetchAsync(normalizedFromCurrency, normalizedToCurrency, date, cancellationToken);
            if (fx != null)
                return amount * fx.Rate;

            logger.LogWarning(
                "Missing FX rate {From}/{To} on {Date} for cash flow conversion (amount={Amount})",
                normalizedFromCurrency,
                normalizedToCurrency,
                date.Date,
                amount);

            return 0m;
        }

        var dietzCashFlowsSource = new List<ReturnCashFlow>();
        var dietzCashFlowsHome = new List<ReturnCashFlow>();

        foreach (var e in cashFlowEvents)
        {
            var amountSource = await ConvertAmountAsync(e.CurrencyCode, portfolio.BaseCurrency, e.TransactionDate, e.Amount);
            dietzCashFlowsSource.Add(new ReturnCashFlow(e.TransactionDate, amountSource));

            decimal amountHome;

            if (string.Equals(e.CurrencyCode, portfolio.HomeCurrency, StringComparison.OrdinalIgnoreCase))
            {
                amountHome = e.Amount;
            }
            else if (e.Source == ReturnCashFlowEventSource.StockTransaction
                     && validTransactions.FirstOrDefault(t => t.Id == e.TransactionId) is { ExchangeRate: > 0 } stockTx)
            {
                amountHome = e.Amount * stockTx.ExchangeRate!.Value;
            }
            else if (e.Source == ReturnCashFlowEventSource.CurrencyLedger
                     && currencyTransactions.FirstOrDefault(t => t.Id == e.TransactionId) is { HomeAmount: > 0 } currencyTx)
            {
                var sign = e.Amount >= 0 ? 1m : -1m;
                amountHome = sign * currencyTx.HomeAmount!.Value;
            }
            else
            {
                amountHome = await ConvertAmountAsync(e.CurrencyCode, portfolio.HomeCurrency, e.TransactionDate, e.Amount);
            }

            dietzCashFlowsHome.Add(new ReturnCashFlow(e.TransactionDate, amountHome));
        }

        var ledgerStartValueSource = 0m;
        var ledgerEndValueSource = 0m;
        var ledgerStartValueHome = 0m;
        var ledgerEndValueHome = 0m;

        if (boundLedger is { IsActive: true } && boundLedger.UserId == portfolio.UserId)
        {
            decimal GetLedgerBalance(DateTime date)
            {
                return boundLedger.Transactions
                    .Where(t => !t.IsDeleted && t.TransactionDate.Date <= date.Date)
                    .OrderBy(t => t.TransactionDate)
                    .ThenBy(t => t.CreatedAt)
                    .Sum(t => t.TransactionType switch
                    {
                        CurrencyTransactionType.ExchangeSell => -t.ForeignAmount,
                        CurrencyTransactionType.Spend => -t.ForeignAmount,
                        CurrencyTransactionType.OtherExpense => -t.ForeignAmount,
                        CurrencyTransactionType.Withdraw => -t.ForeignAmount,
                        _ => t.ForeignAmount
                    });
            }

            var ledgerStartDate = yearStart.AddDays(-1);
            var ledgerBalanceStart = GetLedgerBalance(ledgerStartDate);
            var ledgerBalanceEnd = GetLedgerBalance(yearEnd);

            ledgerStartValueSource = await ConvertAmountAsync(boundLedger.CurrencyCode, portfolio.BaseCurrency, ledgerStartDate, ledgerBalanceStart);
            ledgerEndValueSource = await ConvertAmountAsync(boundLedger.CurrencyCode, portfolio.BaseCurrency, yearEnd, ledgerBalanceEnd);
            ledgerStartValueHome = await ConvertAmountAsync(boundLedger.CurrencyCode, portfolio.HomeCurrency, ledgerStartDate, ledgerBalanceStart);
            ledgerEndValueHome = await ConvertAmountAsync(boundLedger.CurrencyCode, portfolio.HomeCurrency, yearEnd, ledgerBalanceEnd);
        }

        var closedLoopStartValueSource = startValueSource + ledgerStartValueSource;
        var closedLoopEndValueSource = endValueSource + ledgerEndValueSource;

        var closedLoopSourceSnapshots = cashFlowEventSnapshots
            .Select(s => new ReturnValuationSnapshot(
                Date: s.SnapshotDate,
                ValueBefore: s.PortfolioValueBeforeSource,
                ValueAfter: s.PortfolioValueAfterSource))
            .ToList();

        var modifiedDietzSource = returnCalculator.CalculateModifiedDietz(
            startValue: closedLoopStartValueSource,
            endValue: closedLoopEndValueSource,
            periodStart: yearStart,
            periodEnd: yearEnd,
            cashFlows: dietzCashFlowsSource);

        var twrSource = returnCalculator.CalculateTimeWeightedReturn(
            startValue: closedLoopStartValueSource,
            endValue: closedLoopEndValueSource,
            cashFlowSnapshots: closedLoopSourceSnapshots);

        // ===== Calculate Home Currency Performance =====
        var cashFlowsHome = new List<CashFlow>();

        logger.LogDebug("  === Home Currency Calculation ({HomeCurrency}) ===", homeCurrency);

        // Year-start portfolio value in home currency
        var startValueHome = 0m;
        foreach (var position in yearStartPositions)
        {
            if (yearStartPrices.TryGetValue(position.Ticker, out var priceInfo))
            {
                var positionValue = position.TotalShares * priceInfo.Price * priceInfo.ExchangeRate;
                logger.LogDebug("    YearStart Home: {Ticker} x {Shares} @ {Price} * ExRate {ExRate} = {Value} {HomeCurrency}",
                    position.Ticker, position.TotalShares, priceInfo.Price, priceInfo.ExchangeRate, positionValue, homeCurrency);
                startValueHome += positionValue;
            }
        }
        logger.LogDebug("  Total startValueHome ({HomeCurrency}): {Value}", homeCurrency, startValueHome);

        if (startValueHome > 0)
        {
            cashFlowsHome.Add(new CashFlow(-startValueHome, yearStart));
        }

        // Transactions in home currency
        foreach (var tx in yearTransactions)
        {
            switch (tx.TransactionType)
            {
                case TransactionType.Buy:
                    cashFlowsHome.Add(new CashFlow(-tx.TotalCostHome!.Value, tx.TransactionDate));
                    break;
                case TransactionType.Sell:
                {
                    var proceeds = tx.NetProceedsSource * tx.ExchangeRate!.Value;
                    cashFlowsHome.Add(new CashFlow(proceeds, tx.TransactionDate));
                    break;
                }
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

        // Calculate home currency XIRR
        double? rawXirrHome = null;
        if (cashFlowsHome.Count >= 2)
        {
            rawXirrHome = portfolioCalculator.CalculateXirr(cashFlowsHome);
        }

        var closedLoopStartValueHome = startValueHome + ledgerStartValueHome;
        var closedLoopEndValueHome = endValueHome + ledgerEndValueHome;

        // Net contributions aligned to closed-loop external cash-flow set
        var netContributionsSource = dietzCashFlowsSource.Sum(cf => cf.Amount);
        var netContributionsHome = dietzCashFlowsHome.Sum(cf => cf.Amount);

        var totalReturnSource = CalculateTotalReturnPercentage(
            closedLoopStartValueSource,
            closedLoopEndValueSource,
            netContributionsSource);

        // ===== US1: Modified Dietz + TWR (Home Currency) =====
        var closedLoopHomeSnapshots = cashFlowEventSnapshots
            .Select(s => new ReturnValuationSnapshot(
                Date: s.SnapshotDate,
                ValueBefore: s.PortfolioValueBeforeHome,
                ValueAfter: s.PortfolioValueAfterHome))
            .ToList();

        var modifiedDietzHome = returnCalculator.CalculateModifiedDietz(
            startValue: closedLoopStartValueHome,
            endValue: closedLoopEndValueHome,
            periodStart: yearStart,
            periodEnd: yearEnd,
            cashFlows: dietzCashFlowsHome);

        var twrHome = returnCalculator.CalculateTimeWeightedReturn(
            startValue: closedLoopStartValueHome,
            endValue: closedLoopEndValueHome,
            cashFlowSnapshots: closedLoopHomeSnapshots);

        var totalReturnHome = CalculateTotalReturnPercentage(
            closedLoopStartValueHome,
            closedLoopEndValueHome,
            netContributionsHome);

        var hasXirrValue = rawXirrHome.HasValue || rawXirrSource.HasValue;
        var xirrReliability = ResolveXirrReliability(
            hasOpeningBaseline,
            usesPartialHistoryAssumption,
            coverageDays,
            hasXirrValue);

        var xirrHome = rawXirrHome;
        var xirrSource = rawXirrSource;

        if (string.Equals(xirrReliability, XirrReliabilityUnavailable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(xirrReliability, XirrReliabilityLow, StringComparison.OrdinalIgnoreCase))
        {
            xirrHome = null;
            xirrSource = null;
        }

        var xirrPercentageHome = xirrHome.HasValue ? xirrHome.Value * 100 : (double?)null;
        var xirrPercentageSource = xirrSource.HasValue ? xirrSource.Value * 100 : (double?)null;

        var returnDisplayDegradeSignal = ResolveReturnDisplayDegradeSignal(
            xirrReliability,
            hasOpeningBaseline,
            coverageDays);

        logger.LogInformation(
            "Year {Year} performance reliability={Reliability} coverageDays={CoverageDays} hasOpeningBaseline={HasOpeningBaseline} usesPartialHistoryAssumption={UsesPartialHistoryAssumption}",
            year,
            xirrReliability,
            coverageDays,
            hasOpeningBaseline,
            usesPartialHistoryAssumption);

        var sourceXirrPctForLog = xirrSource.HasValue ? xirrSource.Value * 100 : (double?)null;
        var homeXirrPctForLog = xirrHome.HasValue ? xirrHome.Value * 100 : (double?)null;
        logger.LogInformation("Year {Year} performance: Source XIRR={XirrSource}%, Home XIRR={XirrHome}%",
            year, sourceXirrPctForLog, homeXirrPctForLog);

        return new YearPerformanceDto
        {
            Year = year,
            // Home currency (portfolio.HomeCurrency)
            Xirr = xirrHome,
            XirrPercentage = xirrPercentageHome,
            TotalReturnPercentage = totalReturnHome,
            ModifiedDietzPercentage = modifiedDietzHome.HasValue ? (double)(modifiedDietzHome.Value * 100m) : null,
            TimeWeightedReturnPercentage = twrHome.HasValue ? (double)(twrHome.Value * 100m) : null,
            StartValueHome = closedLoopStartValueHome,
            EndValueHome = closedLoopEndValueHome,
            NetContributionsHome = netContributionsHome,
            // Source currency (portfolio.BaseCurrency)
            SourceCurrency = sourceCurrency,
            XirrSource = xirrSource,
            XirrPercentageSource = xirrPercentageSource,
            TotalReturnPercentageSource = totalReturnSource,
            ModifiedDietzPercentageSource = modifiedDietzSource.HasValue ? (double)(modifiedDietzSource.Value * 100m) : null,
            TimeWeightedReturnPercentageSource = twrSource.HasValue ? (double)(twrSource.Value * 100m) : null,
            StartValueSource = closedLoopStartValueSource,
            EndValueSource = closedLoopEndValueSource,
            NetContributionsSource = netContributionsSource,
            // Common
            CashFlowCount = cashFlowsSource.Count,
            TransactionCount = yearTransactions.Count,
            EarliestTransactionDateInYear = earliestTransactionDateInYear,
            CoverageStartDate = coverageStartDate,
            CoverageDays = coverageDays,
            HasOpeningBaseline = hasOpeningBaseline,
            UsesPartialHistoryAssumption = usesPartialHistoryAssumption,
            XirrReliability = xirrReliability,
            ShouldDegradeReturnDisplay = returnDisplayDegradeSignal.ShouldDegrade,
            ReturnDisplayDegradeReasonCode = returnDisplayDegradeSignal.ReasonCode,
            ReturnDisplayDegradeReasonMessage = returnDisplayDegradeSignal.ReasonMessage,
            MissingPrices = []
        };
    }

    private static int? CalculateCoverageDays(DateTime? coverageStartDate, DateTime yearEnd)
    {
        if (!coverageStartDate.HasValue)
            return null;

        var coverageStart = coverageStartDate.Value.Date;
        var coverageEnd = yearEnd.Date;

        if (coverageEnd < coverageStart)
            return 0;

        return (coverageEnd - coverageStart).Days + 1;
    }

    private static string ResolveXirrReliability(
        bool hasOpeningBaseline,
        bool usesPartialHistoryAssumption,
        int? coverageDays,
        bool hasXirrValue)
    {
        if (!hasXirrValue)
            return XirrReliabilityUnavailable;

        if (!hasOpeningBaseline)
            return XirrReliabilityUnavailable;

        if (!coverageDays.HasValue || coverageDays.Value <= 0)
            return XirrReliabilityUnavailable;

        if (coverageDays.Value < MinimumReliableCoverageDays)
            return XirrReliabilityLow;

        if (usesPartialHistoryAssumption)
            return XirrReliabilityMedium;

        return XirrReliabilityHigh;
    }

    private static ReturnDisplayDegradeSignal ResolveReturnDisplayDegradeSignal(
        string? xirrReliability,
        bool hasOpeningBaseline,
        int? coverageDays)
    {
        var isLowConfidence = string.Equals(xirrReliability, XirrReliabilityLow, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(xirrReliability, XirrReliabilityUnavailable, StringComparison.OrdinalIgnoreCase);

        if (!isLowConfidence)
            return ReturnDisplayDegradeSignal.None;

        var hasLowCoverage = !coverageDays.HasValue || coverageDays.Value < MinimumReliableCoverageDays;

        if (!hasOpeningBaseline && hasLowCoverage)
        {
            return new ReturnDisplayDegradeSignal(
                ShouldDegrade: true,
                ReasonCode: ReturnDisplayDegradeReasonNoOpeningBaselineAndLowCoverage,
                ReasonMessage: "Low confidence performance: missing opening baseline and insufficient coverage.");
        }

        if (!hasOpeningBaseline)
        {
            return new ReturnDisplayDegradeSignal(
                ShouldDegrade: true,
                ReasonCode: ReturnDisplayDegradeReasonNoOpeningBaseline,
                ReasonMessage: "Low confidence performance: missing opening baseline.");
        }

        if (hasLowCoverage)
        {
            return new ReturnDisplayDegradeSignal(
                ShouldDegrade: true,
                ReasonCode: ReturnDisplayDegradeReasonLowCoverage,
                ReasonMessage: "Low confidence performance: insufficient coverage period.");
        }

        return ReturnDisplayDegradeSignal.None;
    }

    private static double? CalculateTotalReturnPercentage(decimal startValue, decimal endValue, decimal netContributions)
    {
        if (startValue != 0m)
            return (double)((endValue - startValue - netContributions) / startValue) * 100;

        if (netContributions != 0m)
            return (double)((endValue - netContributions) / netContributions) * 100;

        return null;
    }

    /// <summary>
    /// 判斷是否為台股代號（以數字開頭）。
    /// </summary>
    private static bool IsTaiwanTicker(string ticker) =>
        !string.IsNullOrEmpty(ticker) && char.IsDigit(ticker[0]);

    /// <summary>
    /// 從價格資訊中取得 source/home 匯率（source→home）。
    /// 若 source 與 home 相同，直接回傳 1。
    /// 優先尋找與 sourceCurrency 相同計價幣別的價格資料，取其 ExchangeRate（原始幣別→home）。
    /// 若找不到或資料不完整，回傳 fallback（預設 1）。
    /// </summary>
    private static decimal GetSourceToHomeRate(
        Dictionary<string, YearEndPriceInfo> prices,
        string sourceCurrency,
        string homeCurrency,
        Func<string, string> resolveTickerCurrency,
        decimal fallback = 1m)
    {
        if (string.Equals(sourceCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase))
            return 1m;

        foreach (var (ticker, priceInfo) in prices)
        {
            if (priceInfo.ExchangeRate <= 0)
                continue;

            var tickerCurrency = resolveTickerCurrency(ticker);
            if (string.Equals(tickerCurrency, sourceCurrency, StringComparison.OrdinalIgnoreCase))
                return priceInfo.ExchangeRate;
        }

        return fallback > 0 ? fallback : 1m;
    }

    /// <summary>
    /// 根據市場判斷計價貨幣。
    /// 與即時報價服務 (StockPriceService) 使用相同邏輯。
    /// </summary>
    private static string GetCurrencyForMarket(StockMarket? market)
    {
        return market switch
        {
            StockMarket.TW => "TWD",
            StockMarket.US => "USD",
            StockMarket.UK => "USD", // UK 市場多數 ETF 以 USD 計價
            StockMarket.EU => "USD", // Euronext 多數 ETF 以 USD 計價
            _ => "USD" // 預設 USD
        };
    }

    private readonly record struct ReturnDisplayDegradeSignal(
        bool ShouldDegrade,
        string? ReasonCode,
        string? ReasonMessage)
    {
        public static ReturnDisplayDegradeSignal None { get; } = new(
            ShouldDegrade: false,
            ReasonCode: null,
            ReasonMessage: null);
    }
}
