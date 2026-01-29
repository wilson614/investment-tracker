using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using PortfolioEntity = InvestmentTracker.Domain.Entities.Portfolio;

namespace InvestmentTracker.Application.UseCases.Assets;

/// <summary>
/// Get total assets summary for current user.
/// </summary>
public class GetTotalAssetsSummaryUseCase(
    IPortfolioRepository portfolioRepository,
    IStockTransactionRepository stockTransactionRepository,
    IStockSplitRepository stockSplitRepository,
    PortfolioCalculator portfolioCalculator,
    StockSplitAdjustmentService splitAdjustmentService,
    ITwseStockHistoricalPriceService twseStockHistoricalPriceService,
    IYahooHistoricalPriceService yahooHistoricalPriceService,
    IBankAccountRepository bankAccountRepository,
    TotalAssetsService totalAssetsService,
    ICurrentUserService currentUserService)
{
    private const string DefaultHomeCurrency = "TWD";

    public async Task<TotalAssetsSummaryResponse> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolios = await portfolioRepository.GetByUserIdAsync(userId, cancellationToken);
        var bankAccounts = await bankAccountRepository.GetByUserIdAsync(userId, cancellationToken);

        // Fetch splits once for all portfolios.
        var stockSplits = await stockSplitRepository.GetAllAsync(cancellationToken);

        var investmentTotal = 0m;
        var valuationDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        foreach (var portfolio in portfolios)
        {
            var portfolioValue = await CalculatePortfolioMarketValueHomeAsync(
                portfolio,
                valuationDate,
                stockSplits,
                cancellationToken);

            investmentTotal += portfolioValue;
        }

        var summary = totalAssetsService.Calculate(investmentTotal, bankAccounts);

        return new TotalAssetsSummaryResponse(
            InvestmentTotal: summary.InvestmentTotal,
            BankTotal: summary.BankTotal,
            GrandTotal: summary.GrandTotal,
            InvestmentPercentage: summary.InvestmentPercentage,
            BankPercentage: summary.BankPercentage,
            TotalMonthlyInterest: summary.TotalMonthlyInterest,
            TotalYearlyInterest: summary.TotalYearlyInterest);
    }

    private async Task<decimal> CalculatePortfolioMarketValueHomeAsync(
        PortfolioEntity portfolio,
        DateOnly valuationDate,
        IReadOnlyList<StockSplit> stockSplits,
        CancellationToken cancellationToken)
    {
        var transactions = await stockTransactionRepository.GetByPortfolioIdAsync(portfolio.Id, cancellationToken);

        var positions = portfolioCalculator
            .RecalculateAllPositionsWithSplitAdjustments(transactions, stockSplits, splitAdjustmentService)
            .Where(p => p.TotalShares > 0)
            .ToList();

        if (positions.Count == 0)
            return 0m;

        var total = 0m;

        foreach (var position in positions)
        {
            var market = position.Market ?? StockTransaction.GuessMarketFromTicker(position.Ticker);

            var priceResult = await GetPriceAsync(position.Ticker, market, valuationDate, cancellationToken);
            if (priceResult == null)
                continue;

            var exchangeRate = await GetExchangeRateAsync(
                fromCurrency: priceResult.Currency,
                toCurrency: DefaultHomeCurrency,
                date: priceResult.ActualDate,
                cancellationToken: cancellationToken);

            if (exchangeRate == null)
                continue;

            total += position.TotalShares * priceResult.Price * exchangeRate.Value;
        }

        return Math.Round(total, 4);
    }

    private async Task<HistoricalPriceInfo?> GetPriceAsync(
        string ticker,
        StockMarket market,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        // Taiwan stocks: use TWSE historical price.
        if (market == StockMarket.TW || IsTaiwanTicker(ticker))
        {
            var stockNo = ticker.Split('.')[0];
            var twse = await twseStockHistoricalPriceService.GetStockPriceAsync(stockNo, date, cancellationToken);
            if (twse == null)
                return null;

            return new HistoricalPriceInfo(
                Price: twse.Price,
                Currency: "TWD",
                ActualDate: twse.ActualDate);
        }

        var yahooSymbol = ConvertToYahooSymbol(ticker, market);
        var yahoo = await yahooHistoricalPriceService.GetHistoricalPriceAsync(yahooSymbol, date, cancellationToken);
        if (yahoo == null)
            return null;

        return new HistoricalPriceInfo(
            Price: yahoo.Price,
            Currency: yahoo.Currency,
            ActualDate: yahoo.ActualDate);
    }

    private async Task<decimal?> GetExchangeRateAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
            return 1m;

        var fx = await yahooHistoricalPriceService.GetExchangeRateAsync(
            fromCurrency,
            toCurrency,
            date,
            cancellationToken);

        return fx?.Rate;
    }

    private static string ConvertToYahooSymbol(string ticker, StockMarket market)
    {
        // If ticker already has a suffix (e.g., SWRD.L / AGAC.AS), keep it.
        if (ticker.Contains('.'))
            return ticker;

        return market switch
        {
            StockMarket.UK => $"{ticker}.L",
            StockMarket.EU => $"{ticker}.AS",
            _ => ticker
        };
    }

    private static bool IsTaiwanTicker(string ticker) =>
        !string.IsNullOrWhiteSpace(ticker) && char.IsDigit(ticker[0]);

    private sealed record HistoricalPriceInfo(
        decimal Price,
        string Currency,
        DateOnly ActualDate);
}
