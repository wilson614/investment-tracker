using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Services;

/// <summary>
/// Domain service for portfolio calculations including position tracking,
/// moving average cost, and PnL calculations.
/// </summary>
public class PortfolioCalculator
{
    /// <summary>
    /// Calculates the current position for a ticker based on transaction history.
    /// Uses moving average cost method for cost basis calculation.
    /// </summary>
    public StockPosition CalculatePosition(string ticker, IEnumerable<StockTransaction> transactions)
    {
        var tickerTransactions = transactions
            .Where(t => t.Ticker == ticker && !t.IsDeleted)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        if (!tickerTransactions.Any())
        {
            return new StockPosition(ticker, 0m, 0m, 0m);
        }

        decimal totalShares = 0m;
        decimal totalCostHome = 0m;

        foreach (var transaction in tickerTransactions)
        {
            switch (transaction.TransactionType)
            {
                case TransactionType.Buy:
                    totalShares += transaction.Shares;
                    totalCostHome += transaction.TotalCostHome;
                    break;

                case TransactionType.Sell:
                    if (totalShares > 0)
                    {
                        // Calculate average cost per share before sell
                        var avgCostPerShare = totalCostHome / totalShares;
                        // Remove cost basis for sold shares
                        totalCostHome -= transaction.Shares * avgCostPerShare;
                        totalShares -= transaction.Shares;
                    }
                    break;

                case TransactionType.Split:
                    // Adjust shares but keep total cost the same
                    // Split ratio is stored in Shares field (e.g., 2 for 2:1 split)
                    totalShares *= transaction.Shares;
                    break;

                case TransactionType.Adjustment:
                    // Direct adjustment to shares and/or cost
                    totalShares += transaction.Shares;
                    totalCostHome += transaction.TotalCostHome;
                    break;
            }
        }

        // Prevent negative values due to rounding
        totalShares = Math.Max(0, totalShares);
        totalCostHome = Math.Max(0, totalCostHome);

        var averageCost = totalShares > 0 ? totalCostHome / totalShares : 0m;

        return new StockPosition(ticker, totalShares, totalCostHome, averageCost);
    }

    /// <summary>
    /// Calculates unrealized PnL for a position based on current market price.
    /// </summary>
    public UnrealizedPnl CalculateUnrealizedPnl(
        StockPosition position,
        decimal currentPriceSource,
        decimal currentExchangeRate)
    {
        if (position.TotalShares == 0)
        {
            return new UnrealizedPnl(0m, 0m, 0m);
        }

        var currentValueHome = position.TotalShares * currentPriceSource * currentExchangeRate;
        var unrealizedPnlHome = currentValueHome - position.TotalCostHome;
        var percentage = position.TotalCostHome > 0
            ? (unrealizedPnlHome / position.TotalCostHome) * 100
            : 0m;

        return new UnrealizedPnl(currentValueHome, unrealizedPnlHome, percentage);
    }

    /// <summary>
    /// Calculates realized PnL for a sell transaction based on average cost method.
    /// </summary>
    public decimal CalculateRealizedPnl(StockPosition positionBeforeSell, StockTransaction sellTransaction)
    {
        if (sellTransaction.TransactionType != TransactionType.Sell)
        {
            throw new ArgumentException("Transaction must be a sell transaction", nameof(sellTransaction));
        }

        // Cost basis for sold shares (using average cost)
        var costBasis = sellTransaction.Shares * positionBeforeSell.AverageCostPerShare;

        // Sale proceeds in home currency
        var saleProceeds = sellTransaction.Shares * sellTransaction.PricePerShare * sellTransaction.ExchangeRate;

        // Subtract fees from proceeds (fees reduce realized gain)
        saleProceeds -= sellTransaction.Fees * sellTransaction.ExchangeRate;

        return saleProceeds - costBasis;
    }

    /// <summary>
    /// Recalculates all positions for a portfolio.
    /// </summary>
    public IEnumerable<StockPosition> RecalculateAllPositions(IEnumerable<StockTransaction> transactions)
    {
        var transactionList = transactions.Where(t => !t.IsDeleted).ToList();
        var tickers = transactionList.Select(t => t.Ticker).Distinct();

        return tickers.Select(ticker => CalculatePosition(ticker, transactionList));
    }
}

/// <summary>
/// Represents a stock position with calculated values.
/// </summary>
public record StockPosition(
    string Ticker,
    decimal TotalShares,
    decimal TotalCostHome,
    decimal AverageCostPerShare);

/// <summary>
/// Represents unrealized profit/loss calculation result.
/// </summary>
public record UnrealizedPnl(
    decimal CurrentValueHome,
    decimal UnrealizedPnlHome,
    decimal UnrealizedPnlPercentage);
