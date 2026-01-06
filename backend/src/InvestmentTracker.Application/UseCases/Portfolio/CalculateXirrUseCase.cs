using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// Use case for calculating XIRR (Extended Internal Rate of Return) for a portfolio.
/// </summary>
public class CalculateXirrUseCase
{
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IStockTransactionRepository _transactionRepository;
    private readonly PortfolioCalculator _portfolioCalculator;
    private readonly ICurrentUserService _currentUserService;

    public CalculateXirrUseCase(
        IPortfolioRepository portfolioRepository,
        IStockTransactionRepository transactionRepository,
        PortfolioCalculator portfolioCalculator,
        ICurrentUserService currentUserService)
    {
        _portfolioRepository = portfolioRepository;
        _transactionRepository = transactionRepository;
        _portfolioCalculator = portfolioCalculator;
        _currentUserService = currentUserService;
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

        // Build cash flows list
        var cashFlows = new List<CashFlow>();

        foreach (var tx in transactions.Where(t => !t.IsDeleted).OrderBy(t => t.TransactionDate))
        {
            if (tx.TransactionType == TransactionType.Buy)
            {
                // Outflow (investment)
                cashFlows.Add(new CashFlow(-tx.TotalCostHome, tx.TransactionDate));
            }
            else if (tx.TransactionType == TransactionType.Sell)
            {
                // Inflow (return)
                var proceeds = (tx.Shares * tx.PricePerShare * tx.ExchangeRate) - (tx.Fees * tx.ExchangeRate);
                cashFlows.Add(new CashFlow(proceeds, tx.TransactionDate));
            }
        }

        // Add current portfolio value as final cash flow
        if (request.CurrentPrices != null && request.CurrentPrices.Count > 0)
        {
            var positions = _portfolioCalculator.RecalculateAllPositions(transactions).ToList();
            decimal currentValue = 0m;

            foreach (var position in positions)
            {
                if (request.CurrentPrices.TryGetValue(position.Ticker, out var priceInfo))
                {
                    currentValue += position.TotalShares * priceInfo.Price * priceInfo.ExchangeRate;
                }
            }

            if (currentValue > 0)
            {
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
