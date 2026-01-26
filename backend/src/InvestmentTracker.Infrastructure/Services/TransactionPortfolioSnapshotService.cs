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

        var snapshotDate = DateTime.SpecifyKind(transactionDate.Date, DateTimeKind.Utc);

        // 估值：事件前/事件後都以「截至該日期」的持倉估值計算。
        // 目前先做最小可用版本：before = 使用交易前一日持倉；after = 使用交易日後持倉。
        // 詳細的 before/after 精準定義會在後續串接 use case 時再依交易內容調整。
        var beforeDate = DateOnly.FromDateTime(snapshotDate.AddDays(-1));
        var afterDate = DateOnly.FromDateTime(snapshotDate);

        var beforeValueHome = await CalculatePortfolioValueHomeAsync(portfolioId, beforeDate, portfolio.HomeCurrency, cancellationToken);
        var afterValueHome = await CalculatePortfolioValueHomeAsync(portfolioId, afterDate, portfolio.HomeCurrency, cancellationToken);

        // Source value：以 Portfolio.BaseCurrency 表示，若為台股會用匯率換算。
        var beforeValueSource = await CalculatePortfolioValueSourceAsync(portfolioId, beforeDate, portfolio.BaseCurrency, portfolio.HomeCurrency, cancellationToken);
        var afterValueSource = await CalculatePortfolioValueSourceAsync(portfolioId, afterDate, portfolio.BaseCurrency, portfolio.HomeCurrency, cancellationToken);

        if (portfolio.BoundCurrencyLedgerId.HasValue)
        {
            var ledger = await currencyLedgerRepository.GetByIdWithTransactionsAsync(
                portfolio.BoundCurrencyLedgerId.Value, cancellationToken);

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

        if (existing == null)
        {
            dbContext.TransactionPortfolioSnapshots.Add(entity);
        }
        else
        {
            // Replace row (simplest approach)
            dbContext.TransactionPortfolioSnapshots.Remove(existing);
            dbContext.TransactionPortfolioSnapshots.Add(entity);
        }

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
