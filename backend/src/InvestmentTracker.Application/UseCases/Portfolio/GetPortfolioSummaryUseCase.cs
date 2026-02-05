using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// 取得投資組合摘要（包含計算後的持倉）的 Use Case。
/// 會在計算持倉時套用拆股調整，以確保與現價比較一致。
/// </summary>
public class GetPortfolioSummaryUseCase(
    IPortfolioRepository portfolioRepository,
    IStockTransactionRepository transactionRepository,
    IStockSplitRepository stockSplitRepository,
    PortfolioCalculator portfolioCalculator,
    StockSplitAdjustmentService splitAdjustmentService,
    ICurrentUserService currentUserService)
{
    public async Task<PortfolioSummaryDto> ExecuteAsync(
        Guid portfolioId,
        CalculatePerformanceRequest? performanceRequest = null,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException($"Portfolio {portfolioId} not found");

        if (portfolio.UserId != currentUserService.UserId)
        {
            throw new AccessDeniedException("You do not have access to this portfolio");
        }

        var transactions = await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var stockSplits = await stockSplitRepository.GetAllAsync(cancellationToken);

        // 使用拆股調整後的持倉，確保與現價比較一致（FR-052）
        // 現在以 (Ticker, Market) 作為 composite key 分組，支援同一 ticker 在不同市場
        var positions = portfolioCalculator.RecalculateAllPositionsWithSplitAdjustments(
            transactions, stockSplits, splitAdjustmentService);

        // 轉為不分大小寫的 dictionary，避免 ticker 比對失敗
        // 注意：這裡只按 ticker 查詢，可能需要前端傳入 (ticker, market) composite key
        var currentPrices = performanceRequest?.CurrentPrices != null
            ? new Dictionary<string, CurrentPriceInfo>(
                performanceRequest.CurrentPrices, StringComparer.OrdinalIgnoreCase)
            : null;

        var positionDtos = new List<StockPositionDto>();
        // totalCostHome：僅包含有報價的持倉（避免損益百分比失真）
        var totalCostHome = 0m;
        decimal? totalValueHome = null;
        decimal? totalUnrealizedPnl = null;

        foreach (var position in positions.Where(p => p.TotalShares > 0))
        {
            // 僅在至少一筆交易具有 ExchangeRate 時，才視為可提供本位幣指標
            // 以 (Ticker, Market) composite key 篩選對應的交易
            var hasAnyExchangeRate = transactions.Any(t =>
                !t.IsDeleted &&
                t.Ticker.Equals(position.Ticker, StringComparison.OrdinalIgnoreCase) &&
                t.Market == position.Market &&
                t is { HasExchangeRate: true, TransactionType: TransactionType.Buy or TransactionType.Adjustment });

            // 直接使用 position.Market（現在 StockPosition 包含 Market 資訊）
            var market = position.Market;

            var dto = new StockPositionDto
            {
                Ticker = position.Ticker,
                TotalShares = position.TotalShares,
                TotalCostHome = hasAnyExchangeRate ? position.TotalCostHome : null,
                TotalCostSource = position.TotalCostSource,
                AverageCostPerShareHome = hasAnyExchangeRate ? position.AverageCostPerShareHome : null,
                AverageCostPerShareSource = position.AverageCostPerShareSource,
                Market = market,
                Currency = position.Currency
            };

            // 紀錄此持倉是否應納入總損益統計
            var contributesToPnl = false;

            // 若提供現價，則計算未實現損益
            if (currentPrices?.TryGetValue(position.Ticker, out var priceInfo) == true)
            {
                if (hasAnyExchangeRate)
                {
                    var pnl = portfolioCalculator.CalculateUnrealizedPnl(
                        position, priceInfo.Price, priceInfo.ExchangeRate);

                    dto = dto with
                    {
                        CurrentPrice = priceInfo.Price,
                        CurrentExchangeRate = priceInfo.ExchangeRate,
                        CurrentValueHome = pnl.CurrentValueHome,
                        UnrealizedPnlHome = pnl.UnrealizedPnlHome,
                        UnrealizedPnlPercentage = pnl.UnrealizedPnlPercentage,
                        CurrentValueSource = pnl.CurrentValueSource,
                        UnrealizedPnlSource = pnl.UnrealizedPnlSource,
                        UnrealizedPnlSourcePercentage = pnl.UnrealizedPnlSourcePercentage
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

            // 只有在此持倉納入損益計算時，才累加到 totalCostHome
            // 以確保 TotalCost 與 TotalValue 可比較，進而正確計算百分比
            if (contributesToPnl && hasAnyExchangeRate)
            {
                totalCostHome += position.TotalCostHome;
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
                ? totalUnrealizedPnl.Value / totalCostHome * 100
                : null
        };
    }
}
