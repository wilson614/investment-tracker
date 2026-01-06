using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// Use case for getting portfolio summary with calculated positions.
/// </summary>
public class GetPortfolioSummaryUseCase
{
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IStockTransactionRepository _transactionRepository;
    private readonly PortfolioCalculator _portfolioCalculator;
    private readonly ICurrentUserService _currentUserService;

    public GetPortfolioSummaryUseCase(
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

    public async Task<PortfolioSummaryDto> ExecuteAsync(
        Guid portfolioId,
        CalculatePerformanceRequest? performanceRequest = null,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await _portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new InvalidOperationException($"Portfolio {portfolioId} not found");

        if (portfolio.UserId != _currentUserService.UserId)
        {
            throw new UnauthorizedAccessException("You do not have access to this portfolio");
        }

        var transactions = await _transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var positions = _portfolioCalculator.RecalculateAllPositions(transactions);

        var positionDtos = new List<StockPositionDto>();
        decimal totalCostHome = 0m;
        decimal? totalValueHome = null;
        decimal? totalUnrealizedPnl = null;

        foreach (var position in positions.Where(p => p.TotalShares > 0))
        {
            var dto = new StockPositionDto
            {
                Ticker = position.Ticker,
                TotalShares = position.TotalShares,
                TotalCostHome = position.TotalCostHome,
                AverageCostPerShare = position.AverageCostPerShare
            };

            // If current prices provided, calculate unrealized PnL
            if (performanceRequest?.CurrentPrices?.TryGetValue(position.Ticker, out var priceInfo) == true)
            {
                var pnl = _portfolioCalculator.CalculateUnrealizedPnl(
                    position, priceInfo.Price, priceInfo.ExchangeRate);

                dto = dto with
                {
                    CurrentPrice = priceInfo.Price,
                    CurrentExchangeRate = priceInfo.ExchangeRate,
                    CurrentValueHome = pnl.CurrentValueHome,
                    UnrealizedPnlHome = pnl.UnrealizedPnlHome,
                    UnrealizedPnlPercentage = pnl.UnrealizedPnlPercentage
                };

                totalValueHome = (totalValueHome ?? 0) + pnl.CurrentValueHome;
                totalUnrealizedPnl = (totalUnrealizedPnl ?? 0) + pnl.UnrealizedPnlHome;
            }

            totalCostHome += position.TotalCostHome;
            positionDtos.Add(dto);
        }

        var portfolioDto = new PortfolioDto
        {
            Id = portfolio.Id,
            Name = portfolio.Name,
            Description = portfolio.Description,
            BaseCurrency = portfolio.BaseCurrency,
            HomeCurrency = portfolio.HomeCurrency,
            IsActive = portfolio.IsActive,
            CreatedAt = portfolio.CreatedAt,
            UpdatedAt = portfolio.UpdatedAt
        };

        return new PortfolioSummaryDto
        {
            Portfolio = portfolioDto,
            Positions = positionDtos.OrderBy(p => p.Ticker).ToList(),
            TotalCostHome = totalCostHome,
            TotalValueHome = totalValueHome,
            TotalUnrealizedPnlHome = totalUnrealizedPnl,
            TotalUnrealizedPnlPercentage = totalCostHome > 0 && totalUnrealizedPnl.HasValue
                ? (totalUnrealizedPnl.Value / totalCostHome) * 100
                : null
        };
    }
}
