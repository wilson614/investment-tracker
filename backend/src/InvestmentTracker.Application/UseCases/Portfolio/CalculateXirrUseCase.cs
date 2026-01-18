using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// Use case for calculating XIRR (Extended Internal Rate of Return) for a portfolio.
/// Applies stock split adjustments when calculating current positions for accurate comparison with current prices.
/// US9: Uses transaction-date FX cache for auto-filling missing exchange rates.
/// </summary>
public class CalculateXirrUseCase
{
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IStockTransactionRepository _transactionRepository;
    private readonly IStockSplitRepository _stockSplitRepository;
    private readonly PortfolioCalculator _portfolioCalculator;
    private readonly StockSplitAdjustmentService _splitAdjustmentService;
    private readonly ITransactionDateExchangeRateService _txDateFxService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CalculateXirrUseCase> _logger;

    public CalculateXirrUseCase(
        IPortfolioRepository portfolioRepository,
        IStockTransactionRepository transactionRepository,
        IStockSplitRepository stockSplitRepository,
        PortfolioCalculator portfolioCalculator,
        StockSplitAdjustmentService splitAdjustmentService,
        ITransactionDateExchangeRateService txDateFxService,
        ICurrentUserService currentUserService,
        ILogger<CalculateXirrUseCase> logger)
    {
        _portfolioRepository = portfolioRepository;
        _transactionRepository = transactionRepository;
        _stockSplitRepository = stockSplitRepository;
        _portfolioCalculator = portfolioCalculator;
        _splitAdjustmentService = splitAdjustmentService;
        _txDateFxService = txDateFxService;
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

        // Build cash flows list - include ALL non-deleted transactions
        // US9: Auto-fill exchange rates for transactions missing them
        var cashFlows = new List<CashFlow>();
        var missingFxDates = new List<MissingExchangeRateDto>();

        var orderedTransactions = transactions
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.TransactionDate)
            .ToList();

        foreach (var tx in orderedTransactions)
        {
            var fxRate = await GetExchangeRateForTransactionAsync(tx, cancellationToken);
            
            if (!fxRate.HasValue)
            {
                // Track missing FX rate for reporting
                var currency = tx.IsTaiwanStock ? "TWD" : "USD"; // Default assumption
                missingFxDates.Add(new MissingExchangeRateDto
                {
                    TransactionDate = tx.TransactionDate,
                    Currency = currency
                });
                _logger.LogWarning("Missing exchange rate for transaction {TxId} on {Date}",
                    tx.Id, tx.TransactionDate.ToString("yyyy-MM-dd"));
                continue; // Skip this transaction in XIRR calculation
            }

            if (tx.TransactionType == TransactionType.Buy)
            {
                // Use home currency cost (TotalCostSource * ExchangeRate)
                var homeCost = tx.TotalCostSource * fxRate.Value;
                cashFlows.Add(new CashFlow(-homeCost, tx.TransactionDate));
            }
            else if (tx.TransactionType == TransactionType.Sell)
            {
                // Use home currency proceeds
                var proceeds = ((tx.Shares * tx.PricePerShare) - tx.Fees) * fxRate.Value;
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
                    // Use home currency value (price * shares * current exchange rate)
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
            AsOfDate = request.AsOfDate ?? DateTime.UtcNow.Date,
            MissingExchangeRates = missingFxDates.Count > 0 ? missingFxDates : null
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

        // Build cash flows list - include ALL transactions, auto-fill FX
        var cashFlows = new List<CashFlow>();
        var missingFxDates = new List<MissingExchangeRateDto>();

        foreach (var tx in tickerTransactions)
        {
            var fxRate = await GetExchangeRateForTransactionAsync(tx, cancellationToken);
            
            if (!fxRate.HasValue)
            {
                var currency = tx.IsTaiwanStock ? "TWD" : "USD";
                missingFxDates.Add(new MissingExchangeRateDto
                {
                    TransactionDate = tx.TransactionDate,
                    Currency = currency
                });
                continue;
            }

            if (tx.TransactionType == TransactionType.Buy)
            {
                var homeCost = tx.TotalCostSource * fxRate.Value;
                cashFlows.Add(new CashFlow(-homeCost, tx.TransactionDate));
            }
            else if (tx.TransactionType == TransactionType.Sell)
            {
                var proceeds = ((tx.Shares * tx.PricePerShare) - tx.Fees) * fxRate.Value;
                cashFlows.Add(new CashFlow(proceeds, tx.TransactionDate));
            }
        }

        // Add current position value as final cash flow
        if (request.CurrentPrice.HasValue)
        {
            // Use split-adjusted position for accurate comparison with current price
            var position = _portfolioCalculator.CalculatePositionWithSplitAdjustments(
                ticker, allTransactions, stockSplits, _splitAdjustmentService);

            if (position.TotalShares > 0)
            {
                // Use home currency value
                var currentValue = position.TotalShares * request.CurrentPrice.Value * (request.CurrentExchangeRate ?? 1m);
                cashFlows.Add(new CashFlow(currentValue, request.AsOfDate ?? DateTime.UtcNow.Date));
            }
        }

        var xirr = _portfolioCalculator.CalculateXirr(cashFlows);

        return new XirrResultDto
        {
            Xirr = xirr,
            XirrPercentage = xirr.HasValue ? xirr.Value * 100 : null,
            CashFlowCount = cashFlows.Count,
            AsOfDate = request.AsOfDate ?? DateTime.UtcNow.Date,
            MissingExchangeRates = missingFxDates.Count > 0 ? missingFxDates : null
        };
    }

    /// <summary>
    /// Gets exchange rate for a transaction:
    /// 1. Use transaction's stored ExchangeRate if available
    /// 2. For TWD (IsTaiwanStock), return 1.0
    /// 3. Otherwise, try to fetch from transaction-date FX cache
    /// </summary>
    private async Task<decimal?> GetExchangeRateForTransactionAsync(
        StockTransaction tx,
        CancellationToken cancellationToken)
    {
        // If transaction already has exchange rate, use it
        if (tx.HasExchangeRate)
        {
            return tx.ExchangeRate!.Value;
        }

        // Taiwan stocks are in TWD, no conversion needed
        if (tx.IsTaiwanStock)
        {
            return 1.0m;
        }

        // Try to get from transaction-date FX cache
        // Default to USD for non-Taiwan stocks (most common foreign currency)
        var fxResult = await _txDateFxService.GetOrFetchAsync(
            "USD", "TWD", tx.TransactionDate, cancellationToken);

        return fxResult?.Rate;
    }
}
