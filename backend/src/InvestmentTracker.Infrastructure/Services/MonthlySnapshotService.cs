using System.Globalization;
using System.Text.Json;
using InvestmentTracker.Application.DTOs;
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

public class MonthlySnapshotService(
    AppDbContext dbContext,
    IPortfolioRepository portfolioRepository,
    IStockTransactionRepository transactionRepository,
    PortfolioCalculator portfolioCalculator,
    ICurrentUserService currentUserService,
    IYahooHistoricalPriceService yahooService,
    IStooqHistoricalPriceService stooqService,
    ITwseStockHistoricalPriceService twseStockService) : IMonthlySnapshotService
{
    public async Task InvalidateFromMonthAsync(
        Guid portfolioId,
        DateOnly fromMonth,
        CancellationToken cancellationToken = default)
    {
        var normalizedFrom = new DateOnly(fromMonth.Year, fromMonth.Month, 1);

        var toRemove = await dbContext.MonthlyNetWorthSnapshots
            .Where(s => s.PortfolioId == portfolioId && s.Month >= normalizedFrom)
            .ToListAsync(cancellationToken);

        if (toRemove.Count == 0)
        {
            return;
        }

        dbContext.MonthlyNetWorthSnapshots.RemoveRange(toRemove);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<MonthlyNetWorthHistoryDto> GetMonthlyNetWorthAsync(
        Guid portfolioId,
        DateOnly? fromMonth = null,
        DateOnly? toMonth = null,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", portfolioId);

        if (portfolio.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        var allTransactions = await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var validTransactions = allTransactions.Where(t => !t.IsDeleted).ToList();

        if (validTransactions.Count == 0)
        {
            return new MonthlyNetWorthHistoryDto
            {
                Data = [],
                Currency = portfolio.HomeCurrency,
                TotalMonths = 0,
                DataSource = "Calculated"
            };
        }

        var earliestTxDate = validTransactions.Min(t => t.TransactionDate.Date);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var resolvedFromMonth = NormalizeMonthOrDefault(fromMonth, DateOnly.FromDateTime(earliestTxDate));
        var resolvedToMonth = NormalizeMonthOrDefault(toMonth, new DateOnly(today.Year, today.Month, 1));

        if (resolvedFromMonth > resolvedToMonth)
        {
            (resolvedFromMonth, resolvedToMonth) = (resolvedToMonth, resolvedFromMonth);
        }

        var months = EnumerateMonths(resolvedFromMonth, resolvedToMonth).ToList();

        var results = new List<MonthlyNetWorthDto>(months.Count);
        var sourcesUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var month in months)
        {
            var valuationDate = GetValuationDate(month, today);

            var cached = await dbContext.MonthlyNetWorthSnapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.PortfolioId == portfolioId && s.Month == month, cancellationToken);

            if (cached != null)
            {
                results.Add(new MonthlyNetWorthDto
                {
                    Month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    Value = cached.TotalValueHome,
                    Contributions = cached.TotalContributions
                });

                sourcesUsed.Add(cached.DataSource);
                continue;
            }

            var transactionsUpToDate = validTransactions
                .Where(t => DateOnly.FromDateTime(t.TransactionDate) <= valuationDate)
                .OrderBy(t => t.TransactionDate)
                .ToList();

            var contributionsHome = await CalculateCumulativeNetContributionsHomeAsync(
                transactionsUpToDate,
                portfolio.HomeCurrency,
                valuationDate,
                cancellationToken);

            var positions = CalculatePositionsByMarket(transactionsUpToDate)
                .Where(p => p.TotalShares > 0)
                .ToList();

            if (positions.Count == 0)
            {
                results.Add(new MonthlyNetWorthDto
                {
                    Month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    Value = 0m,
                    Contributions = contributionsHome
                });

                await SaveSnapshotAsync(
                    portfolioId,
                    month,
                    totalValueHome: 0m,
                    totalContributions: contributionsHome ?? 0m,
                    dataSource: "Calculated",
                    calculatedAt: DateTime.UtcNow,
                    positionDetails: null,
                    cancellationToken);

                sourcesUsed.Add("Calculated");
                continue;
            }

            var totalValueHome = 0m;
            var perPositionDetails = new List<object>();

            var monthSourceSummary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var isComplete = true;

            foreach (var position in positions)
            {
                var market = position.Market;
                var ticker = position.Ticker;

                var priceResult = await GetHistoricalPriceAsync(
                    ticker,
                    market,
                    valuationDate,
                    cancellationToken);

                if (priceResult == null)
                {
                    isComplete = false;
                    perPositionDetails.Add(new
                    {
                        ticker,
                        market,
                        status = "missing_price"
                    });
                    continue;
                }

                var fromCurrency = priceResult.Currency;
                var exchangeRate = await GetExchangeRateAsync(
                    fromCurrency,
                    portfolio.HomeCurrency,
                    priceResult.ActualDate,
                    cancellationToken);

                if (exchangeRate == null)
                {
                    isComplete = false;
                    perPositionDetails.Add(new
                    {
                        ticker,
                        market,
                        status = "missing_fx",
                        price = priceResult.Price,
                        priceCurrency = priceResult.Currency,
                        priceActualDate = priceResult.ActualDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        fromCurrency,
                        toCurrency = portfolio.HomeCurrency
                    });
                    continue;
                }

                var positionValueHome = position.TotalShares * priceResult.Price * exchangeRate.Value;
                totalValueHome += positionValueHome;

                monthSourceSummary.Add(priceResult.Source);

                perPositionDetails.Add(new
                {
                    ticker,
                    market,
                    shares = position.TotalShares,
                    price = priceResult.Price,
                    priceCurrency = priceResult.Currency,
                    priceActualDate = priceResult.ActualDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    fx = exchangeRate.Value,
                    fxActualDate = priceResult.ActualDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    valueHome = Math.Round(positionValueHome, 4),
                    source = priceResult.Source
                });
            }

            var monthLabel = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

            if (!isComplete)
            {
                results.Add(new MonthlyNetWorthDto
                {
                    Month = monthLabel,
                    Value = null,
                    Contributions = contributionsHome
                });

                sourcesUsed.Add("Mixed");
                continue;
            }

            var dataSource = monthSourceSummary.Count switch
            {
                0 => "Calculated",
                1 => monthSourceSummary.First(),
                _ => "Mixed"
            };

            results.Add(new MonthlyNetWorthDto
            {
                Month = monthLabel,
                Value = Math.Round(totalValueHome, 4),
                Contributions = contributionsHome
            });

            sourcesUsed.Add(dataSource);

            var positionDetailsJson = JsonSerializer.Serialize(new
            {
                valuationDate = valuationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                positions = perPositionDetails
            });

            await SaveSnapshotAsync(
                portfolioId,
                month,
                totalValueHome: totalValueHome,
                totalContributions: contributionsHome ?? 0m,
                dataSource: dataSource,
                calculatedAt: DateTime.UtcNow,
                positionDetails: positionDetailsJson,
                cancellationToken);
        }

        var overallSource = sourcesUsed.Count switch
        {
            0 => "Calculated",
            1 => sourcesUsed.First(),
            _ => "Mixed"
        };

        return new MonthlyNetWorthHistoryDto
        {
            Data = results,
            Currency = portfolio.HomeCurrency,
            TotalMonths = results.Count,
            DataSource = overallSource
        };
    }

    private static DateOnly NormalizeMonthOrDefault(DateOnly? month, DateOnly fallback)
    {
        var value = month ?? fallback;
        return new DateOnly(value.Year, value.Month, 1);
    }

    private static IEnumerable<DateOnly> EnumerateMonths(DateOnly fromMonth, DateOnly toMonth)
    {
        var cursor = new DateOnly(fromMonth.Year, fromMonth.Month, 1);
        var end = new DateOnly(toMonth.Year, toMonth.Month, 1);

        while (cursor <= end)
        {
            yield return cursor;
            cursor = cursor.AddMonths(1);
        }
    }

    private static DateOnly GetValuationDate(DateOnly month, DateOnly today)
    {
        var isCurrentMonth = month.Year == today.Year && month.Month == today.Month;
        if (isCurrentMonth)
        {
            return today;
        }

        var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
        return new DateOnly(month.Year, month.Month, lastDay);
    }

    private IEnumerable<StockPosition> CalculatePositionsByMarket(IReadOnlyList<StockTransaction> transactions)
    {
        var keys = transactions
            .Select(t => (t.Ticker, t.Market))
            .Distinct();

        foreach (var (ticker, market) in keys)
        {
            yield return portfolioCalculator.CalculatePositionByMarket(ticker, market, transactions);
        }
    }

    private async Task<decimal?> CalculateCumulativeNetContributionsHomeAsync(
        IReadOnlyList<StockTransaction> transactionsUpToDate,
        string homeCurrency,
        DateOnly valuationDate,
        CancellationToken cancellationToken)
    {
        if (transactionsUpToDate.Count == 0)
        {
            return 0m;
        }

        var totalBuys = 0m;
        var totalSells = 0m;

        foreach (var tx in transactionsUpToDate)
        {
            if (tx.TransactionType is not (TransactionType.Buy or TransactionType.Sell))
                continue;

            var fx = await GetTransactionExchangeRateAsync(tx, homeCurrency, cancellationToken);
            if (fx == null)
            {
                return null;
            }

            if (tx.TransactionType == TransactionType.Buy)
            {
                totalBuys += tx.TotalCostSource * fx.Value;
                continue;
            }

            var proceedsSource = tx.Shares * tx.PricePerShare - tx.Fees;
            totalSells += proceedsSource * fx.Value;
        }

        return Math.Round(totalBuys - totalSells, 4);
    }

    private async Task<decimal?> GetTransactionExchangeRateAsync(
        StockTransaction tx,
        string homeCurrency,
        CancellationToken cancellationToken)
    {
        if (tx.IsTaiwanStock)
        {
            return 1m;
        }

        if (tx.HasExchangeRate)
        {
            return tx.ExchangeRate!.Value;
        }

        var fromCurrency = tx.Currency.ToString();
        var toCurrency = homeCurrency;

        return await GetExchangeRateAsync(
            fromCurrency,
            toCurrency,
            DateOnly.FromDateTime(tx.TransactionDate),
            cancellationToken);
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
        return result?.Rate;
    }

    private async Task<HistoricalPriceInfo?> GetHistoricalPriceAsync(
        string ticker,
        StockMarket? market,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        // Taiwan stocks: use TWSE
        if (market == StockMarket.TW || IsTaiwanTicker(ticker))
        {
            var stockNo = ticker.Split('.')[0];
            var twse = await twseStockService.GetStockPriceAsync(stockNo, date, cancellationToken);
            if (twse == null)
            {
                return null;
            }

            return new HistoricalPriceInfo(
                Price: twse.Price,
                Currency: "TWD",
                ActualDate: twse.ActualDate,
                Source: "TWSE");
        }

        // Non-TW: Yahoo primary
        var yahooSymbol = YahooSymbolHelper.ConvertToYahooSymbol(ticker, market);
        var yahoo = await yahooService.GetHistoricalPriceAsync(yahooSymbol, date, cancellationToken);
        if (yahoo != null)
        {
            return new HistoricalPriceInfo(
                Price: yahoo.Price,
                Currency: yahoo.Currency,
                ActualDate: yahoo.ActualDate,
                Source: "Yahoo");
        }

        // Fallback: Stooq (skip EU market where Yahoo is expected to handle)
        if (market != StockMarket.EU)
        {
            var stooq = await stooqService.GetStockPriceAsync(ticker, date, cancellationToken);
            if (stooq != null)
            {
                return new HistoricalPriceInfo(
                    Price: stooq.Price,
                    Currency: stooq.Currency,
                    ActualDate: stooq.ActualDate,
                    Source: "Stooq");
            }
        }

        return null;
    }

    private static bool IsTaiwanTicker(string ticker) =>
        !string.IsNullOrEmpty(ticker) && char.IsDigit(ticker[0]);

    private async Task SaveSnapshotAsync(
        Guid portfolioId,
        DateOnly month,
        decimal totalValueHome,
        decimal totalContributions,
        string dataSource,
        DateTime calculatedAt,
        string? positionDetails,
        CancellationToken cancellationToken)
    {
        var entity = new MonthlyNetWorthSnapshot(
            portfolioId,
            month,
            totalValueHome,
            totalContributions,
            dataSource,
            calculatedAt,
            positionDetails);

        dbContext.MonthlyNetWorthSnapshots.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Likely unique constraint conflict (race). Ignore.
        }
    }

    private sealed record HistoricalPriceInfo(
        decimal Price,
        string Currency,
        DateOnly ActualDate,
        string Source);
}
