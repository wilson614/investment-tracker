using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// Use case for getting portfolio summary with calculated positions.
/// Applies stock split adjustments when calculating positions for accurate comparison with current prices.
/// </summary>
public class GetPortfolioSummaryUseCase
{
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IStockTransactionRepository _transactionRepository;
    private readonly IStockSplitRepository _stockSplitRepository;
    private readonly PortfolioCalculator _portfolioCalculator;
    private readonly StockSplitAdjustmentService _splitAdjustmentService;
    private readonly ICurrentUserService _currentUserService;

    public GetPortfolioSummaryUseCase(
        IPortfolioRepository portfolioRepository,
        IStockTransactionRepository transactionRepository,
        IStockSplitRepository stockSplitRepository,
        PortfolioCalculator portfolioCalculator,
        StockSplitAdjustmentService splitAdjustmentService,
        ICurrentUserService currentUserService)
    {
        _portfolioRepository = portfolioRepository;
        _transactionRepository = transactionRepository;
        _stockSplitRepository = stockSplitRepository;
        _portfolioCalculator = portfolioCalculator;
        _splitAdjustmentService = splitAdjustmentService;
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
        var stockSplits = await _stockSplitRepository.GetAllAsync(cancellationToken);

        // Use split-adjusted positions for accurate comparison with current prices (FR-052)
        var positions = _portfolioCalculator.RecalculateAllPositionsWithSplitAdjustments(
            transactions, stockSplits, _splitAdjustmentService);

        // Convert to case-insensitive dictionary for reliable ticker matching
        var currentPrices = performanceRequest?.CurrentPrices != null
            ? new Dictionary<string, CurrentPriceInfo>(
                performanceRequest.CurrentPrices, StringComparer.OrdinalIgnoreCase)
            : null;

        // For ForeignCurrency portfolios, all metrics are calculated in source currency (no exchange rate conversion)
        var isForeignCurrencyPortfolio = portfolio.PortfolioType == PortfolioType.ForeignCurrency;

        var positionDtos = new List<StockPositionDto>();
        // totalCostHome: only includes positions that have quotes (for accurate PnL percentage)
        decimal totalCostHome = 0m;
        decimal? totalValueHome = null;
        decimal? totalUnrealizedPnl = null;

        foreach (var position in positions.Where(p => p.TotalShares > 0))
        {
            var hasHomeCost = position.TotalCostHome > 0;
            // Only treat home-currency metrics as available when there is at least one transaction with ExchangeRate
            // For ForeignCurrency portfolios, we always use source currency, so treat as having "exchange rate" for consistency
            var hasAnyExchangeRate = isForeignCurrencyPortfolio || transactions.Any(t =>
                !t.IsDeleted &&
                t.Ticker.Equals(position.Ticker, StringComparison.OrdinalIgnoreCase) &&
                t.HasExchangeRate &&
                (t.TransactionType == TransactionType.Buy || t.TransactionType == TransactionType.Adjustment));

            StockPositionDto dto;

            if (isForeignCurrencyPortfolio)
            {
                // ForeignCurrency portfolio: use source currency for all metrics
                dto = new StockPositionDto
                {
                    Ticker = position.Ticker,
                    TotalShares = position.TotalShares,
                    // For FC portfolios, display source values in the "Home" fields since they're the primary display
                    TotalCostHome = position.TotalCostSource,
                    TotalCostSource = position.TotalCostSource,
                    AverageCostPerShareHome = position.AverageCostPerShareSource,
                    AverageCostPerShareSource = position.AverageCostPerShareSource
                };
            }
            else
            {
                dto = new StockPositionDto
                {
                    Ticker = position.Ticker,
                    TotalShares = position.TotalShares,
                    TotalCostHome = hasAnyExchangeRate ? position.TotalCostHome : null,
                    TotalCostSource = position.TotalCostSource,
                    AverageCostPerShareHome = hasAnyExchangeRate ? position.AverageCostPerShareHome : null,
                    AverageCostPerShareSource = position.AverageCostPerShareSource
                };
            }

            // Track whether this position contributes to PnL totals
            var contributesToPnl = false;

            // If current prices provided, calculate unrealized PnL
            if (currentPrices?.TryGetValue(position.Ticker, out var priceInfo) == true)
            {
                if (isForeignCurrencyPortfolio)
                {
                    // ForeignCurrency portfolio: calculate PnL in source currency (exchange rate = 1)
                    var currentValueSource = position.TotalShares * priceInfo.Price;
                    var unrealizedPnlSource = currentValueSource - position.TotalCostSource;
                    var unrealizedPnlPercentage = position.TotalCostSource > 0
                        ? (unrealizedPnlSource / position.TotalCostSource) * 100
                        : null as decimal?;

                    dto = dto with
                    {
                        CurrentPrice = priceInfo.Price,
                        CurrentExchangeRate = 1m, // FC portfolios don't use exchange rates
                        CurrentValueHome = currentValueSource,
                        UnrealizedPnlHome = unrealizedPnlSource,
                        UnrealizedPnlPercentage = unrealizedPnlPercentage
                    };

                    totalValueHome = (totalValueHome ?? 0) + currentValueSource;
                    totalUnrealizedPnl = (totalUnrealizedPnl ?? 0) + unrealizedPnlSource;
                    contributesToPnl = true;
                }
                else if (hasAnyExchangeRate)
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
                    contributesToPnl = true;
                }
                else
                {
                    dto = dto with
                    {
                        CurrentPrice = priceInfo.Price,
                        CurrentExchangeRate = priceInfo.ExchangeRate,
                        CurrentValueHome = null,
                        UnrealizedPnlHome = null,
                        UnrealizedPnlPercentage = null
                    };
                }
            }

            // Only add to totalCostHome if this position contributes to PnL calculations
            // This ensures TotalCost and TotalValue are comparable for percentage calculation
            if (contributesToPnl)
            {
                if (isForeignCurrencyPortfolio)
                {
                    totalCostHome += position.TotalCostSource;
                }
                else if (hasAnyExchangeRate)
                {
                    totalCostHome += position.TotalCostHome;
                }
            }

            positionDtos.Add(dto);
        }

        var portfolioDto = new PortfolioDto
        {
            Id = portfolio.Id,
            Description = portfolio.Description,
            BaseCurrency = portfolio.BaseCurrency,
            HomeCurrency = portfolio.HomeCurrency,
            IsActive = portfolio.IsActive,
            PortfolioType = portfolio.PortfolioType,
            DisplayName = portfolio.DisplayName,
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
