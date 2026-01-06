using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Services;

/// <summary>
/// Domain service for currency ledger calculations.
/// </summary>
public class CurrencyLedgerService
{
    /// <summary>
    /// Calculates the current balance of foreign currency.
    /// </summary>
    public decimal CalculateBalance(IEnumerable<CurrencyTransaction> transactions)
    {
        decimal balance = 0m;

        foreach (var tx in transactions.Where(t => !t.IsDeleted).OrderBy(t => t.TransactionDate))
        {
            balance += GetBalanceChange(tx);
        }

        return balance;
    }

    /// <summary>
    /// Calculates the weighted average cost per unit of foreign currency.
    /// Uses the moving average cost method.
    /// </summary>
    public decimal CalculateWeightedAverageCost(IEnumerable<CurrencyTransaction> transactions)
    {
        decimal totalCost = 0m;
        decimal balance = 0m;

        foreach (var tx in transactions.Where(t => !t.IsDeleted).OrderBy(t => t.TransactionDate))
        {
            switch (tx.TransactionType)
            {
                case CurrencyTransactionType.ExchangeBuy:
                    // Add to cost basis
                    totalCost += tx.HomeAmount ?? 0m;
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.ExchangeSell:
                    // Reduce cost basis proportionally
                    if (balance > 0)
                    {
                        var avgCost = totalCost / balance;
                        var costReduction = avgCost * tx.ForeignAmount;
                        totalCost -= costReduction;
                        balance -= tx.ForeignAmount;
                    }
                    break;

                case CurrencyTransactionType.Interest:
                    // Interest adds to balance but not to cost (reduces average cost)
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.Spend:
                    // Spending reduces balance and cost proportionally
                    if (balance > 0)
                    {
                        var avgCost = totalCost / balance;
                        var costReduction = avgCost * tx.ForeignAmount;
                        totalCost -= costReduction;
                        balance -= tx.ForeignAmount;
                    }
                    break;
            }
        }

        if (balance <= 0)
            return 0m;

        return Math.Round(totalCost / balance, 6);
    }

    /// <summary>
    /// Calculates the total cost basis in home currency.
    /// </summary>
    public decimal CalculateTotalCost(IEnumerable<CurrencyTransaction> transactions)
    {
        decimal totalCost = 0m;
        decimal balance = 0m;

        foreach (var tx in transactions.Where(t => !t.IsDeleted).OrderBy(t => t.TransactionDate))
        {
            switch (tx.TransactionType)
            {
                case CurrencyTransactionType.ExchangeBuy:
                    totalCost += tx.HomeAmount ?? 0m;
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.ExchangeSell:
                    // Subtract proceeds from total cost
                    totalCost -= tx.HomeAmount ?? 0m;
                    balance -= tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.Interest:
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.Spend:
                    if (balance > 0)
                    {
                        var avgCost = totalCost / balance;
                        totalCost -= avgCost * tx.ForeignAmount;
                        balance -= tx.ForeignAmount;
                    }
                    break;
            }
        }

        return Math.Round(totalCost, 2);
    }

    /// <summary>
    /// Calculates the realized profit/loss from currency exchanges.
    /// </summary>
    public decimal CalculateRealizedPnl(IEnumerable<CurrencyTransaction> transactions)
    {
        decimal realizedPnl = 0m;
        decimal totalCost = 0m;
        decimal balance = 0m;

        foreach (var tx in transactions.Where(t => !t.IsDeleted).OrderBy(t => t.TransactionDate))
        {
            switch (tx.TransactionType)
            {
                case CurrencyTransactionType.ExchangeBuy:
                    totalCost += tx.HomeAmount ?? 0m;
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.ExchangeSell:
                    if (balance > 0)
                    {
                        var avgCost = totalCost / balance;
                        var costBasis = avgCost * tx.ForeignAmount;
                        var proceeds = tx.HomeAmount ?? 0m;
                        realizedPnl += proceeds - costBasis;

                        totalCost -= costBasis;
                        balance -= tx.ForeignAmount;
                    }
                    break;

                case CurrencyTransactionType.Interest:
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.Spend:
                    if (balance > 0)
                    {
                        var avgCost = totalCost / balance;
                        totalCost -= avgCost * tx.ForeignAmount;
                        balance -= tx.ForeignAmount;
                    }
                    break;
            }
        }

        return Math.Round(realizedPnl, 2);
    }

    /// <summary>
    /// Validates if a spend amount is possible given the current balance.
    /// </summary>
    public bool ValidateSpend(IEnumerable<CurrencyTransaction> transactions, decimal spendAmount)
    {
        var balance = CalculateBalance(transactions);
        return balance >= spendAmount;
    }

    private static decimal GetBalanceChange(CurrencyTransaction tx)
    {
        return tx.TransactionType switch
        {
            CurrencyTransactionType.ExchangeBuy => tx.ForeignAmount,
            CurrencyTransactionType.Interest => tx.ForeignAmount,
            CurrencyTransactionType.ExchangeSell => -tx.ForeignAmount,
            CurrencyTransactionType.Spend => -tx.ForeignAmount,
            _ => 0m
        };
    }
}
