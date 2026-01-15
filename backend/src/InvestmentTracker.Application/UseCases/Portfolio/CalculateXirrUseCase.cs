using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// Use case for calculating XIRR (Extended Internal Rate of Return) for a portfolio.
/// Applies stock split adjustments when calculating current positions for accurate comparison with current prices.
/// </summary>
public class CalculateXirrUseCase
{
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IStockTransactionRepository _transactionRepository;
    private readonly IStockSplitRepository _stockSplitRepository;
    private readonly PortfolioCalculator _portfolioCalculator;
    private readonly StockSplitAdjustmentService _splitAdjustmentService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CalculateXirrUseCase> _logger;

    public CalculateXirrUseCase(
        IPortfolioRepository portfolioRepository,
        IStockTransactionRepository transactionRepository,
        IStockSplitRepository stockSplitRepository,
        PortfolioCalculator portfolioCalculator,
        StockSplitAdjustmentService splitAdjustmentService,
        ICurrentUserService currentUserService,
        ILogger<CalculateXirrUseCase> logger)
    {
        _portfolioRepository = portfolioRepository;
        _transactionRepository = transactionRepository;
        _stockSplitRepository = stockSplitRepository;
        _portfolioCalculator = portfolioCalculator;
        _splitAdjustmentService = splitAdjustmentService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<XirrResultDto> ExecuteAsync(
        Guid portfolioId,
        CalculateXirrRequest request,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await _portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new InvalidOperationException($"Portfolio {portfolioId} not found");

        if (portfolio.UserId != _currentUserService.UserId)
        {
            throw new UnauthorizedAccessException("You do not have access to this portfolio");
        }

        var transactions = await _transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var stockSplits = await _stockSplitRepository.GetAllAsync(cancellationToken);

        // Build cash flows list
        // FR-004: Only include transactions WITH exchange rate in TWD-based XIRR calculation
        var cashFlows = new List<CashFlow>();

        foreach (var tx in transactions.Where(t => !t.IsDeleted && t.HasExchangeRate).OrderBy(t => t.TransactionDate))
        {
            if (tx.TransactionType == TransactionType.Buy)
            {
                // Outflow (investment) - TotalCostHome is guaranteed non-null when HasExchangeRate is true
                cashFlows.Add(new CashFlow(-tx.TotalCostHome!.Value, tx.TransactionDate));
            }
            else if (tx.TransactionType == TransactionType.Sell)
            {
                // Inflow (return) - ExchangeRate is guaranteed non-null when HasExchangeRate is true
                var proceeds = (tx.Shares * tx.PricePerShare * tx.ExchangeRate!.Value) - (tx.Fees * tx.ExchangeRate!.Value);
                cashFlows.Add(new CashFlow(proceeds, tx.TransactionDate));
            }
        }

        // Add current portfolio value as final cash flow
        if (request.CurrentPrices != null && request.CurrentPrices.Count > 0)
        {
            _logger.LogDebug("XIRR: Received {Count} current prices", request.CurrentPrices.Count);

            // Convert to case-insensitive dictionary for reliable ticker matching
            var currentPrices = new Dictionary<string, CurrentPriceInfo>(
                request.CurrentPrices, StringComparer.OrdinalIgnoreCase);

            // Use split-adjusted positions for accurate comparison with current prices
            var positions = _portfolioCalculator.RecalculateAllPositionsWithSplitAdjustments(
                transactions, stockSplits, _splitAdjustmentService).ToList();

            _logger.LogDebug("XIRR: Found {Count} positions", positions.Count);

            decimal currentValue = 0m;

            foreach (var position in positions)
            {
                if (currentPrices.TryGetValue(position.Ticker, out var priceInfo))
                {
                    var positionValue = position.TotalShares * priceInfo.Price * priceInfo.ExchangeRate;
                    _logger.LogDebug("XIRR: Position {Ticker}: {Shares} shares * {Price} * {Rate} = {Value}",
                        position.Ticker, position.TotalShares, priceInfo.Price, priceInfo.ExchangeRate, positionValue);
                    currentValue += positionValue;
                }
                else
                {
                    _logger.LogDebug("XIRR: No price found for position {Ticker}. Available prices: {Keys}",
                        position.Ticker, string.Join(", ", currentPrices.Keys));
                }
            }

            _logger.LogDebug("XIRR: Total current value = {Value}", currentValue);

            if (currentValue > 0)
            {
                cashFlows.Add(new CashFlow(currentValue, request.AsOfDate ?? DateTime.UtcNow.Date));
            }
        }
        else
        {
            _logger.LogDebug("XIRR: No current prices provided. CurrentPrices is {Status}",
                request.CurrentPrices == null ? "null" : $"empty (count: {request.CurrentPrices.Count})");
        }

        _logger.LogDebug("XIRR: Total cash flows = {Count}", cashFlows.Count);

        var xirr = _portfolioCalculator.CalculateXirr(cashFlows);

        return new XirrResultDto
        {
            Xirr = xirr,
            XirrPercentage = xirr.HasValue ? xirr.Value * 100 : null,
            CashFlowCount = cashFlows.Count,
            AsOfDate = request.AsOfDate ?? DateTime.UtcNow.Date
        };
    }

    /// <summary>
    /// Calculate XIRR for a single position (ticker).
    /// </summary>
    public async Task<XirrResultDto> ExecuteForPositionAsync(
        Guid portfolioId,
        string ticker,
        CalculatePositionXirrRequest request,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await _portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new InvalidOperationException($"Portfolio {portfolioId} not found");

        if (portfolio.UserId != _currentUserService.UserId)
        {
            throw new UnauthorizedAccessException("You do not have access to this portfolio");
        }

        var allTransactions = await _transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var stockSplits = await _stockSplitRepository.GetAllAsync(cancellationToken);

        // Filter to only this ticker's transactions
        var tickerTransactions = allTransactions
            .Where(t => !t.IsDeleted && t.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.TransactionDate)
            .ToList();

        if (tickerTransactions.Count == 0)
        {
            return new XirrResultDto
            {
                Xirr = null,
                XirrPercentage = null,
                CashFlowCount = 0,
                AsOfDate = request.AsOfDate ?? DateTime.UtcNow.Date
            };
        }

        // Build cash flows list for this position
        // FR-004: Only include transactions WITH exchange rate in TWD-based XIRR calculation
        var cashFlows = new List<CashFlow>();

        foreach (var tx in tickerTransactions.Where(t => t.HasExchangeRate))
        {
            if (tx.TransactionType == TransactionType.Buy)
            {
                // Outflow (investment) - TotalCostHome is guaranteed non-null when HasExchangeRate is true
                cashFlows.Add(new CashFlow(-tx.TotalCostHome!.Value, tx.TransactionDate));
            }
            else if (tx.TransactionType == TransactionType.Sell)
            {
                // Inflow (return) - ExchangeRate is guaranteed non-null when HasExchangeRate is true
                var proceeds = (tx.Shares * tx.PricePerShare * tx.ExchangeRate!.Value) - (tx.Fees * tx.ExchangeRate!.Value);
                cashFlows.Add(new CashFlow(proceeds, tx.TransactionDate));
            }
        }

        // Add current position value as final cash flow
        if (request.CurrentPrice.HasValue && request.CurrentExchangeRate.HasValue)
        {
            // Use split-adjusted position for accurate comparison with current price
            var position = _portfolioCalculator.CalculatePositionWithSplitAdjustments(
                ticker, allTransactions, stockSplits, _splitAdjustmentService);

            if (position.TotalShares > 0)
            {
                var currentValue = position.TotalShares * request.CurrentPrice.Value * request.CurrentExchangeRate.Value;
                cashFlows.Add(new CashFlow(currentValue, request.AsOfDate ?? DateTime.UtcNow.Date));
            }
        }

        var xirr = _portfolioCalculator.CalculateXirr(cashFlows);

        return new XirrResultDto
        {
            Xirr = xirr,
            XirrPercentage = xirr.HasValue ? xirr.Value * 100 : null,
            CashFlowCount = cashFlows.Count,
            AsOfDate = request.AsOfDate ?? DateTime.UtcNow.Date
        };
    }
}
