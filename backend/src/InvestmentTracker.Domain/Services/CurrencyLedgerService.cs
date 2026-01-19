using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Services;

/// <summary>
/// 外幣台帳的領域服務（Domain Service），負責餘額、成本與損益等計算。
/// </summary>
public class CurrencyLedgerService
{
    /// <summary>
    /// 計算外幣目前餘額。
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
    /// 計算平均買入匯率（僅納入 ExchangeBuy 與 InitialBalance）。
    /// 這是外幣買入匯率的簡單平均值。
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
    /// 計算本位幣（TWD）淨投入金額。
    /// 淨投入 =（ExchangeBuy + InitialBalance）- ExchangeSell 的 HomeAmount。
    /// 用於表示實際投入的 TWD（包含賣回的回收）。
    /// </summary>
    public decimal CalculateTotalExchanged(IEnumerable<CurrencyTransaction> transactions)
    {
        decimal buyTotal = 0m;
        decimal sellTotal = 0m;

        foreach (var tx in transactions.Where(t => !t.IsDeleted))
        {
            if (tx.TransactionType == CurrencyTransactionType.ExchangeBuy ||
                tx.TransactionType == CurrencyTransactionType.InitialBalance)
            {
                buyTotal += tx.HomeAmount ?? 0m;
            }
            else if (tx.TransactionType == CurrencyTransactionType.ExchangeSell)
            {
                sellTotal += tx.HomeAmount ?? 0m;
            }
        }

        return buyTotal - sellTotal;
    }

    /// <summary>
    /// 計算用於買股的外幣支出總額（Spend 類型）。
    /// </summary>
    public decimal CalculateTotalSpentOnStocks(IEnumerable<CurrencyTransaction> transactions)
    {
        return transactions
            .Where(t => !t.IsDeleted && t.TransactionType == CurrencyTransactionType.Spend)
            .Sum(t => t.ForeignAmount);
    }

    /// <summary>
    /// 計算利息收入總額（Interest 類型）。
    /// </summary>
    public decimal CalculateTotalInterest(IEnumerable<CurrencyTransaction> transactions)
    {
        return transactions
            .Where(t => !t.IsDeleted && t.TransactionType == CurrencyTransactionType.Interest)
            .Sum(t => t.ForeignAmount);
    }

    /// <summary>
    /// 計算外幣單位的加權平均成本。
    /// 使用移動平均成本法（moving average cost）。
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
                    // 納入成本基礎（cost basis）
                    totalCost += tx.HomeAmount ?? 0m;
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.ExchangeSell:
                    // 依比例扣除成本基礎（cost basis）
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
                    // Interest/OtherIncome 會增加餘額但不增加成本（因此平均成本會下降）
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.Spend:
                case CurrencyTransactionType.OtherExpense:
                    // Spend/OtherExpense 會扣除餘額並依比例扣除成本
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
    /// 計算本位幣的總成本（cost basis）。
    /// 使用與加權平均成本相同的移動平均成本法。
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
                    // 依比例扣除成本基礎（與加權平均成本計算相同）
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
    /// 計算外幣買賣（Exchange）所產生的已實現損益。
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
    /// 依據目前餘額，驗證指定支出金額是否可行。
    /// </summary>
    public bool ValidateSpend(IEnumerable<CurrencyTransaction> transactions, decimal spendAmount)
    {
        var balance = CalculateBalance(transactions);
        return balance >= spendAmount;
    }

