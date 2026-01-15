using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Application.Services;

/// <summary>
/// Service for calculating historical year performance.
/// Calculates XIRR and returns for any historical year (2020+).
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
    /// Get available years for performance calculation.
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
    /// Calculate performance for a specific year.
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

        // Check year-end prices for positions (use previous year Dec 31 for price reference)
        foreach (var position in yearEndPositions)
        {
            if (!yearEndPrices.ContainsKey(position.Ticker))
            {
                missingPrices.Add(new MissingPriceDto
                {
                    Ticker = position.Ticker,
                    Date = priceReferenceDate,
                    PriceType = "YearStart"
                });
            }
        }

        // Check year-start prices for positions that existed at year start
        foreach (var position in yearStartPositions)
        {
            if (!yearEndPrices.ContainsKey(position.Ticker))
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
                MissingPrices = missingPrices.DistinctBy(p => p.Ticker).ToList(),
                CashFlowCount = 0
            };
        }

        // Build cash flows for XIRR calculation
        var cashFlows = new List<CashFlow>();

        // Add year-start portfolio value as initial inflow (if we had positions)
        decimal startValue = 0m;
        foreach (var position in yearStartPositions)
        {
            if (yearEndPrices.TryGetValue(position.Ticker, out var priceInfo))
            {
                // Use year-start prices (same prices dict for simplicity - user should provide both)
                startValue += position.TotalShares * priceInfo.Price * priceInfo.ExchangeRate;
            }
        }

        if (startValue > 0)
        {
            // Starting value is treated as an inflow at year start
            cashFlows.Add(new CashFlow(-startValue, yearStart));
        }

        // Add transactions during the year
        foreach (var tx in yearTransactions)
        {
            if (tx.TransactionType == TransactionType.Buy)
            {
                // Outflow (investment)
                cashFlows.Add(new CashFlow(-tx.TotalCostHome!.Value, tx.TransactionDate));
            }
            else if (tx.TransactionType == TransactionType.Sell)
            {
                // Inflow (return)
                var proceeds = (tx.Shares * tx.PricePerShare * tx.ExchangeRate!.Value) - (tx.Fees * tx.ExchangeRate!.Value);
                cashFlows.Add(new CashFlow(proceeds, tx.TransactionDate));
            }
        }

        // Add year-end portfolio value as final outflow
        decimal endValue = 0m;
        foreach (var position in yearEndPositions)
        {
            if (yearEndPrices.TryGetValue(position.Ticker, out var priceInfo))
            {
                endValue += position.TotalShares * priceInfo.Price * priceInfo.ExchangeRate;
            }
        }

        if (endValue > 0)
        {
            cashFlows.Add(new CashFlow(endValue, yearEnd));
        }

        // Calculate net contributions
        decimal netContributions = yearTransactions
            .Where(t => t.TransactionType == TransactionType.Buy)
            .Sum(t => t.TotalCostHome ?? 0)
            - yearTransactions
            .Where(t => t.TransactionType == TransactionType.Sell)
            .Sum(t => (t.Shares * t.PricePerShare * (t.ExchangeRate ?? 1)) - (t.Fees * (t.ExchangeRate ?? 1)));

        // Calculate XIRR
        double? xirr = null;
        if (cashFlows.Count >= 2)
        {
            xirr = _portfolioCalculator.CalculateXirr(cashFlows);
        }

        // Calculate simple total return
        double? totalReturn = null;
        if (startValue > 0)
        {
            totalReturn = (double)((endValue - startValue - netContributions) / startValue) * 100;
        }
        else if (netContributions > 0)
        {
            totalReturn = (double)((endValue - netContributions) / netContributions) * 100;
        }

        _logger.LogInformation("Year {Year} performance: XIRR={Xirr}, TotalReturn={Return}%",
            year, xirr.HasValue ? xirr.Value * 100 : null, totalReturn);

        return new YearPerformanceDto
        {
            Year = year,
            Xirr = xirr,
            XirrPercentage = xirr.HasValue ? xirr.Value * 100 : null,
            TotalReturnPercentage = totalReturn,
            StartValueHome = startValue > 0 ? startValue : null,
            EndValueHome = endValue > 0 ? endValue : null,
            NetContributionsHome = netContributions,
            CashFlowCount = cashFlows.Count,
            MissingPrices = []
        };
    }
}
