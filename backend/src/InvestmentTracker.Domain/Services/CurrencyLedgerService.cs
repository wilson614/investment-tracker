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
    /// Calculates the average exchange rate (only from ExchangeBuy and InitialBalance).
    /// This is the simple average rate at which foreign currency was purchased.
    /// </summary>
    public decimal CalculateAverageExchangeRate(IEnumerable<CurrencyTransaction> transactions)
    {
        decimal totalHomeCost = 0m;
        decimal totalForeignAmount = 0m;

        foreach (var tx in transactions.Where(t => !t.IsDeleted))
        {
            if (tx.TransactionType == CurrencyTransactionType.ExchangeBuy ||
                tx.TransactionType == CurrencyTransactionType.InitialBalance)
            {
                totalHomeCost += tx.HomeAmount ?? 0m;
                totalForeignAmount += tx.ForeignAmount;
            }
        }

        if (totalForeignAmount <= 0)
            return 0m;

        return Math.Round(totalHomeCost / totalForeignAmount, 4);
    }

    /// <summary>
    /// Calculates the total amount exchanged in home currency (TWD).
    /// Sum of all ExchangeBuy and InitialBalance HomeAmount.
    /// </summary>
    public decimal CalculateTotalExchanged(IEnumerable<CurrencyTransaction> transactions)
    {
        return transactions
            .Where(t => !t.IsDeleted &&
                       (t.TransactionType == CurrencyTransactionType.ExchangeBuy ||
                        t.TransactionType == CurrencyTransactionType.InitialBalance))
            .Sum(t => t.HomeAmount ?? 0m);
    }

    /// <summary>
    /// Calculates the total foreign currency spent on stocks (Spend type transactions).
    /// </summary>
    public decimal CalculateTotalSpentOnStocks(IEnumerable<CurrencyTransaction> transactions)
    {
        return transactions
            .Where(t => !t.IsDeleted && t.TransactionType == CurrencyTransactionType.Spend)
            .Sum(t => t.ForeignAmount);
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
                case CurrencyTransactionType.InitialBalance:
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
                case CurrencyTransactionType.OtherIncome:
                    // Interest/OtherIncome adds to balance but not to cost (reduces average cost)
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.Spend:
                case CurrencyTransactionType.OtherExpense:
                    // Spending/OtherExpense reduces balance and cost proportionally
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
    /// Uses the same moving average cost method as weighted average cost.
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
                case CurrencyTransactionType.InitialBalance:
                    totalCost += tx.HomeAmount ?? 0m;
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.ExchangeSell:
                    // Reduce cost basis proportionally (same as weighted average method)
                    if (balance > 0)
                    {
                        var avgCost = totalCost / balance;
                        totalCost -= avgCost * tx.ForeignAmount;
                        balance -= tx.ForeignAmount;
                    }
                    break;

                case CurrencyTransactionType.Interest:
                case CurrencyTransactionType.OtherIncome:
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.Spend:
                case CurrencyTransactionType.OtherExpense:
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
                case CurrencyTransactionType.InitialBalance:
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
                case CurrencyTransactionType.OtherIncome:
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.Spend:
                case CurrencyTransactionType.OtherExpense:
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

    /// <summary>
    /// Calculates the weighted average cost at a specific date.
    /// Only considers transactions on or before the specified date.
    /// </summary>
    public decimal CalculateWeightedAverageCostAtDate(IEnumerable<CurrencyTransaction> transactions, DateTime asOfDate)
    {
        decimal totalCost = 0m;
        decimal balance = 0m;

        // Only include transactions on or before the specified date
        var relevantTransactions = transactions
            .Where(t => !t.IsDeleted && t.TransactionDate.Date <= asOfDate.Date)
            .OrderBy(t => t.TransactionDate);

        foreach (var tx in relevantTransactions)
        {
            switch (tx.TransactionType)
            {
                case CurrencyTransactionType.ExchangeBuy:
                case CurrencyTransactionType.InitialBalance:
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
                case CurrencyTransactionType.OtherIncome:
                    // Interest/OtherIncome adds to balance but not to cost (reduces average cost)
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.Spend:
                case CurrencyTransactionType.OtherExpense:
                    // Spending/OtherExpense reduces balance and cost proportionally
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

        return Math.Round(totalCost / balance, 4);
    }

    /// <summary>
    /// Calculates the balance at a specific date.
    /// Only considers transactions on or before the specified date.
    /// </summary>
    public decimal CalculateBalanceAtDate(IEnumerable<CurrencyTransaction> transactions, DateTime asOfDate)
    {
        decimal balance = 0m;

        var relevantTransactions = transactions
            .Where(t => !t.IsDeleted && t.TransactionDate.Date <= asOfDate.Date)
            .OrderBy(t => t.TransactionDate);

        foreach (var tx in relevantTransactions)
        {
            balance += GetBalanceChange(tx);
        }

        return balance;
    }

    /// <summary>
    /// Calculates the exchange rate to use for a stock purchase.
    /// Uses LIFO-like logic: traces back income transactions (exchanges, interest, bonuses)
    /// but only counts actual exchanges (ExchangeBuy/InitialBalance) for rate calculation.
    /// This way, free income like bonuses reduces the amount needing exchange rate calculation.
    /// </summary>
    public decimal CalculateExchangeRateForPurchase(
        IEnumerable<CurrencyTransaction> transactions,
        DateTime purchaseDate,
        decimal purchaseAmount)
    {
        // Get all income transactions up to purchase date, ordered by most recent first
        // Include: ExchangeBuy, InitialBalance, Interest, OtherIncome
        var incomeTransactions = transactions
            .Where(t => !t.IsDeleted &&
                       t.TransactionDate.Date <= purchaseDate.Date &&
                       (t.TransactionType == CurrencyTransactionType.ExchangeBuy ||
                        t.TransactionType == CurrencyTransactionType.InitialBalance ||
                        t.TransactionType == CurrencyTransactionType.Interest ||
                        t.TransactionType == CurrencyTransactionType.OtherIncome))
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();

        // Get all expense transactions (to calculate remaining amounts via LIFO)
        var expenseTransactions = transactions
            .Where(t => !t.IsDeleted &&
                       t.TransactionDate.Date <= purchaseDate.Date &&
                       (t.TransactionType == CurrencyTransactionType.ExchangeSell ||
                        t.TransactionType == CurrencyTransactionType.Spend ||
                        t.TransactionType == CurrencyTransactionType.OtherExpense))
            // Exclude spend on purchase date (that's what we're calculating for)
            .Where(t => !(t.TransactionDate.Date == purchaseDate.Date &&
                         (t.TransactionType == CurrencyTransactionType.Spend ||
                          t.TransactionType == CurrencyTransactionType.OtherExpense)))
            .ToList();

        // Calculate total expenses to deduct from income (LIFO)
        decimal totalExpenses = expenseTransactions.Sum(t => t.ForeignAmount);

        // Build available amounts for each income transaction after LIFO deduction
        var availableAmounts = new List<(CurrencyTransaction tx, decimal available)>();
        decimal remainingExpenses = totalExpenses;

        // Process from oldest to newest to apply LIFO correctly
        foreach (var tx in incomeTransactions.AsEnumerable().Reverse())
        {
            decimal available = tx.ForeignAmount;

            // LIFO: expenses consume from oldest income first
            if (remainingExpenses > 0)
            {
                var consumed = Math.Min(available, remainingExpenses);
                available -= consumed;
                remainingExpenses -= consumed;
            }

            if (available > 0)
            {
                availableAmounts.Add((tx, available));
            }
        }

        // Reverse to get most recent first for purchase allocation
        availableAmounts.Reverse();

        // Allocate purchase amount using LIFO (most recent first)
        decimal remaining = purchaseAmount;
        decimal totalExchangeCost = 0m;
        decimal totalExchangeAmount = 0m;

        foreach (var (tx, available) in availableAmounts)
        {
            if (remaining <= 0)
                break;

            var take = Math.Min(available, remaining);
            remaining -= take;

            // Only ExchangeBuy/InitialBalance count toward exchange rate
            if (tx.TransactionType == CurrencyTransactionType.ExchangeBuy ||
                tx.TransactionType == CurrencyTransactionType.InitialBalance)
            {
                var ratio = take / tx.ForeignAmount;
                totalExchangeCost += (tx.HomeAmount ?? 0m) * ratio;
                totalExchangeAmount += take;
            }
            // Interest/OtherIncome: reduces 'remaining' but doesn't add to exchange cost
        }

        if (totalExchangeAmount <= 0)
            return 0m;

        return Math.Round(totalExchangeCost / totalExchangeAmount, 4);
    }

    /// <summary>
    /// Calculates the balance before any spend transactions on a specific date.
    /// Includes all transactions before the date, plus buy/interest on the date,
    /// but excludes spend transactions on the date.
    /// </summary>
    private decimal CalculateBalanceBeforeSpend(IEnumerable<CurrencyTransaction> transactions, DateTime asOfDate)
    {
        decimal balance = 0m;

        var relevantTransactions = transactions
            .Where(t => !t.IsDeleted && t.TransactionDate.Date <= asOfDate.Date)
            .OrderBy(t => t.TransactionDate);

        foreach (var tx in relevantTransactions)
        {
            // Skip spend/expense transactions on the target date (they happen after the balance calculation)
            if (tx.TransactionDate.Date == asOfDate.Date &&
                (tx.TransactionType == CurrencyTransactionType.Spend || tx.TransactionType == CurrencyTransactionType.OtherExpense))
                continue;

            balance += GetBalanceChange(tx);
        }

        return balance;
    }

    private static decimal GetBalanceChange(CurrencyTransaction tx)
    {
        return tx.TransactionType switch
        {
            CurrencyTransactionType.ExchangeBuy => tx.ForeignAmount,
            CurrencyTransactionType.InitialBalance => tx.ForeignAmount,
            CurrencyTransactionType.Interest => tx.ForeignAmount,
            CurrencyTransactionType.OtherIncome => tx.ForeignAmount,
            CurrencyTransactionType.ExchangeSell => -tx.ForeignAmount,
            CurrencyTransactionType.Spend => -tx.ForeignAmount,
            CurrencyTransactionType.OtherExpense => -tx.ForeignAmount,
            _ => 0m
        };
    }
}
