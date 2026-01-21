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
    ITransactionDateExchangeRateService txDateFxService)
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

        // 取得匯率：若未提供，且資金來源為外幣帳本時，會自動由帳本推算
        var exchangeRate = request.ExchangeRate;
        Domain.Entities.CurrencyLedger? currencyLedger = null;
        decimal? requiredAmount = null;

        if (request is { FundSource: FundSource.CurrencyLedger, CurrencyLedgerId: not null })
        {
            currencyLedger = await currencyLedgerRepository.GetByIdWithTransactionsAsync(
                request.CurrencyLedgerId.Value, cancellationToken)
                ?? throw new EntityNotFoundException("CurrencyLedger", request.CurrencyLedgerId);

            // 確認外幣帳本屬於目前使用者
            if (currencyLedger.UserId != currentUserService.UserId)
                throw new AccessDeniedException();

            // 取得既有外幣交易
            var currencyTransactions = await currencyTransactionRepository.GetByLedgerIdOrderedAsync(
                currencyLedger.Id, cancellationToken);

            // 計算所需外幣金額（以原始幣別計算的總成本）
            requiredAmount = request.Shares * request.PricePerShare + request.Fees;

            // 在建立任何交易前先確認餘額足夠
            if (!currencyLedgerService.ValidateSpend(currencyTransactions, requiredAmount.Value))
                throw new BusinessRuleException("Insufficient balance");

            // 若未提供匯率，則以近期換匯交易推算（LIFO）
            // 僅考慮實際換匯交易，不包含利息/紅利
            if (exchangeRate is not > 0)
            {
                var calculatedRate = currencyLedgerService.CalculateExchangeRateForPurchase(
                    currencyTransactions, request.TransactionDate, requiredAmount.Value);

                if (calculatedRate <= 0)
                    throw new BusinessRuleException(
                        $"Cannot calculate exchange rate from currency ledger. No transactions found on or before {request.TransactionDate:yyyy-MM-dd}");

                exchangeRate = calculatedRate;
            }
        }
        else if (exchangeRate is not > 0)
        {
            // 未使用外幣帳本且未提供匯率，需自動取得
            // 台股以數字開頭（如 0050、2330），匯率為 1.0
            if (!string.IsNullOrEmpty(request.Ticker) && char.IsDigit(request.Ticker[0]))
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
            // 取得此投資組合的所有交易
            var existingTransactions = await transactionRepository.GetByPortfolioIdAsync(
                request.PortfolioId, cancellationToken);

            // 計算此 ticker 的目前持倉
            var currentPosition = portfolioCalculator.CalculatePosition(
                request.Ticker, existingTransactions);

            // 確認持股足夠
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
                request.FundSource,
                request.CurrencyLedgerId,
                request.Notes,
                request.Market);

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
            request.FundSource,
            request.CurrencyLedgerId,
            request.Notes,
            request.Market);

        // 若為賣出交易，設定已實現損益
        if (realizedPnlHome.HasValue)
        {
            transaction.SetRealizedPnl(realizedPnlHome.Value);
        }

        await transactionRepository.AddAsync(transaction, cancellationToken);

        // 股票交易建立後（取得 ID），再建立連動的外幣交易
        if (currencyLedger != null && requiredAmount.HasValue)
        {
            var currencyTransaction = new CurrencyTransaction(
                currencyLedger.Id,
                request.TransactionDate,
                CurrencyTransactionType.Spend,
                requiredAmount.Value,
                relatedStockTransactionId: transaction.Id,
                notes: $"買入 {request.Ticker} × {request.Shares}");

            await currencyTransactionRepository.AddAsync(currencyTransaction, cancellationToken);
        }

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
            FundSource = transaction.FundSource,
            CurrencyLedgerId = transaction.CurrencyLedgerId,
            Notes = transaction.Notes,
            TotalCostSource = transaction.TotalCostSource,
            TotalCostHome = transaction.TotalCostHome,
            HasExchangeRate = transaction.HasExchangeRate,
            RealizedPnlHome = transaction.RealizedPnlHome,
            Market = transaction.Market,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt
        };
    }
}
