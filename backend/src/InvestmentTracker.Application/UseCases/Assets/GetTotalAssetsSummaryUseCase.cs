using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using BankAccountEntity = InvestmentTracker.Domain.Entities.BankAccount;
using PortfolioEntity = InvestmentTracker.Domain.Entities.Portfolio;

namespace InvestmentTracker.Application.UseCases.Assets;

/// <summary>
/// Get total assets summary for current user.
/// </summary>
public class GetTotalAssetsSummaryUseCase(
    IPortfolioRepository portfolioRepository,
    ICurrencyLedgerRepository currencyLedgerRepository,
    IStockTransactionRepository stockTransactionRepository,
    IStockSplitRepository stockSplitRepository,
    PortfolioCalculator portfolioCalculator,
    StockSplitAdjustmentService splitAdjustmentService,
    CurrencyLedgerService currencyLedgerService,
    ITwseStockHistoricalPriceService twseStockHistoricalPriceService,
    IYahooHistoricalPriceService yahooHistoricalPriceService,
    IBankAccountRepository bankAccountRepository,
    IFundAllocationRepository fundAllocationRepository,
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

        var portfolioMarketValue = 0m;
        var cashBalance = 0m;
        var valuationDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        foreach (var portfolio in portfolios)
        {
            var marketValue = await CalculatePortfolioMarketValueHomeAsync(
                portfolio,
                valuationDate,
                stockSplits,
                cancellationToken);

            var ledgerCashBalance = await CalculatePortfolioCashBalanceHomeAsync(
                portfolio,
                valuationDate,
                cancellationToken);

            portfolioMarketValue += marketValue;
            cashBalance += ledgerCashBalance;
        }

        var portfolioValue = portfolioMarketValue + cashBalance;
        var bankExchangeRates = await GetBankExchangeRatesToTwdAsync(bankAccounts, valuationDate, cancellationToken);

        var summary = totalAssetsService.Calculate(
            portfolioMarketValue,
            bankAccounts,
            bankExchangeRates);

        var fundAllocations = await fundAllocationRepository.GetByUserIdAsync(userId, cancellationToken);
        var disposableDeposit = fundAllocations
            .Where(x => x.IsDisposable)
            .Sum(x => x.Amount);
        var nonDisposableDeposit = fundAllocations
            .Where(x => !x.IsDisposable)
            .Sum(x => x.Amount);
        var totalAllocated = disposableDeposit + nonDisposableDeposit;
        var unallocated = summary.BankTotal - totalAllocated;

        var investmentRatioDenominator = portfolioValue + disposableDeposit;
        var investmentRatio = investmentRatioDenominator > 0m
            ? portfolioValue / investmentRatioDenominator
            : 0m;

        var stockRatio = portfolioValue > 0m
            ? portfolioMarketValue / portfolioValue
            : 0m;

        var allocationBreakdown = fundAllocations
            .Select(x => new AllocationBreakdownResponse(
                Purpose: x.Purpose,
                PurposeDisplayName: FundAllocationResponse.GetPurposeDisplayName(x.Purpose),
                Amount: x.Amount))
            .ToList();

        return new TotalAssetsSummaryResponse(
            InvestmentTotal: summary.InvestmentTotal,
            BankTotal: summary.BankTotal,
            GrandTotal: summary.GrandTotal,
            InvestmentPercentage: summary.InvestmentPercentage,
            BankPercentage: summary.BankPercentage,
            TotalMonthlyInterest: summary.TotalMonthlyInterest,
            TotalYearlyInterest: summary.TotalYearlyInterest,
            PortfolioValue: portfolioValue,
            CashBalance: cashBalance,
            DisposableDeposit: disposableDeposit,
            NonDisposableDeposit: nonDisposableDeposit,
            InvestmentRatio: investmentRatio,
            StockRatio: stockRatio,
            TotalAllocated: totalAllocated,
            Unallocated: unallocated,
            AllocationBreakdown: allocationBreakdown);
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

    private async Task<decimal> CalculatePortfolioCashBalanceHomeAsync(
        PortfolioEntity portfolio,
        DateOnly valuationDate,
        CancellationToken cancellationToken)
    {
        var ledger = await currencyLedgerRepository.GetByIdWithTransactionsAsync(
            portfolio.BoundCurrencyLedgerId,
            cancellationToken);

        if (ledger == null)
            return 0m;

        var transactionsUpToDate = ledger.Transactions
            .Where(t => !t.IsDeleted && DateOnly.FromDateTime(t.TransactionDate) <= valuationDate)
            .ToList();

        var balance = currencyLedgerService.CalculateBalance(transactionsUpToDate);
        if (balance <= 0m)
            return 0m;

        var exchangeRate = await GetExchangeRateAsync(
            fromCurrency: ledger.CurrencyCode,
            toCurrency: DefaultHomeCurrency,
            date: valuationDate,
            cancellationToken: cancellationToken);

        if (exchangeRate is not > 0m)
            return 0m;

        return Math.Round(balance * exchangeRate.Value, 4);
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

    private async Task<IReadOnlyDictionary<string, decimal>> GetBankExchangeRatesToTwdAsync(
        IReadOnlyList<BankAccountEntity> bankAccounts,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var currencies = bankAccounts
            .Select(a => a.Currency)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultHomeCurrency] = 1m
        };

        foreach (var currency in currencies)
        {
            if (string.Equals(currency, DefaultHomeCurrency, StringComparison.OrdinalIgnoreCase))
                continue;

            var fx = await GetExchangeRateAsync(currency, DefaultHomeCurrency, date, cancellationToken);
            if (fx is > 0m)
            {
                rates[currency] = fx.Value;
            }
        }

        return rates;
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
