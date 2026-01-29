using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

/// <summary>
/// 建立股票交易的 Use Case。
/// </summary>
public class CreateStockTransactionUseCase(
    IStockTransactionRepository transactionRepository,
    IPortfolioRepository portfolioRepository,
    ICurrencyLedgerRepository currencyLedgerRepository,
    ICurrencyTransactionRepository currencyTransactionRepository,
    PortfolioCalculator portfolioCalculator,
    CurrencyLedgerService currencyLedgerService,
    ICurrentUserService currentUserService,
    ITransactionDateExchangeRateService txDateFxService,
    IMonthlySnapshotService monthlySnapshotService,
    ITransactionPortfolioSnapshotService txSnapshotService)
{
    public async Task<StockTransactionDto> ExecuteAsync(
        CreateStockTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        // 確認投資組合存在，且屬於目前使用者
        var portfolio = await portfolioRepository.GetByIdAsync(request.PortfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", request.PortfolioId);

        if (portfolio.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        // 載入 Portfolio 綁定的 CurrencyLedger
        var boundLedger = await currencyLedgerRepository.GetByIdAsync(
            portfolio.BoundCurrencyLedgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", portfolio.BoundCurrencyLedgerId);

        if (boundLedger.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        // 推斷交易幣別
        var resolvedMarket = request.Market ?? StockTransaction.GuessMarketFromTicker(request.Ticker);
        var resolvedCurrency = request.Currency ?? StockTransaction.GuessCurrencyFromMarket(resolvedMarket);

        // FR-005: 驗證股票幣別與綁定帳本幣別一致
        StockTransactionLinking.ValidateCurrencyMatchesBoundLedger(resolvedCurrency, boundLedger);

        // 計算交易金額
        var subtotal = request.Shares * request.PricePerShare;
        if (resolvedMarket == StockMarket.TW)
            subtotal = Math.Floor(subtotal);
        var buyAmount = subtotal + request.Fees;

        // FR-012a: Buy 交易不得因餘額不足而阻擋
        if (request.TransactionType == TransactionType.Buy)
        {
            if (buyAmount <= 0)
                throw new BusinessRuleException("交易金額必須大於 0");
        }

        // 取得匯率
        var exchangeRate = request.ExchangeRate;
        if (exchangeRate is not > 0)
        {
            // 台股（數字開頭），匯率為 1.0
            if (resolvedCurrency == Currency.TWD)
            {
                exchangeRate = 1.0m;
            }
            else
            {
                // 非台股，自動抓取交易日當天的歷史匯率
                var fxResult = await txDateFxService.GetOrFetchAsync(
                    portfolio.BaseCurrency, portfolio.HomeCurrency, request.TransactionDate, cancellationToken);

                if (fxResult == null)
                    throw new BusinessRuleException(
                        $"無法取得 {portfolio.BaseCurrency}/{portfolio.HomeCurrency} 於 {request.TransactionDate:yyyy-MM-dd} 的匯率，請手動輸入匯率");

                exchangeRate = fxResult.Rate;
            }
        }

        // 若為賣出交易：檢查持股是否足夠，並計算已實現損益
        decimal? realizedPnlHome = null;
        if (request.TransactionType == TransactionType.Sell)
        {
            var existingTransactions = await transactionRepository.GetByPortfolioIdAsync(
                request.PortfolioId, cancellationToken);

            var currentPosition = portfolioCalculator.CalculatePosition(
                request.Ticker, existingTransactions);

            if (currentPosition.TotalShares < request.Shares)
                throw new BusinessRuleException(
                    $"持股不足。可賣出: {currentPosition.TotalShares:F4}，欲賣出: {request.Shares:F4}");

            // 建立暫時的賣出交易，用於損益計算
            var tempSellTransaction = new StockTransaction(
                request.PortfolioId,
                request.TransactionDate,
                request.Ticker,
                request.TransactionType,
                request.Shares,
                request.PricePerShare,
                exchangeRate,
                request.Fees,
                boundLedger.Id,
                request.Notes,
                request.Market,
                request.Currency);

            realizedPnlHome = portfolioCalculator.CalculateRealizedPnl(currentPosition, tempSellTransaction);
        }

        // 建立股票交易
        var transaction = new StockTransaction(
            request.PortfolioId,
            request.TransactionDate,
            request.Ticker,
            request.TransactionType,
            request.Shares,
            request.PricePerShare,
            exchangeRate,
            request.Fees,
            boundLedger.Id,
            request.Notes,
            request.Market,
            request.Currency);

        if (realizedPnlHome.HasValue)
        {
            transaction.SetRealizedPnl(realizedPnlHome.Value);
        }

        await transactionRepository.AddAsync(transaction, cancellationToken);

        // 交易異動後：使月度快照失效
        var affectedFromMonth = new DateOnly(request.TransactionDate.Year, request.TransactionDate.Month, 1);
        await monthlySnapshotService.InvalidateFromMonthAsync(
            request.PortfolioId, affectedFromMonth, cancellationToken);

        // 使用 shared helper 產生連動的 CurrencyTransaction 規格
        var linkedSpec = StockTransactionLinking.BuildLinkedCurrencyTransactionSpec(
            request.TransactionType, transaction, boundLedger);

        if (linkedSpec != null)
        {
            // FR-012: 若 Buy 且餘額不足，AutoDeposit=true 時自動建立 Deposit 補足差額
            if (request.TransactionType == TransactionType.Buy && request.AutoDeposit)
            {
                var ledgerTransactions = await currencyTransactionRepository.GetByLedgerIdOrderedAsync(
                    boundLedger.Id, cancellationToken);

                var currentBalance = currencyLedgerService.CalculateBalance(ledgerTransactions);
                var shortfall = linkedSpec.Amount - currentBalance;

                if (shortfall > 0)
                {
                    decimal? depositHomeAmount = null;
                    decimal? depositExchangeRate = null;
                    if (boundLedger.CurrencyCode == boundLedger.HomeCurrency)
                    {
                        depositExchangeRate = 1.0m;
                        depositHomeAmount = shortfall;
                    }

                    var depositTx = new CurrencyTransaction(
                        boundLedger.Id,
                        request.TransactionDate,
                        CurrencyTransactionType.Deposit,
                        shortfall,
                        homeAmount: depositHomeAmount,
                        exchangeRate: depositExchangeRate,
                        relatedStockTransactionId: transaction.Id,
                        notes: $"自動入金補足買入 {request.Ticker} × {request.Shares}");

                    await currencyTransactionRepository.AddAsync(depositTx, cancellationToken);

                    // Deposit 是 external cash flow，需要寫入快照
                    await txSnapshotService.UpsertSnapshotAsync(
                        request.PortfolioId,
                        depositTx.Id,
                        depositTx.TransactionDate,
                        cancellationToken);
                }
            }

            // 本幣帳本強制 exchangeRate=1.0
            decimal? linkedHomeAmount = null;
            decimal? linkedExchangeRate = null;
            if (boundLedger.CurrencyCode == boundLedger.HomeCurrency)
            {
                linkedExchangeRate = 1.0m;
                linkedHomeAmount = linkedSpec.Amount;
            }

            var noteAction = linkedSpec.TransactionType == CurrencyTransactionType.Spend ? "買入" : "賣出";
            var currencyTransaction = new CurrencyTransaction(
                boundLedger.Id,
                request.TransactionDate,
                linkedSpec.TransactionType,
                linkedSpec.Amount,
                homeAmount: linkedHomeAmount,
                exchangeRate: linkedExchangeRate,
                relatedStockTransactionId: transaction.Id,
                notes: $"{noteAction} {request.Ticker} × {request.Shares}");

            await currencyTransactionRepository.AddAsync(currencyTransaction, cancellationToken);
        }

        // US1: 寫入交易日快照
        await txSnapshotService.UpsertSnapshotAsync(
            request.PortfolioId,
            transaction.Id,
            transaction.TransactionDate,
            cancellationToken);

        return MapToDto(transaction);
    }

    private static StockTransactionDto MapToDto(StockTransaction transaction)
    {
        return new StockTransactionDto
        {
            Id = transaction.Id,
            PortfolioId = transaction.PortfolioId,
            TransactionDate = transaction.TransactionDate,
            Ticker = transaction.Ticker,
            TransactionType = transaction.TransactionType,
            Shares = transaction.Shares,
            PricePerShare = transaction.PricePerShare,
            ExchangeRate = transaction.ExchangeRate,
            Fees = transaction.Fees,
            CurrencyLedgerId = transaction.CurrencyLedgerId,
            Notes = transaction.Notes,
            TotalCostSource = transaction.TotalCostSource,
            TotalCostHome = transaction.TotalCostHome,
            HasExchangeRate = transaction.HasExchangeRate,
            RealizedPnlHome = transaction.RealizedPnlHome,
            Market = transaction.Market,
            Currency = transaction.Currency,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt
        };
    }
}
