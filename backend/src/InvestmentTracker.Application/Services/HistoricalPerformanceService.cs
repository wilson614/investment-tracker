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
    private const decimal RecentLargeInflowThresholdRatio = 0.5m;
    private const decimal RecentLargeInflowPeriodRatio = 0.1m;
    private const string RecentLargeInflowWarningMessage = "近期大額資金異動可能導致資金加權報酬率短期波動。";

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
            : new Dictionary<string, YearEndPriceInfo>(yearEndPrices, StringComparer.OrdinalIgnoreCase);

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

        var deduplicatedMissingPrices = missingPrices
            .DistinctBy(p => (p.Ticker, p.PriceType))
            .ToList();

        if (deduplicatedMissingPrices.Count > 0)
        {
            logger.LogWarning(
                "Missing {Count} prices for year {Year} performance calculation; continuing with internal cost-basis fallback valuation where possible",
                deduplicatedMissingPrices.Count,
                year);
        }

        // ===== Calculate Source Currency (portfolio.BaseCurrency) Performance =====

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

        async Task<Dictionary<string, decimal>> BuildFallbackTickerToHomeRatesAsync(
            IReadOnlyCollection<StockPosition> positions,
            IReadOnlyDictionary<string, YearEndPriceInfo> priceLookup,
            int targetYear)
        {
            var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            foreach (var ticker in positions
                         .Where(position => !priceLookup.ContainsKey(position.Ticker))
                         .Select(position => position.Ticker)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var tickerCurrency = GetTickerCurrency(ticker);
                rates[ticker] = await ResolveExchangeRateAsync(tickerCurrency, targetYear);
            }

            return rates;
        }

        CurrentHoldingProjectionDto ProjectPositionValue(
            StockPosition position,
            DateTime valuationDate,
            IReadOnlyDictionary<string, YearEndPriceInfo> priceLookup,
            IReadOnlyDictionary<string, decimal> fallbackTickerToHomeRates)
        {
            var tickerCurrency = GetTickerCurrency(position.Ticker);

            if (priceLookup.TryGetValue(position.Ticker, out var priceInfo))
            {
                var priceInSource = ConvertAmountToSource(
                    priceInfo.Price,
                    tickerCurrency,
                    valuationDate,
                    priceInfo.ExchangeRate);

                return new CurrentHoldingProjectionDto
                {
                    Ticker = position.Ticker,
                    Shares = position.TotalShares,
                    CostSource = position.TotalCostSource,
                    CostHome = position.TotalCostHome,
                    MarketValueSource = position.TotalShares * priceInSource,
                    MarketValueHome = position.TotalShares * priceInfo.Price * priceInfo.ExchangeRate,
                    ValuationSource = PositionValuationSource.MarketPrice
                };
            }

            fallbackTickerToHomeRates.TryGetValue(position.Ticker, out var fallbackTickerToHomeRate);

            var fallbackSourceValue = position.TotalCostSource > 0m
                ? ConvertAmountToSource(
                    position.TotalCostSource,
                    tickerCurrency,
                    valuationDate,
                    fallbackTickerToHomeRate > 0m ? fallbackTickerToHomeRate : (decimal?)null)
                : 0m;

            var fallbackHomeValue = position.TotalCostHome;
            if (fallbackHomeValue <= 0m && position.TotalCostSource > 0m)
            {
                if (string.Equals(tickerCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase))
                {
                    fallbackHomeValue = position.TotalCostSource;
                }
                else if (fallbackTickerToHomeRate > 0m)
                {
                    fallbackHomeValue = position.TotalCostSource * fallbackTickerToHomeRate;
                }
            }

            var valuationSource = fallbackSourceValue > 0m || fallbackHomeValue > 0m
                ? PositionValuationSource.CostBasisFallback
                : PositionValuationSource.Unavailable;

            return new CurrentHoldingProjectionDto
            {
                Ticker = position.Ticker,
                Shares = position.TotalShares,
                CostSource = position.TotalCostSource,
                CostHome = position.TotalCostHome,
                MarketValueSource = fallbackSourceValue,
                MarketValueHome = fallbackHomeValue,
                ValuationSource = valuationSource
            };
        }

        var yearStartFallbackTickerToHomeRates = await BuildFallbackTickerToHomeRatesAsync(
            yearStartPositions,
            yearStartPrices,
            yearStartYear);

        var yearEndFallbackTickerToHomeRates = await BuildFallbackTickerToHomeRatesAsync(
            yearEndPositions,
            yearEndPrices,
            year);

        var yearStartProjectedPositions = yearStartPositions
            .Select(position => ProjectPositionValue(position, yearStart, yearStartPrices, yearStartFallbackTickerToHomeRates))
            .ToList();

        var yearEndProjectedPositions = yearEndPositions
            .Select(position => ProjectPositionValue(position, yearEnd, yearEndPrices, yearEndFallbackTickerToHomeRates))
            .ToList();

        var hasYearStartCostBasisFallbackSignal = yearStartProjectedPositions.Any(p => p.ValuationSource == PositionValuationSource.CostBasisFallback);
        var hasYearStartMissingPriceSignal = deduplicatedMissingPrices.Any(p => string.Equals(p.PriceType, "YearStart", StringComparison.OrdinalIgnoreCase));
        var hasYearStartFallbackSignal = hasYearStartMissingPriceSignal || hasYearStartCostBasisFallbackSignal;

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

        // Year-start portfolio value in source currency (internal projection with cost-basis fallback)
        var startValueSource = yearStartProjectedPositions.Sum(position => position.MarketValueSource);

        logger.LogDebug("  Total startValueSource ({SourceCurrency}): {StartValue}", sourceCurrency, startValueSource);

        if (startValueSource > 0)
        {
            logger.LogDebug("  Year-start valuation (no annual XIRR cash-flow): {Amount} on {Date}", startValueSource, yearStart);
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

                    break;
                }
            }
        }

        // Year-end portfolio value in source currency (internal projection with cost-basis fallback)
        var endValueSource = yearEndProjectedPositions.Sum(position => position.MarketValueSource);

        logger.LogDebug("  Total endValueSource ({SourceCurrency}): {EndValue}", sourceCurrency, endValueSource);

        if (endValueSource > 0)
        {
            logger.LogDebug("  Year-end valuation (no annual XIRR cash-flow): {Amount} on {Date}", endValueSource, yearEnd);
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
                currencyTransactions)
            .ToList();

        // TWR 快照路徑可在「無顯式外部現金流」時回退到股票交易事件；
        // 但 MD / NetContributions 應維持原本外部現金流資料路徑，不受 fallback 影響。
        var twrCashFlowEvents = cashFlowEvents;

        if (twrCashFlowEvents.Count == 0)
        {
            var stockFallbackEvents = new StockTransactionCashFlowStrategy().GetCashFlowEvents(
                portfolio,
                yearStart,
                yearEnd,
                validTransactions,
                ledgers,
                currencyTransactions);

            if (stockFallbackEvents.Count > 0)
            {
                twrCashFlowEvents = stockFallbackEvents.ToList();
                logger.LogInformation(
                    "No explicit external cash-flow events found for portfolio {PortfolioId} year {Year}; fallback to stock-transaction cash-flow path for TWR snapshots only.",
                    portfolioId,
                    year);
            }
        }

        var twrCashFlowEventIds = twrCashFlowEvents
            .Select(e => e.TransactionId)
            .ToHashSet();

        // Ensure snapshots exist for the year (on-demand backfill)
        await txSnapshotService.BackfillSnapshotsAsync(portfolioId, yearStart, yearEnd, cancellationToken);

        var snapshots = await txSnapshotService.GetSnapshotsAsync(portfolioId, yearStart, yearEnd, cancellationToken);

        var cashFlowEventSnapshots = snapshots
            .Where(s => twrCashFlowEventIds.Contains(s.TransactionId))
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

        if (closedLoopStartValueSource > 0m
            && hasYearStartFallbackSignal
            && closedLoopSourceSnapshots.Count > 0)
        {
            // 在 missing-price / 成本 fallback 場景，第一個現金流快照的 before 可能仍為 0（資料來源尚未套用 fallback），
            // 直接帶入 TWR 會把首段乘成 0 導致固定 -100%。
            // 保守修補：僅修補「第一段錨點快照」，避免覆寫後續可能是實際語意的零值子期間。
            var firstSnapshot = closedLoopSourceSnapshots[0];
            if (firstSnapshot.ValueBefore == 0m && firstSnapshot.ValueAfter > 0m)
            {
                closedLoopSourceSnapshots[0] = firstSnapshot with { ValueBefore = closedLoopStartValueSource };
            }
        }

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
        logger.LogDebug("  === Home Currency Calculation ({HomeCurrency}) ===", homeCurrency);

        // Year-start portfolio value in home currency (internal projection with cost-basis fallback)
        var startValueHome = 0m;
        foreach (var projection in yearStartProjectedPositions)
        {
            if (projection.ValuationSource == PositionValuationSource.Unavailable)
                continue;

            logger.LogDebug(
                "    YearStart Home ({ValuationSource}): {Ticker} x {Shares} => {Value} {HomeCurrency}",
                projection.ValuationSource,
                projection.Ticker,
                projection.Shares,
                projection.MarketValueHome,
                homeCurrency);
            startValueHome += projection.MarketValueHome;
        }

        logger.LogDebug("  Total startValueHome ({HomeCurrency}): {Value}", homeCurrency, startValueHome);

        if (startValueHome > 0)
        {
            logger.LogDebug("  Year-start home valuation (no annual XIRR cash-flow): {Amount} on {Date}", startValueHome, yearStart);
        }

        // Transactions in home currency
        foreach (var tx in yearTransactions)
        {
            switch (tx.TransactionType)
            {
                case TransactionType.Buy:
                    break;
                case TransactionType.Sell:
                {
                    break;
                }
            }
        }

        // Year-end portfolio value in home currency (internal projection with cost-basis fallback)
        var endValueHome = yearEndProjectedPositions
            .Where(position => position.ValuationSource != PositionValuationSource.Unavailable)
            .Sum(position => position.MarketValueHome);

        if (endValueHome > 0)
        {
            logger.LogDebug("  Year-end home valuation (no annual XIRR cash-flow): {Amount} on {Date}", endValueHome, yearEnd);
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

        if (closedLoopStartValueHome > 0m
            && hasYearStartFallbackSignal
            && closedLoopHomeSnapshots.Count > 0)
        {
            // 同 source 路徑：僅修補第一段錨點快照，避免擴大覆寫導致掩蓋後續真實 -100%/零值路徑。
            var firstSnapshot = closedLoopHomeSnapshots[0];
            if (firstSnapshot.ValueBefore == 0m && firstSnapshot.ValueAfter > 0m)
            {
                closedLoopHomeSnapshots[0] = firstSnapshot with { ValueBefore = closedLoopStartValueHome };
            }
        }

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

        var recentLargeInflowWarning = ResolveRecentLargeInflowWarningSignal(
            periodStart: yearStart,
            periodEnd: yearEnd,
            sourceCurrencyCashFlows: dietzCashFlowsSource,
            sourceStartValue: closedLoopStartValueSource,
            sourceEndValue: closedLoopEndValueSource,
            homeCurrencyCashFlows: dietzCashFlowsHome,
            homeStartValue: closedLoopStartValueHome,
            homeEndValue: closedLoopEndValueHome);

        var xirrReliability = ResolveXirrReliability(
            hasOpeningBaseline,
            usesPartialHistoryAssumption,
            coverageDays);

        double? xirrHome = null;
        double? xirrSource = null;
        double? xirrPercentageHome = null;
        double? xirrPercentageSource = null;

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

        logger.LogInformation("Year {Year} performance: annual XIRR disabled for yearly analysis", year);

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
            YearStartHoldingProjections = yearStartProjectedPositions,
            YearEndHoldingProjections = yearEndProjectedPositions,
            LedgerStartValueHome = ledgerStartValueHome,
            LedgerEndValueHome = ledgerEndValueHome,
            // Common
            CashFlowCount = 0,
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
            HasRecentLargeInflowWarning = recentLargeInflowWarning.ShouldWarn,
            RecentLargeInflowWarningMessage = recentLargeInflowWarning.WarningMessage,
            MissingPrices = deduplicatedMissingPrices
        };
    }

    private static RecentLargeInflowWarningSignal ResolveRecentLargeInflowWarningSignal(
        DateTime periodStart,
        DateTime periodEnd,
        IReadOnlyList<ReturnCashFlow> sourceCurrencyCashFlows,
        decimal sourceStartValue,
        decimal sourceEndValue,
        IReadOnlyList<ReturnCashFlow> homeCurrencyCashFlows,
        decimal homeStartValue,
        decimal homeEndValue)
    {
        var sourceTriggered = HasRecentLargeInflowInLastWindow(
            periodStart,
            periodEnd,
            sourceCurrencyCashFlows,
            sourceStartValue,
            sourceEndValue);

        var homeTriggered = HasRecentLargeInflowInLastWindow(
            periodStart,
            periodEnd,
            homeCurrencyCashFlows,
            homeStartValue,
            homeEndValue);

        if (!sourceTriggered && !homeTriggered)
            return RecentLargeInflowWarningSignal.None;

        return new RecentLargeInflowWarningSignal(
            ShouldWarn: true,
            WarningMessage: RecentLargeInflowWarningMessage);
    }

    private static bool HasRecentLargeInflowInLastWindow(
        DateTime periodStart,
        DateTime periodEnd,
        IReadOnlyList<ReturnCashFlow> cashFlows,
        decimal startValue,
        decimal endValue)
    {
        var startDate = periodStart.Date;
        var endDate = periodEnd.Date;
        var totalDays = (endDate - startDate).Days;

        if (totalDays <= 0)
            return false;

        var lastWindowDays = Math.Max(1, (int)Math.Ceiling(totalDays * (double)RecentLargeInflowPeriodRatio));
        var lastWindowStartDate = endDate.AddDays(-(lastWindowDays - 1));

        var periodTotalAssets = Math.Max(startValue, endValue);
        if (periodTotalAssets <= 0m)
            return false;

        var inflowThreshold = periodTotalAssets * RecentLargeInflowThresholdRatio;

        return cashFlows.Any(cashFlow =>
            cashFlow.Amount > inflowThreshold
            && cashFlow.Amount > 0m
            && cashFlow.Date.Date >= lastWindowStartDate
            && cashFlow.Date.Date <= endDate);
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
        int? coverageDays)
    {
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

    private readonly record struct RecentLargeInflowWarningSignal(
        bool ShouldWarn,
        string? WarningMessage)
    {
        public static RecentLargeInflowWarningSignal None { get; } = new(
            ShouldWarn: false,
            WarningMessage: null);
    }
}
