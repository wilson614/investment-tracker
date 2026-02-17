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
        CancellationToken cancellationToken,
        SnapshotBackfillComputationContext? backfillContext = null)
    {
        var transactions = backfillContext?.AllTransactions
                           ?? await transactionRepository.GetByPortfolioIdAsync(portfolio.Id, cancellationToken);

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

        // 若快照皆存在，仍需驗算 dayStart/dayEnd 是否已過期；僅在值未變且已串接時跳過重寫。
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

            if (!alreadyChained)
            {
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

            // 即使 alreadyChained，也不能直接 return：
            // 交易更新（日期內移動、金額/費用變更）後，dayStart/dayEnd 可能已改變，
            // 若沿用舊值會留下過期快照。
            var recalculatedBeforeDate = date.AddDays(-1);

            var recalculatedDayStartHome = await GetCachedPortfolioValueHomeAsync(
                portfolio,
                recalculatedBeforeDate,
                cancellationToken,
                backfillContext);

            var recalculatedDayEndHome = await GetCachedPortfolioValueHomeAsync(
                portfolio,
                date,
                cancellationToken,
                backfillContext);

            var recalculatedDayStartSource = await GetCachedPortfolioValueSourceAsync(
                portfolio,
                recalculatedBeforeDate,
                cancellationToken,
                backfillContext);

            var recalculatedDayEndSource = await GetCachedPortfolioValueSourceAsync(
                portfolio,
                date,
                cancellationToken,
                backfillContext);

            if (!recalculatedDayStartHome.HasValue
                || !recalculatedDayEndHome.HasValue
                || !recalculatedDayStartSource.HasValue
                || !recalculatedDayEndSource.HasValue)
            {
                return;
            }

            var resolvedRecalculatedDayStartHome = recalculatedDayStartHome.Value;
            var resolvedRecalculatedDayEndHome = recalculatedDayEndHome.Value;
            var resolvedRecalculatedDayStartSource = recalculatedDayStartSource.Value;
            var resolvedRecalculatedDayEndSource = recalculatedDayEndSource.Value;

            var recalculatedLedger = await GetBoundLedgerAsync(
                portfolio,
                cancellationToken,
                backfillContext);

            if (recalculatedLedger is { IsActive: true } && recalculatedLedger.UserId == portfolio.UserId)
            {
                resolvedRecalculatedDayStartHome += await CalculateLedgerValueHomeAsync(recalculatedLedger, portfolio.HomeCurrency, recalculatedBeforeDate, cancellationToken);
                resolvedRecalculatedDayEndHome += await CalculateLedgerValueHomeAsync(recalculatedLedger, portfolio.HomeCurrency, date, cancellationToken);

                resolvedRecalculatedDayStartSource += await CalculateLedgerValueSourceAsync(recalculatedLedger, portfolio.BaseCurrency, portfolio.HomeCurrency, recalculatedBeforeDate, cancellationToken);
                resolvedRecalculatedDayEndSource += await CalculateLedgerValueSourceAsync(recalculatedLedger, portfolio.BaseCurrency, portfolio.HomeCurrency, date, cancellationToken);
            }

            var valuesUnchanged = firstSnapshot.PortfolioValueBeforeHome == resolvedRecalculatedDayStartHome
                                  && existingDayEndHome == resolvedRecalculatedDayEndHome
                                  && firstSnapshot.PortfolioValueBeforeSource == resolvedRecalculatedDayStartSource
                                  && existingDayEndSource == resolvedRecalculatedDayEndSource;

            if (valuesUnchanged)
                return;

            await ReplaceStockSnapshotsForDateAsync(
                portfolioId: portfolio.Id,
                snapshotDate: snapshotDate,
                dayTransactions: dayTransactions,
                dayStartHome: resolvedRecalculatedDayStartHome,
                dayEndHome: resolvedRecalculatedDayEndHome,
                dayStartSource: resolvedRecalculatedDayStartSource,
                dayEndSource: resolvedRecalculatedDayEndSource,
                cancellationToken: cancellationToken);

            return;
        }

        var beforeDate = date.AddDays(-1);

        var dayStartHome = await GetCachedPortfolioValueHomeAsync(
            portfolio,
            beforeDate,
            cancellationToken,
            backfillContext);

        var dayEndHome = await GetCachedPortfolioValueHomeAsync(
            portfolio,
            date,
            cancellationToken,
            backfillContext);

        var dayStartSource = await GetCachedPortfolioValueSourceAsync(
            portfolio,
            beforeDate,
            cancellationToken,
            backfillContext);

        var dayEndSource = await GetCachedPortfolioValueSourceAsync(
            portfolio,
            date,
            cancellationToken,
            backfillContext);

        if (!dayStartHome.HasValue
            || !dayEndHome.HasValue
            || !dayStartSource.HasValue
            || !dayEndSource.HasValue)
        {
            return;
        }

        var resolvedDayStartHome = dayStartHome.Value;
        var resolvedDayEndHome = dayEndHome.Value;
        var resolvedDayStartSource = dayStartSource.Value;
        var resolvedDayEndSource = dayEndSource.Value;

        var ledger = await GetBoundLedgerAsync(
            portfolio,
            cancellationToken,
            backfillContext);

        if (ledger is { IsActive: true } && ledger.UserId == portfolio.UserId)
        {
            resolvedDayStartHome += await CalculateLedgerValueHomeAsync(ledger, portfolio.HomeCurrency, beforeDate, cancellationToken);
            resolvedDayEndHome += await CalculateLedgerValueHomeAsync(ledger, portfolio.HomeCurrency, date, cancellationToken);

            resolvedDayStartSource += await CalculateLedgerValueSourceAsync(ledger, portfolio.BaseCurrency, portfolio.HomeCurrency, beforeDate, cancellationToken);
            resolvedDayEndSource += await CalculateLedgerValueSourceAsync(ledger, portfolio.BaseCurrency, portfolio.HomeCurrency, date, cancellationToken);
        }

        await ReplaceStockSnapshotsForDateAsync(
            portfolioId: portfolio.Id,
            snapshotDate: snapshotDate,
            dayTransactions: dayTransactions,
            dayStartHome: resolvedDayStartHome,
            dayEndHome: resolvedDayEndHome,
            dayStartSource: resolvedDayStartSource,
            dayEndSource: resolvedDayEndSource,
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

        if (!beforeValueHome.HasValue
            || !afterValueHome.HasValue
            || !beforeValueSource.HasValue
            || !afterValueSource.HasValue)
        {
            return;
        }

        var resolvedBeforeValueHome = beforeValueHome.Value;
        var resolvedAfterValueHome = afterValueHome.Value;
        var resolvedBeforeValueSource = beforeValueSource.Value;
        var resolvedAfterValueSource = afterValueSource.Value;

        var ledger = await currencyLedgerRepository.GetByIdWithTransactionsAsync(
            portfolio.BoundCurrencyLedgerId,
            cancellationToken);

        if (ledger is { IsActive: true } && ledger.UserId == portfolio.UserId)
        {
            resolvedBeforeValueHome += await CalculateLedgerValueHomeAsync(ledger, portfolio.HomeCurrency, beforeDate, cancellationToken);
            resolvedAfterValueHome += await CalculateLedgerValueHomeAsync(ledger, portfolio.HomeCurrency, afterDate, cancellationToken);

            resolvedBeforeValueSource += await CalculateLedgerValueSourceAsync(ledger, portfolio.BaseCurrency, portfolio.HomeCurrency, beforeDate, cancellationToken);
            resolvedAfterValueSource += await CalculateLedgerValueSourceAsync(ledger, portfolio.BaseCurrency, portfolio.HomeCurrency, afterDate, cancellationToken);
        }

        var entity = new TransactionPortfolioSnapshot(
            portfolioId,
            transactionId,
            snapshotDate,
            resolvedBeforeValueHome,
            resolvedAfterValueHome,
            resolvedBeforeValueSource,
            resolvedAfterValueSource);

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

        var allTransactions = (await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken)).ToList();
        var validTx = allTransactions
            .Where(t => t is { IsDeleted: false }
                        && t.TransactionDate.Date >= from
                        && t.TransactionDate.Date <= to
                        && t.TransactionType is TransactionType.Buy or TransactionType.Sell)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        var ledger = await currencyLedgerRepository.GetByIdWithTransactionsAsync(
            portfolio.BoundCurrencyLedgerId,
            cancellationToken);

        var boundLedger = ledger is { IsActive: true } && ledger.UserId == portfolio.UserId
            ? ledger
            : null;

        var backfillContext = new SnapshotBackfillComputationContext(
            allTransactions,
            boundLedger,
            string.Equals(portfolio.BaseCurrency, portfolio.HomeCurrency, StringComparison.OrdinalIgnoreCase));

        var externalLedgerTx = boundLedger != null
            ? boundLedger.Transactions
                .Where(t => t is { IsDeleted: false }
                            && t.TransactionDate.Date >= from
                            && t.TransactionDate.Date <= to
                            && IsExternalCashFlowForSnapshotBackfill(t, boundLedger.CurrencyCode))
                .OrderBy(t => t.TransactionDate)
                .ThenBy(t => t.CreatedAt)
                .ToList()
            : [];

        var allEventIds = validTx.Select(t => t.Id)
            .Concat(externalLedgerTx.Select(t => t.Id))
            .Distinct()
            .ToList();

        var stockDates = validTx
            .Select(t => DateOnly.FromDateTime(t.TransactionDate))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        foreach (var stockDate in stockDates)
        {
            await UpsertStockTransactionSnapshotsForDateAsync(
                portfolio,
                stockDate,
                cancellationToken,
                backfillContext);
        }

        if (boundLedger != null && externalLedgerTx.Count > 0)
        {
            var externalByDate = externalLedgerTx
                .GroupBy(t => DateOnly.FromDateTime(t.TransactionDate))
                .OrderBy(g => g.Key);

            foreach (var dayGroup in externalByDate)
            {
                await UpsertExternalLedgerSnapshotsForDateAsync(
                    portfolio,
                    boundLedger,
                    dayGroup.Key,
                    dayGroup.ToList(),
                    cancellationToken,
                    backfillContext);
            }
        }

        // 清理範圍內已不屬於 CF 事件集合的過期快照，避免 historical 計算讀到舊資料。
        if (allEventIds.Count == 0)
        {
            var staleInRange = await dbContext.TransactionPortfolioSnapshots
                .IgnoreQueryFilters()
                .Where(s => s.PortfolioId == portfolioId
                            && s.SnapshotDate >= from
                            && s.SnapshotDate <= to)
                .ToListAsync(cancellationToken);

            if (staleInRange.Count > 0)
            {
                dbContext.TransactionPortfolioSnapshots.RemoveRange(staleInRange);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        var staleSnapshots = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolioId
                        && s.SnapshotDate >= from
                        && s.SnapshotDate <= to
                        && !allEventIds.Contains(s.TransactionId))
            .ToListAsync(cancellationToken);

        if (staleSnapshots.Count > 0)
        {
            dbContext.TransactionPortfolioSnapshots.RemoveRange(staleSnapshots);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task UpsertExternalLedgerSnapshotsForDateAsync(
        Portfolio portfolio,
        CurrencyLedger ledger,
        DateOnly date,
        IReadOnlyList<CurrencyTransaction> dayTransactions,
        CancellationToken cancellationToken,
        SnapshotBackfillComputationContext? backfillContext = null)
    {
        if (dayTransactions.Count == 0)
            return;

        var snapshotDate = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var beforeDate = date.AddDays(-1);

        var dayStartHome = await GetCachedPortfolioValueHomeAsync(
            portfolio,
            beforeDate,
            cancellationToken,
            backfillContext);

        var dayEndHome = await GetCachedPortfolioValueHomeAsync(
            portfolio,
            date,
            cancellationToken,
            backfillContext);

        var dayStartSource = await GetCachedPortfolioValueSourceAsync(
            portfolio,
            beforeDate,
            cancellationToken,
            backfillContext);

        var dayEndSource = await GetCachedPortfolioValueSourceAsync(
            portfolio,
            date,
            cancellationToken,
            backfillContext);

        if (!dayStartHome.HasValue
            || !dayEndHome.HasValue
            || !dayStartSource.HasValue
            || !dayEndSource.HasValue)
        {
            return;
        }

        var resolvedDayStartHome = dayStartHome.Value;
        var resolvedDayEndHome = dayEndHome.Value;
        var resolvedDayStartSource = dayStartSource.Value;
        var resolvedDayEndSource = dayEndSource.Value;

        resolvedDayStartHome += await CalculateLedgerValueHomeAsync(ledger, portfolio.HomeCurrency, beforeDate, cancellationToken);
        resolvedDayEndHome += await CalculateLedgerValueHomeAsync(ledger, portfolio.HomeCurrency, date, cancellationToken);

        resolvedDayStartSource += await CalculateLedgerValueSourceAsync(ledger, portfolio.BaseCurrency, portfolio.HomeCurrency, beforeDate, cancellationToken);
        resolvedDayEndSource += await CalculateLedgerValueSourceAsync(ledger, portfolio.BaseCurrency, portfolio.HomeCurrency, date, cancellationToken);

        await ReplaceExternalLedgerSnapshotsForDateAsync(
            portfolio.Id,
            snapshotDate,
            dayTransactions,
            resolvedDayStartHome,
            resolvedDayEndHome,
            resolvedDayStartSource,
            resolvedDayEndSource,
            cancellationToken);
    }

    private async Task ReplaceExternalLedgerSnapshotsForDateAsync(
        Guid portfolioId,
        DateTime snapshotDate,
        IReadOnlyList<CurrencyTransaction> dayTransactions,
        decimal dayStartHome,
        decimal dayEndHome,
        decimal dayStartSource,
        decimal dayEndSource,
        CancellationToken cancellationToken)
    {
        var ordered = dayTransactions
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToList();

        if (ordered.Count == 0)
            return;

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

    private async Task<CurrencyLedger?> GetBoundLedgerAsync(
        Portfolio portfolio,
        CancellationToken cancellationToken,
        SnapshotBackfillComputationContext? backfillContext)
    {
        if (backfillContext != null)
        {
            return backfillContext.BoundLedger;
        }

        var ledger = await currencyLedgerRepository.GetByIdWithTransactionsAsync(
            portfolio.BoundCurrencyLedgerId,
            cancellationToken);

        return ledger is { IsActive: true } && ledger.UserId == portfolio.UserId
            ? ledger
            : null;
    }

    private async Task<decimal?> GetCachedPortfolioValueHomeAsync(
        Portfolio portfolio,
        DateOnly valuationDate,
        CancellationToken cancellationToken,
        SnapshotBackfillComputationContext? backfillContext)
    {
        if (backfillContext == null)
        {
            return await CalculatePortfolioValueHomeAsync(
                portfolio.Id,
                valuationDate,
                portfolio.HomeCurrency,
                cancellationToken);
        }

        var cacheKey = new SnapshotValuationCacheKey(valuationDate, portfolio.HomeCurrency);
        if (backfillContext.HomeValuationCache.TryGetValue(cacheKey, out var cachedHomeValue))
        {
            return cachedHomeValue;
        }

        var calculatedHomeValue = await CalculatePortfolioValueHomeAsync(
            portfolio.Id,
            valuationDate,
            portfolio.HomeCurrency,
            cancellationToken,
            backfillContext.AllTransactions);

        backfillContext.HomeValuationCache[cacheKey] = calculatedHomeValue;
        return calculatedHomeValue;
    }

    private async Task<decimal?> GetCachedPortfolioValueSourceAsync(
        Portfolio portfolio,
        DateOnly valuationDate,
        CancellationToken cancellationToken,
        SnapshotBackfillComputationContext? backfillContext)
    {
        if (backfillContext == null)
        {
            return await CalculatePortfolioValueSourceAsync(
                portfolio.Id,
                valuationDate,
                portfolio.BaseCurrency,
                portfolio.HomeCurrency,
                cancellationToken);
        }

        if (backfillContext.IsSourceSameAsHome)
        {
            return await GetCachedPortfolioValueHomeAsync(
                portfolio,
                valuationDate,
                cancellationToken,
                backfillContext);
        }

        var cacheKey = new SnapshotValuationCacheKey(valuationDate, portfolio.BaseCurrency);
        if (backfillContext.SourceValuationCache.TryGetValue(cacheKey, out var cachedSourceValue))
        {
            return cachedSourceValue;
        }

        var calculatedSourceValue = await CalculatePortfolioValueSourceAsync(
            portfolio.Id,
            valuationDate,
            portfolio.BaseCurrency,
            portfolio.HomeCurrency,
            cancellationToken,
            backfillContext.AllTransactions);

        backfillContext.SourceValuationCache[cacheKey] = calculatedSourceValue;
        return calculatedSourceValue;
    }

    private static bool IsExternalCashFlowForSnapshotBackfill(
        CurrencyTransaction transaction,
        string ledgerCurrencyCode)
    {
        if (transaction.TransactionType is CurrencyTransactionType.InitialBalance
            or CurrencyTransactionType.Deposit
            or CurrencyTransactionType.Withdraw
            or CurrencyTransactionType.OtherIncome
            or CurrencyTransactionType.OtherExpense)
        {
            if (transaction.RelatedStockTransactionId.HasValue
                && transaction.TransactionType == CurrencyTransactionType.OtherIncome
                && !IsStockTopUpEvent(transaction))
            {
                return false;
            }

            return true;
        }

        if (transaction.TransactionType is CurrencyTransactionType.ExchangeBuy or CurrencyTransactionType.ExchangeSell)
        {
            if (string.Equals(ledgerCurrencyCode, "TWD", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!transaction.RelatedStockTransactionId.HasValue)
                return true;

            // related-stock 的 top-up 是 external；其餘視為 internal FX transfer effect
            return IsStockTopUpEvent(transaction);
        }

        return false;
    }

    private static bool IsStockTopUpEvent(CurrencyTransaction transaction)
        => transaction.Notes?.StartsWith("補足買入", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<decimal?> CalculatePortfolioValueHomeAsync(
        Guid portfolioId,
        DateOnly valuationDate,
        string homeCurrency,
        CancellationToken cancellationToken,
        IReadOnlyList<StockTransaction>? allTransactions = null)
    {
        var transactions = allTransactions
                           ?? await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);

        var transactionsUpToDate = transactions
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
                return null;

            var exchangeRate = await GetExchangeRateAsync(priceInfo.Currency, homeCurrency, priceInfo.ActualDate, cancellationToken);
            if (exchangeRate == null)
                return null;

            total += position.TotalShares * priceInfo.Price * exchangeRate.Value;
        }

        return Math.Round(total, 4);
    }

    private async Task<decimal?> CalculatePortfolioValueSourceAsync(
        Guid portfolioId,
        DateOnly valuationDate,
        string sourceCurrency,
        string homeCurrency,
        CancellationToken cancellationToken,
        IReadOnlyList<StockTransaction>? allTransactions = null)
    {
        if (string.Equals(sourceCurrency, homeCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return await CalculatePortfolioValueHomeAsync(
                portfolioId,
                valuationDate,
                homeCurrency,
                cancellationToken,
                allTransactions);
        }

        var transactions = allTransactions
                           ?? await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);

        var transactionsUpToDate = transactions
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
                return null;

            // 先取得該價格幣別到 sourceCurrency 的匯率
            var exchangeRate = await GetExchangeRateAsync(priceInfo.Currency, sourceCurrency, priceInfo.ActualDate, cancellationToken);
            if (exchangeRate == null)
                return null;

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
        foreach (var yahooSymbol in GetYahooSymbolsForHistoricalLookup(ticker, market))
        {
            var yahoo = await yahooService.GetHistoricalPriceAsync(yahooSymbol, date, cancellationToken);
            if (yahoo == null)
            {
                continue;
            }

            return new HistoricalPriceInfo(
                Price: yahoo.Price,
                Currency: yahoo.Currency,
                ActualDate: yahoo.ActualDate,
                Market: market,
                Source: "Yahoo");
        }

        if (market == StockMarket.TW || IsTaiwanTicker(ticker))
        {
            var stockNo = ticker.Split('.')[0];
            var twse = await twseStockService.GetStockPriceAsync(stockNo, date, cancellationToken);
            if (twse != null)
            {
                return new HistoricalPriceInfo(
                    Price: twse.Price,
                    Currency: "TWD",
                    ActualDate: twse.ActualDate,
                    Market: StockMarket.TW,
                    Source: twse.Source is "TWSE" or "TPEx" ? twse.Source : "TWSE");
            }

            return null;
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

    private static IEnumerable<string> GetYahooSymbolsForHistoricalLookup(string ticker, StockMarket? market)
    {
        var isTaiwanStock = market == StockMarket.TW || IsTaiwanTicker(ticker);
        if (isTaiwanStock)
        {
            var baseTicker = ticker.Split('.')[0];
            var explicitSuffix = ticker.Contains('.') ? ticker[(ticker.LastIndexOf('.') + 1)..] : null;

            if (string.Equals(explicitSuffix, "TW", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{baseTicker}.TW";
                yield return $"{baseTicker}.TWO";
                yield break;
            }

            if (string.Equals(explicitSuffix, "TWO", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{baseTicker}.TWO";
                yield return $"{baseTicker}.TW";
                yield break;
            }

            yield return $"{baseTicker}.TW";
            yield return $"{baseTicker}.TWO";
            yield break;
        }

        yield return YahooSymbolHelper.ConvertToYahooSymbol(ticker, market);
    }

    private static bool IsTaiwanTicker(string ticker) =>
        !string.IsNullOrEmpty(ticker) && char.IsDigit(ticker[0]);

    private sealed record SnapshotValuationCacheKey(DateOnly Date, string Currency);

    private sealed class SnapshotBackfillComputationContext(
        IReadOnlyList<StockTransaction> allTransactions,
        CurrencyLedger? boundLedger,
        bool isSourceSameAsHome)
    {
        public IReadOnlyList<StockTransaction> AllTransactions { get; } = allTransactions;
        public CurrencyLedger? BoundLedger { get; } = boundLedger;
        public bool IsSourceSameAsHome { get; } = isSourceSameAsHome;

        public Dictionary<SnapshotValuationCacheKey, decimal?> HomeValuationCache { get; } = new();
        public Dictionary<SnapshotValuationCacheKey, decimal?> SourceValuationCache { get; } = new();
    }

    private sealed record HistoricalPriceInfo(
        decimal Price,
        string Currency,
        DateOnly ActualDate,
        StockMarket? Market,
        string Source);
}
