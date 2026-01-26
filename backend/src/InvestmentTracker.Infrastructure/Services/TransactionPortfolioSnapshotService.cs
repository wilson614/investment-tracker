using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// TransactionPortfolioSnapshot 的寫入/回填服務。
///
/// 注意：目前 CurrencyLedger 與 Portfolio 尚無直接關聯，因此此服務先以 Portfolio 的股票持倉估值為主。
/// 後續若加入更完整的 ledger 資產範圍策略，會在 CF Strategy 層處理。
/// </summary>
public class TransactionPortfolioSnapshotService(
    AppDbContext dbContext,
    IPortfolioRepository portfolioRepository,
    IStockTransactionRepository transactionRepository,
    ICurrencyLedgerRepository currencyLedgerRepository,
    CurrencyLedgerService currencyLedgerService,
    PortfolioCalculator portfolioCalculator,
    ICurrentUserService currentUserService,
    IYahooHistoricalPriceService yahooService,
    IStooqHistoricalPriceService stooqService,
    ITwseStockHistoricalPriceService twseStockService)
    : ITransactionPortfolioSnapshotService
{
    public async Task<IReadOnlyList<TransactionPortfolioSnapshot>> GetSnapshotsAsync(
        Guid portfolioId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", portfolioId);

        if (portfolio.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        // Npgsql 對 timestamptz 參數要求 Kind=Utc，避免 Unspecified 造成例外
        var from = DateTime.SpecifyKind(fromDate.Date, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toDate.Date, DateTimeKind.Utc);

        return await dbContext.TransactionPortfolioSnapshots
            .AsNoTracking()
            .Where(s => s.PortfolioId == portfolioId && s.SnapshotDate >= from && s.SnapshotDate <= to)
            .OrderBy(s => s.SnapshotDate)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertSnapshotAsync(
        Guid portfolioId,
        Guid transactionId,
        DateTime transactionDate,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", portfolioId);

        if (portfolio.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        // StockTransaction：同日多筆 Buy/Sell 需要「串接」before/after，避免 TWR 產生錯誤的重複子期間。
        var stockTx = await transactionRepository.GetByIdAsync(transactionId, cancellationToken);
        if (stockTx != null)
        {
            await UpsertStockTransactionSnapshotsForDateAsync(
                portfolio,
                DateOnly.FromDateTime(stockTx.TransactionDate),
                cancellationToken);
            return;
        }

        // 非 StockTransaction（例如 CurrencyTransaction）暫時沿用既有日切估值方式。
        await UpsertLegacySnapshotAsync(
            portfolio,
            portfolioId,
            transactionId,
            transactionDate,
            cancellationToken);
    }

    private async Task UpsertStockTransactionSnapshotsForDateAsync(
        Portfolio portfolio,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var transactions = await transactionRepository.GetByPortfolioIdAsync(portfolio.Id, cancellationToken);

        var dayTransactions = transactions
            .Where(t => t is { IsDeleted: false }
                        && DateOnly.FromDateTime(t.TransactionDate) == date
                        && t.TransactionType is TransactionType.Buy or TransactionType.Sell)
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToList();

        if (dayTransactions.Count == 0)
            return;

        var snapshotDate = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var dayIds = dayTransactions.Select(t => t.Id).ToList();

        // 若快照都已存在且已經符合「同日多筆交易串接」規則，則不重寫（避免重複對外取價）。
        var existingSnapshots = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolio.Id && dayIds.Contains(s.TransactionId))
            .ToListAsync(cancellationToken);

        var existingById = existingSnapshots
            .GroupBy(s => s.TransactionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First());

        var allSnapshotsExist = dayIds.All(id => existingById.ContainsKey(id));
        var firstTxId = dayTransactions[0].Id;

        if (allSnapshotsExist)
        {
            var firstSnapshot = existingById[firstTxId];
            var existingDayEndHome = firstSnapshot.PortfolioValueAfterHome;
            var existingDayEndSource = firstSnapshot.PortfolioValueAfterSource;

            var alreadyChained = dayTransactions
                .Skip(1)
                .Select(tx => existingById[tx.Id])
                .All(s => s.PortfolioValueBeforeHome == existingDayEndHome
                          && s.PortfolioValueAfterHome == existingDayEndHome
                          && s.PortfolioValueBeforeSource == existingDayEndSource
                          && s.PortfolioValueAfterSource == existingDayEndSource);

            if (alreadyChained)
                return;

            await ReplaceStockSnapshotsForDateAsync(
                portfolioId: portfolio.Id,
                snapshotDate: snapshotDate,
                dayTransactions: dayTransactions,
                dayStartHome: firstSnapshot.PortfolioValueBeforeHome,
                dayEndHome: existingDayEndHome,
                dayStartSource: firstSnapshot.PortfolioValueBeforeSource,
                dayEndSource: existingDayEndSource,
                cancellationToken: cancellationToken);

            return;
        }

        var beforeDate = date.AddDays(-1);

        var dayStartHome = await CalculatePortfolioValueHomeAsync(
            portfolio.Id,
            beforeDate,
            portfolio.HomeCurrency,
            cancellationToken);

        var dayEndHome = await CalculatePortfolioValueHomeAsync(
            portfolio.Id,
            date,
            portfolio.HomeCurrency,
            cancellationToken);

        var dayStartSource = await CalculatePortfolioValueSourceAsync(
            portfolio.Id,
            beforeDate,
            portfolio.BaseCurrency,
            portfolio.HomeCurrency,
            cancellationToken);

        var dayEndSource = await CalculatePortfolioValueSourceAsync(
            portfolio.Id,
            date,
            portfolio.BaseCurrency,
            portfolio.HomeCurrency,
            cancellationToken);

        if (portfolio.BoundCurrencyLedgerId.HasValue)
        {
            var ledger = await currencyLedgerRepository.GetByIdWithTransactionsAsync(
                portfolio.BoundCurrencyLedgerId.Value,
                cancellationToken);

            if (ledger is { IsActive: true } && ledger.UserId == portfolio.UserId)
            {
                dayStartHome += await CalculateLedgerValueHomeAsync(ledger, portfolio.HomeCurrency, beforeDate, cancellationToken);
                dayEndHome += await CalculateLedgerValueHomeAsync(ledger, portfolio.HomeCurrency, date, cancellationToken);

                dayStartSource += await CalculateLedgerValueSourceAsync(ledger, portfolio.BaseCurrency, portfolio.HomeCurrency, beforeDate, cancellationToken);
                dayEndSource += await CalculateLedgerValueSourceAsync(ledger, portfolio.BaseCurrency, portfolio.HomeCurrency, date, cancellationToken);
            }
        }

        await ReplaceStockSnapshotsForDateAsync(
            portfolioId: portfolio.Id,
            snapshotDate: snapshotDate,
            dayTransactions: dayTransactions,
            dayStartHome: dayStartHome,
            dayEndHome: dayEndHome,
            dayStartSource: dayStartSource,
            dayEndSource: dayEndSource,
            cancellationToken: cancellationToken);
    }

    private async Task ReplaceStockSnapshotsForDateAsync(
        Guid portfolioId,
        DateTime snapshotDate,
        IReadOnlyList<StockTransaction> dayTransactions,
        decimal dayStartHome,
        decimal dayEndHome,
        decimal dayStartSource,
        decimal dayEndSource,
        CancellationToken cancellationToken)
    {
        var ordered = dayTransactions
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToList();

        var firstId = ordered[0].Id;

        var newSnapshots = ordered
            .Select(tx => new TransactionPortfolioSnapshot(
                portfolioId: portfolioId,
                transactionId: tx.Id,
                snapshotDate: snapshotDate,
                portfolioValueBeforeHome: tx.Id == firstId ? dayStartHome : dayEndHome,
                portfolioValueAfterHome: dayEndHome,
                portfolioValueBeforeSource: tx.Id == firstId ? dayStartSource : dayEndSource,
                portfolioValueAfterSource: dayEndSource))
            .ToList();

        var dayIds = ordered.Select(t => t.Id).ToList();

        var existing = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolioId && dayIds.Contains(s.TransactionId))
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            dbContext.TransactionPortfolioSnapshots.RemoveRange(existing);
        }

        dbContext.TransactionPortfolioSnapshots.AddRange(newSnapshots);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertLegacySnapshotAsync(
        Portfolio portfolio,
        Guid portfolioId,
        Guid transactionId,
        DateTime transactionDate,
        CancellationToken cancellationToken)
    {
        var snapshotDate = DateTime.SpecifyKind(transactionDate.Date, DateTimeKind.Utc);

        var beforeDate = DateOnly.FromDateTime(snapshotDate.AddDays(-1));
        var afterDate = DateOnly.FromDateTime(snapshotDate);

        var beforeValueHome = await CalculatePortfolioValueHomeAsync(
            portfolioId,
            beforeDate,
            portfolio.HomeCurrency,
            cancellationToken);

        var afterValueHome = await CalculatePortfolioValueHomeAsync(
            portfolioId,
            afterDate,
            portfolio.HomeCurrency,
            cancellationToken);

        var beforeValueSource = await CalculatePortfolioValueSourceAsync(
            portfolioId,
            beforeDate,
            portfolio.BaseCurrency,
            portfolio.HomeCurrency,
            cancellationToken);

        var afterValueSource = await CalculatePortfolioValueSourceAsync(
            portfolioId,
            afterDate,
            portfolio.BaseCurrency,
            portfolio.HomeCurrency,
            cancellationToken);

        if (portfolio.BoundCurrencyLedgerId.HasValue)
        {
            var ledger = await currencyLedgerRepository.GetByIdWithTransactionsAsync(
                portfolio.BoundCurrencyLedgerId.Value,
                cancellationToken);

            if (ledger is { IsActive: true } && ledger.UserId == portfolio.UserId)
            {
                beforeValueHome += await CalculateLedgerValueHomeAsync(ledger, portfolio.HomeCurrency, beforeDate, cancellationToken);
                afterValueHome += await CalculateLedgerValueHomeAsync(ledger, portfolio.HomeCurrency, afterDate, cancellationToken);

                beforeValueSource += await CalculateLedgerValueSourceAsync(ledger, portfolio.BaseCurrency, portfolio.HomeCurrency, beforeDate, cancellationToken);
                afterValueSource += await CalculateLedgerValueSourceAsync(ledger, portfolio.BaseCurrency, portfolio.HomeCurrency, afterDate, cancellationToken);
            }
        }

        var entity = new TransactionPortfolioSnapshot(
            portfolioId,
            transactionId,
            snapshotDate,
            beforeValueHome,
            afterValueHome,
            beforeValueSource,
            afterValueSource);

        var existing = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.PortfolioId == portfolioId && s.TransactionId == transactionId, cancellationToken);

        if (existing != null)
        {
            dbContext.TransactionPortfolioSnapshots.Remove(existing);
        }

        dbContext.TransactionPortfolioSnapshots.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteSnapshotAsync(
        Guid portfolioId,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.PortfolioId == portfolioId && s.TransactionId == transactionId, cancellationToken);

        if (existing == null)
            return;

        dbContext.TransactionPortfolioSnapshots.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task BackfillSnapshotsAsync(
        Guid portfolioId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", portfolioId);

        if (portfolio.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        var from = DateTime.SpecifyKind(fromDate.Date, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toDate.Date, DateTimeKind.Utc);

        var transactions = await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var validTx = transactions
            .Where(t => t is { IsDeleted: false }
                        && t.TransactionDate.Date >= from
                        && t.TransactionDate.Date <= to
                        && t.TransactionType is TransactionType.Buy or TransactionType.Sell)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        foreach (var tx in validTx)
        {
            var exists = await dbContext.TransactionPortfolioSnapshots
                .AsNoTracking()
                .AnyAsync(s => s.PortfolioId == portfolioId && s.TransactionId == tx.Id, cancellationToken);

            if (exists)
                continue;

            await UpsertSnapshotAsync(portfolioId, tx.Id, tx.TransactionDate, cancellationToken);
        }
    }

    private async Task<decimal> CalculatePortfolioValueHomeAsync(
        Guid portfolioId,
        DateOnly valuationDate,
        string homeCurrency,
        CancellationToken cancellationToken)
    {
        var allTransactions = await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var transactionsUpToDate = allTransactions
            .Where(t => t is { IsDeleted: false, HasExchangeRate: true }
                        && DateOnly.FromDateTime(t.TransactionDate) <= valuationDate)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        var positions = portfolioCalculator.RecalculateAllPositionsWithSplitAdjustments(
                transactionsUpToDate,
                splits: [],
                splitService: new StockSplitAdjustmentService())
            .Where(p => p.TotalShares > 0)
            .ToList();

        if (positions.Count == 0)
            return 0m;

        var total = 0m;

        foreach (var position in positions)
        {
            var priceInfo = await GetHistoricalPriceAsync(position.Ticker, position.Market, valuationDate, cancellationToken);
            if (priceInfo == null)
                continue;

            var exchangeRate = await GetExchangeRateAsync(priceInfo.Currency, homeCurrency, priceInfo.ActualDate, cancellationToken);
            if (exchangeRate == null)
                continue;

            total += position.TotalShares * priceInfo.Price * exchangeRate.Value;
        }

        return Math.Round(total, 4);
    }

    private async Task<decimal> CalculatePortfolioValueSourceAsync(
        Guid portfolioId,
        DateOnly valuationDate,
        string sourceCurrency,
        string homeCurrency,
        CancellationToken cancellationToken)
    {
        if (string.Equals(sourceCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return await CalculatePortfolioValueHomeAsync(portfolioId, valuationDate, homeCurrency, cancellationToken);
        }

        var allTransactions = await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var transactionsUpToDate = allTransactions
            .Where(t => t is { IsDeleted: false, HasExchangeRate: true }
                        && DateOnly.FromDateTime(t.TransactionDate) <= valuationDate)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        // 這裡必須保留 Market 資訊（UK/EU 需要 suffix），避免 Yahoo 查價用到未加尾碼的 symbol（例如 VWRA → VWRA.L）
        var positions = portfolioCalculator.RecalculateAllPositionsWithSplitAdjustments(
                transactionsUpToDate,
                splits: [],
                splitService: new StockSplitAdjustmentService())
            .Where(p => p.TotalShares > 0)
            .ToList();

        if (positions.Count == 0)
            return 0m;

        var total = 0m;

        foreach (var position in positions)
        {
            var priceInfo = await GetHistoricalPriceAsync(position.Ticker, position.Market, valuationDate, cancellationToken);
            if (priceInfo == null)
                continue;

            // 先取得該價格幣別到 sourceCurrency 的匯率
            var exchangeRate = await GetExchangeRateAsync(priceInfo.Currency, sourceCurrency, priceInfo.ActualDate, cancellationToken);
            if (exchangeRate == null)
                continue;

            total += position.TotalShares * priceInfo.Price * exchangeRate.Value;
        }

        return Math.Round(total, 4);
    }

    private async Task<decimal?> GetExchangeRateAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        var result = await yahooService.GetExchangeRateAsync(fromCurrency, toCurrency, date, cancellationToken);
        if (result != null)
            return result.Rate;

        var stooq = await stooqService.GetExchangeRateAsync(fromCurrency, toCurrency, date, cancellationToken);
        return stooq?.Rate;
    }

    private async Task<HistoricalPriceInfo?> GetHistoricalPriceAsync(
        string ticker,
        StockMarket? market,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (market == StockMarket.TW || IsTaiwanTicker(ticker))
        {
            var stockNo = ticker.Split('.')[0];
            var twse = await twseStockService.GetStockPriceAsync(stockNo, date, cancellationToken);
            if (twse == null)
                return null;

            return new HistoricalPriceInfo(
                Price: twse.Price,
                Currency: "TWD",
                ActualDate: twse.ActualDate,
                Market: StockMarket.TW,
                Source: "TWSE");
        }

        var yahooSymbol = YahooSymbolHelper.ConvertToYahooSymbol(ticker, market);
        var yahoo = await yahooService.GetHistoricalPriceAsync(yahooSymbol, date, cancellationToken);
        if (yahoo != null)
        {
            return new HistoricalPriceInfo(
                Price: yahoo.Price,
                Currency: yahoo.Currency,
                ActualDate: yahoo.ActualDate,
                Market: market,
                Source: "Yahoo");
        }

        if (market != StockMarket.EU)
        {
            var stooq = await stooqService.GetStockPriceAsync(ticker, date, cancellationToken);
            if (stooq != null)
            {
                return new HistoricalPriceInfo(
                    Price: stooq.Price,
                    Currency: stooq.Currency,
                    ActualDate: stooq.ActualDate,
                    Market: market,
                    Source: "Stooq");
            }
        }

        return null;
    }

    private async Task<decimal> CalculateLedgerValueHomeAsync(
        CurrencyLedger ledger,
        string homeCurrency,
        DateOnly valuationDate,
        CancellationToken cancellationToken)
    {
        var balance = currencyLedgerService.CalculateBalance(
            ledger.Transactions.Where(t => DateOnly.FromDateTime(t.TransactionDate) <= valuationDate));

        if (balance <= 0)
            return 0m;

        if (string.Equals(ledger.CurrencyCode, homeCurrency, StringComparison.OrdinalIgnoreCase))
            return Math.Round(balance, 4);

        var exchangeRate = await GetExchangeRateAsync(ledger.CurrencyCode, homeCurrency, valuationDate, cancellationToken);
        if (exchangeRate == null)
            return 0m;

        return Math.Round(balance * exchangeRate.Value, 4);
    }

    private async Task<decimal> CalculateLedgerValueSourceAsync(
        CurrencyLedger ledger,
        string sourceCurrency,
        string homeCurrency,
        DateOnly valuationDate,
        CancellationToken cancellationToken)
    {
        var balance = currencyLedgerService.CalculateBalance(
            ledger.Transactions.Where(t => DateOnly.FromDateTime(t.TransactionDate) <= valuationDate));

        if (balance <= 0)
            return 0m;

        if (string.Equals(ledger.CurrencyCode, sourceCurrency, StringComparison.OrdinalIgnoreCase))
            return Math.Round(balance, 4);

        // 以 Home 作為中介：ledgerCurrency -> home -> source
        var toHomeRate = await GetExchangeRateAsync(ledger.CurrencyCode, homeCurrency, valuationDate, cancellationToken);
        if (toHomeRate == null)
            return 0m;

        if (string.Equals(homeCurrency, sourceCurrency, StringComparison.OrdinalIgnoreCase))
            return Math.Round(balance * toHomeRate.Value, 4);

        var homeToSourceRate = await GetExchangeRateAsync(homeCurrency, sourceCurrency, valuationDate, cancellationToken);
        if (homeToSourceRate == null)
            return 0m;

        return Math.Round(balance * toHomeRate.Value * homeToSourceRate.Value, 4);
    }

    private static bool IsTaiwanTicker(string ticker) =>
        !string.IsNullOrEmpty(ticker) && char.IsDigit(ticker[0]);

    private sealed record HistoricalPriceInfo(
        decimal Price,
        string Currency,
        DateOnly ActualDate,
        StockMarket? Market,
        string Source);
}
