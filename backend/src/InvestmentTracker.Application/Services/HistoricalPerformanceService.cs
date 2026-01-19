using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Application.Services;

/// <summary>
/// 歷史年度績效計算服務。
/// 計算任一年度（2020+）的 XIRR 與總報酬。
/// </summary>
public class HistoricalPerformanceService : IHistoricalPerformanceService
{
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IStockTransactionRepository _transactionRepository;
    private readonly IStockSplitRepository _stockSplitRepository;
    private readonly PortfolioCalculator _portfolioCalculator;
    private readonly StockSplitAdjustmentService _splitAdjustmentService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<HistoricalPerformanceService> _logger;

    public HistoricalPerformanceService(
        IPortfolioRepository portfolioRepository,
        IStockTransactionRepository transactionRepository,
        IStockSplitRepository stockSplitRepository,
        PortfolioCalculator portfolioCalculator,
        StockSplitAdjustmentService splitAdjustmentService,
        ICurrentUserService currentUserService,
        ILogger<HistoricalPerformanceService> logger)
    {
        _portfolioRepository = portfolioRepository;
        _transactionRepository = transactionRepository;
        _stockSplitRepository = stockSplitRepository;
        _portfolioCalculator = portfolioCalculator;
        _splitAdjustmentService = splitAdjustmentService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    /// <summary>
    /// 取得可計算績效的年份清單。
    /// </summary>
    public async Task<AvailableYearsDto> GetAvailableYearsAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await _portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new InvalidOperationException($"Portfolio {portfolioId} not found");

        if (portfolio.UserId != _currentUserService.UserId)
        {
            throw new UnauthorizedAccessException("You do not have access to this portfolio");
        }

        var transactions = await _transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
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
        var portfolio = await _portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new InvalidOperationException($"Portfolio {portfolioId} not found");

        if (portfolio.UserId != _currentUserService.UserId)
        {
            throw new UnauthorizedAccessException("You do not have access to this portfolio");
        }

        var year = request.Year;
        var currentYear = DateTime.UtcNow.Year;
        var isYtd = year == currentYear;
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = isYtd ? DateTime.UtcNow.Date : new DateTime(year, 12, 31);
        // For asking user prices, use previous year end (Dec 31 of year-1)
        var priceReferenceDate = new DateTime(year - 1, 12, 31);

        _logger.LogInformation("Calculating performance for portfolio {PortfolioId}, year {Year}", portfolioId, year);

        var allTransactions = await _transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var stockSplits = await _stockSplitRepository.GetAllAsync(cancellationToken);

        // Filter to valid transactions only
        var validTransactions = allTransactions.Where(t => !t.IsDeleted && t.HasExchangeRate).ToList();

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

        // Calculate positions at year end
        var yearEndPositions = _portfolioCalculator.RecalculateAllPositionsWithSplitAdjustments(
            transactionsUpToYearEnd, stockSplits, _splitAdjustmentService).ToList();

        // Calculate positions at year start
        var yearStartPositions = _portfolioCalculator.RecalculateAllPositionsWithSplitAdjustments(
            transactionsUpToYearStart, stockSplits, _splitAdjustmentService).ToList();

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
            _logger.LogWarning("Missing {Count} prices for year {Year} performance calculation", missingPrices.Count, year);

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

        // Year-start portfolio value in source currency
        decimal startValueSource = 0m;
        foreach (var position in yearStartPositions)
        {
            if (yearStartPrices.TryGetValue(position.Ticker, out var priceInfo))
            {
                startValueSource += position.TotalShares * priceInfo.Price; // No exchange rate
            }
        }

        if (startValueSource > 0)
        {
            cashFlowsSource.Add(new CashFlow(-startValueSource, yearStart));
        }

        // Transactions in source currency
        foreach (var tx in yearTransactions)
        {
            if (tx.TransactionType == TransactionType.Buy)
            {
                cashFlowsSource.Add(new CashFlow(-tx.TotalCostSource, tx.TransactionDate));
            }
            else if (tx.TransactionType == TransactionType.Sell)
            {
                var proceeds = (tx.Shares * tx.PricePerShare) - tx.Fees;
                cashFlowsSource.Add(new CashFlow(proceeds, tx.TransactionDate));
            }
        }

        // Year-end portfolio value in source currency
        decimal endValueSource = 0m;
        foreach (var position in yearEndPositions)
        {
            if (yearEndPrices.TryGetValue(position.Ticker, out var priceInfo))
            {
                endValueSource += position.TotalShares * priceInfo.Price; // No exchange rate
            }
        }

        if (endValueSource > 0)
        {
            cashFlowsSource.Add(new CashFlow(endValueSource, yearEnd));
        }

        // Net contributions in source currency
        decimal netContributionsSource = yearTransactions
            .Where(t => t.TransactionType == TransactionType.Buy)
            .Sum(t => t.TotalCostSource)
            - yearTransactions
            .Where(t => t.TransactionType == TransactionType.Sell)
            .Sum(t => (t.Shares * t.PricePerShare) - t.Fees);

        // Calculate source currency XIRR
        double? xirrSource = null;
        if (cashFlowsSource.Count >= 2)
        {
            xirrSource = _portfolioCalculator.CalculateXirr(cashFlowsSource);
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

        // Year-start portfolio value in home currency
        decimal startValueHome = 0m;
        foreach (var position in yearStartPositions)
        {
            if (yearStartPrices.TryGetValue(position.Ticker, out var priceInfo))
            {
                startValueHome += position.TotalShares * priceInfo.Price * priceInfo.ExchangeRate;
            }
        }

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
        decimal endValueHome = 0m;
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
        decimal netContributionsHome = yearTransactions
            .Where(t => t.TransactionType == TransactionType.Buy)
            .Sum(t => t.TotalCostHome ?? 0)
            - yearTransactions
            .Where(t => t.TransactionType == TransactionType.Sell)
            .Sum(t => (t.Shares * t.PricePerShare * (t.ExchangeRate ?? 1)) - (t.Fees * (t.ExchangeRate ?? 1)));

        // Calculate home currency XIRR
        double? xirrHome = null;
        if (cashFlowsHome.Count >= 2)
        {
            xirrHome = _portfolioCalculator.CalculateXirr(cashFlowsHome);
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

        _logger.LogInformation("Year {Year} performance: Source XIRR={XirrSource}%, Home XIRR={XirrHome}%",
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
}