    /// <summary>
    /// 計算指定日期的加權平均成本。
    /// 僅納入該日期（含）之前的交易。
    /// </summary>
    public decimal CalculateWeightedAverageCostAtDate(IEnumerable<CurrencyTransaction> transactions, DateTime asOfDate)
    {
        decimal totalCost = 0m;
        decimal balance = 0m;

        // 僅納入指定日期（含）之前的交易
        var relevantTransactions = transactions
            .Where(t => !t.IsDeleted && t.TransactionDate.Date <= asOfDate.Date)
            .OrderBy(t => t.TransactionDate);

        foreach (var tx in relevantTransactions)
        {
            switch (tx.TransactionType)
            {
                case CurrencyTransactionType.ExchangeBuy:
                case CurrencyTransactionType.InitialBalance:
                    // 納入成本基礎（cost basis）
                    totalCost += tx.HomeAmount ?? 0m;
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.ExchangeSell:
                    // 依比例扣除成本基礎（cost basis）
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
                    // Interest/OtherIncome 會增加餘額但不增加成本（因此平均成本會下降）
                    balance += tx.ForeignAmount;
                    break;

                case CurrencyTransactionType.Spend:
                case CurrencyTransactionType.OtherExpense:
                    // Spend/OtherExpense 會扣除餘額並依比例扣除成本
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
    /// 計算指定日期的餘額。
    /// 僅納入該日期（含）之前的交易。
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
    /// 計算買股時應採用的匯率。
    /// 使用類 LIFO 邏輯：回溯收入類交易（換匯、利息、回饋），
    /// 但匯率計算僅納入實際換匯（ExchangeBuy／InitialBalance）。
    /// 因此，像回饋這類「免費收入」會先抵減需計算匯率的金額。
    /// </summary>
    public decimal CalculateExchangeRateForPurchase(
        IEnumerable<CurrencyTransaction> transactions,
        DateTime purchaseDate,
        decimal purchaseAmount)
    {
        // 取得買入日（含）之前的所有收入類交易，依時間由新到舊排序
        // 包含：ExchangeBuy、InitialBalance、Interest、OtherIncome
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

        // 取得所有支出類交易（用於透過 LIFO 計算剩餘可用金額）
        var expenseTransactions = transactions
            .Where(t => !t.IsDeleted &&
                       t.TransactionDate.Date <= purchaseDate.Date &&
                       (t.TransactionType == CurrencyTransactionType.ExchangeSell ||
                        t.TransactionType == CurrencyTransactionType.Spend ||
                        t.TransactionType == CurrencyTransactionType.OtherExpense))
            // 排除買入日當天的 Spend（因為這筆就是正在計算的買股）
            .Where(t => !(t.TransactionDate.Date == purchaseDate.Date &&
                         (t.TransactionType == CurrencyTransactionType.Spend ||
                          t.TransactionType == CurrencyTransactionType.OtherExpense)))
            .ToList();

        // 計算需從收入中扣除的支出總額（LIFO）
        decimal totalExpenses = expenseTransactions.Sum(t => t.ForeignAmount);

        // 建立每筆收入交易在 LIFO 扣除後的可用金額
        var availableAmounts = new List<(CurrencyTransaction tx, decimal available)>();
        decimal remainingExpenses = totalExpenses;

        // 由舊到新處理，才能正確套用 LIFO 扣抵
        foreach (var tx in incomeTransactions.AsEnumerable().Reverse())
        {
            decimal available = tx.ForeignAmount;

            // LIFO：支出會先消耗最早的收入
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

        // 反轉後改為由新到舊，供買入分配使用
        availableAmounts.Reverse();

        // 使用 LIFO（由新到舊）分配買入金額
        decimal remaining = purchaseAmount;
        decimal totalExchangeCost = 0m;
        decimal totalExchangeAmount = 0m;

        foreach (var (tx, available) in availableAmounts)
        {
            if (remaining <= 0)
                break;

            var take = Math.Min(available, remaining);
            remaining -= take;

            // 只有 ExchangeBuy/InitialBalance 會納入匯率計算
            if (tx.TransactionType == CurrencyTransactionType.ExchangeBuy ||
                tx.TransactionType == CurrencyTransactionType.InitialBalance)
            {
                var ratio = take / tx.ForeignAmount;
                totalExchangeCost += (tx.HomeAmount ?? 0m) * ratio;
                totalExchangeAmount += take;
            }
            // Interest/OtherIncome：會扣掉 remaining，但不會增加換匯成本
        }

        if (totalExchangeAmount <= 0)
            return 0m;

        return Math.Round(totalExchangeCost / totalExchangeAmount, 4);
    }

    /// <summary>
    /// 計算指定日期「支出交易之前」的餘額。
    /// 包含該日期之前的所有交易，以及該日期當天的買入／利息，
    /// 但排除該日期當天的支出交易。
    /// </summary>
    private decimal CalculateBalanceBeforeSpend(IEnumerable<CurrencyTransaction> transactions, DateTime asOfDate)
    {
        decimal balance = 0m;

        var relevantTransactions = transactions
            .Where(t => !t.IsDeleted && t.TransactionDate.Date <= asOfDate.Date)
            .OrderBy(t => t.TransactionDate);

        foreach (var tx in relevantTransactions)
        {
            // 跳過目標日期當天的 Spend/OtherExpense（它們應視為在餘額計算之後發生）
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
