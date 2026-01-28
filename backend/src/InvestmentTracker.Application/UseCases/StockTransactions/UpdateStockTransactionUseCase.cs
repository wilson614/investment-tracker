using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

/// <summary>
/// 更新股票交易的 Use Case。
/// </summary>
public class UpdateStockTransactionUseCase(
    IStockTransactionRepository transactionRepository,
    IPortfolioRepository portfolioRepository,
    ICurrencyTransactionRepository currencyTransactionRepository,
    ICurrencyLedgerRepository currencyLedgerRepository,
    CurrencyLedgerService currencyLedgerService,
    ICurrentUserService currentUserService,
    PortfolioCalculator portfolioCalculator,
    ITransactionDateExchangeRateService txDateFxService,
    IMonthlySnapshotService monthlySnapshotService,
    ITransactionPortfolioSnapshotService txSnapshotService)
{
    public async Task<StockTransactionDto> ExecuteAsync(
        Guid transactionId,
        UpdateStockTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId, cancellationToken)
            ?? throw new EntityNotFoundException("Transaction", transactionId);

        // 透過投資組合確認存取權限
        var portfolio = await portfolioRepository.GetByIdAsync(transaction.PortfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", transaction.PortfolioId);

        if (portfolio.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        // 載入 Portfolio 綁定的 CurrencyLedger
        var boundLedger = await currencyLedgerRepository.GetByIdAsync(
            portfolio.BoundCurrencyLedgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", portfolio.BoundCurrencyLedgerId);

        // 推斷交易幣別
        var resolvedMarket = request.Market ?? StockTransaction.GuessMarketFromTicker(request.Ticker);
        var resolvedCurrency = request.Currency ?? StockTransaction.GuessCurrencyFromMarket(resolvedMarket);

        // FR-005: 驗證股票幣別與綁定帳本幣別一致
        StockTransactionLinking.ValidateCurrencyMatchesBoundLedger(resolvedCurrency, boundLedger);

        // 保留原始值供後續計算使用
        var originalType = transaction.TransactionType;
        var originalTransactionDate = transaction.TransactionDate;

        // 處理匯率：若未提供，自動抓取交易日匯率
        var exchangeRate = request.ExchangeRate;
        if (exchangeRate is not > 0)
        {
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

        // 更新交易屬性（包含 ticker 與交易類型）
        transaction.SetTicker(request.Ticker);
        transaction.SetTransactionType(request.TransactionType);
        transaction.SetTransactionDate(request.TransactionDate);
        transaction.SetShares(request.Shares);
        transaction.SetPricePerShare(request.PricePerShare);
        transaction.SetExchangeRate(exchangeRate);
        transaction.SetFees(request.Fees);
        transaction.SetCurrencyLedgerId(portfolio.BoundCurrencyLedgerId);
        transaction.SetNotes(request.Notes);
        if (request.Market.HasValue)
            transaction.SetMarket(request.Market.Value);
        if (request.Currency.HasValue)
            transaction.SetCurrency(request.Currency.Value);

        // 若為賣出交易：重新計算已實現損益（支援 Buy/Sell 互換）
        if (transaction.TransactionType == TransactionType.Sell)
        {
            // 取得此投資組合的所有交易
            var allTransactions = await transactionRepository.GetByPortfolioIdAsync(
                transaction.PortfolioId, cancellationToken);

            // 只使用較早的交易，計算此賣出交易發生前的持倉
            var transactionsBeforeSell = allTransactions
                .Where(t => t.Id != transaction.Id) // 排除目前這筆交易
                .Where(t => t.TransactionDate < transaction.TransactionDate ||
                           (t.TransactionDate == transaction.TransactionDate && t.CreatedAt < transaction.CreatedAt))
                .ToList();

            var positionBeforeSell = portfolioCalculator.CalculatePosition(
                transaction.Ticker, transactionsBeforeSell);

            // 確認持股足夠
            if (positionBeforeSell.TotalShares < request.Shares)
                throw new BusinessRuleException(
                    $"持股不足。可賣出: {positionBeforeSell.TotalShares:F4}，欲賣出: {request.Shares:F4}");

            // 建立暫時交易（使用更新後的數值）用於損益計算
            var tempSellTransaction = new StockTransaction(
                transaction.PortfolioId,
                request.TransactionDate,
                transaction.Ticker,
                TransactionType.Sell,
                request.Shares,
                request.PricePerShare,
                exchangeRate ?? 1.0m,
                request.Fees);

            var realizedPnl = portfolioCalculator.CalculateRealizedPnl(positionBeforeSell, tempSellTransaction);
            transaction.SetRealizedPnl(realizedPnl);
        }
        else if (originalType == TransactionType.Sell)
        {
            // 若從 Sell 改為 Buy，清除已實現損益
            transaction.SetRealizedPnl(null);
        }

        await transactionRepository.UpdateAsync(transaction, cancellationToken);

        // 交易異動後：使月度快取失效（從影響月份起）
        var originalDateOnly = DateOnly.FromDateTime(originalTransactionDate);
        var newDateOnly = DateOnly.FromDateTime(transaction.TransactionDate);
        var affectedDate = originalDateOnly <= newDateOnly ? originalDateOnly : newDateOnly;
        var affectedFromMonth = new DateOnly(affectedDate.Year, affectedDate.Month, 1);
        await monthlySnapshotService.InvalidateFromMonthAsync(
            transaction.PortfolioId, affectedFromMonth, cancellationToken);

        // US1: 寫入交易日快照（before/after），供 Dietz/TWR 計算使用
        await txSnapshotService.UpsertSnapshotAsync(
            transaction.PortfolioId,
            transaction.Id,
            transaction.TransactionDate,
            cancellationToken);

        // FR-009: 同步更新連動的 CurrencyTransaction（若存在）
        var linkedCurrencyTransaction = await currencyTransactionRepository.GetByStockTransactionIdAsync(
            transaction.Id, cancellationToken);

        if (linkedCurrencyTransaction != null)
        {
            // 取得連動帳本以判斷是否為 TWD 帳本
            var linkedLedger = await currencyLedgerRepository.GetByIdAsync(
                linkedCurrencyTransaction.CurrencyLedgerId, cancellationToken);

            // CRIT-5: 驗證連動帳本所有權
            if (linkedLedger != null && linkedLedger.UserId != currentUserService.UserId)
                throw new AccessDeniedException();

            if (linkedLedger != null)
            {
                // 計算新金額
                decimal newAmount;
                CurrencyTransactionType newType;
                string newNotes;

                if (linkedLedger.CurrencyCode == "TWD")
                {
                    // TWD 帳本使用 flooring
                    var subtotal = Math.Floor(request.Shares * request.PricePerShare);

                    if (request.TransactionType == TransactionType.Buy)
                    {
                        newAmount = subtotal + request.Fees;
                        newType = CurrencyTransactionType.Spend;
                        newNotes = $"買入 {request.Ticker} × {request.Shares}";

                        // 驗證餘額（排除當前連動交易後的餘額）
                        var ledgerTransactions = await currencyTransactionRepository.GetByLedgerIdOrderedAsync(
                            linkedLedger.Id, cancellationToken);
                        var balanceWithoutCurrent = currencyLedgerService.CalculateBalance(
                            ledgerTransactions.Where(t => t.Id != linkedCurrencyTransaction.Id));
                        if (balanceWithoutCurrent < newAmount)
                            throw new BusinessRuleException("TWD 帳本餘額不足");
                    }
                    else if (request.TransactionType == TransactionType.Sell)
                    {
                        newAmount = subtotal - request.Fees;
                        newType = CurrencyTransactionType.OtherIncome;
                        newNotes = $"賣出 {request.Ticker} × {request.Shares}";

                        // Sell 金額為 0 或負時刪除連動交易
                        if (newAmount <= 0)
                        {
                            await currencyTransactionRepository.SoftDeleteAsync(linkedCurrencyTransaction.Id, cancellationToken);
                            linkedCurrencyTransaction = null;
                        }
                    }
                    else
                    {
                        // Split/Adjustment 改動時刪除連動交易
                        await currencyTransactionRepository.SoftDeleteAsync(linkedCurrencyTransaction.Id, cancellationToken);
                        linkedCurrencyTransaction = null;
                        newAmount = 0;
                        newType = CurrencyTransactionType.Spend;
                        newNotes = "";
                    }

                    if (linkedCurrencyTransaction != null && newAmount > 0)
                    {
                        linkedCurrencyTransaction.SetTransactionDate(request.TransactionDate);
                        linkedCurrencyTransaction.SetAmounts(newType, newAmount, homeAmount: newAmount, exchangeRate: 1.0m);
                        linkedCurrencyTransaction.SetNotes(newNotes);
                        await currencyTransactionRepository.UpdateAsync(linkedCurrencyTransaction, cancellationToken);
                    }
                }
                else
                {
                    // 外幣帳本：金額計算方式與原有邏輯一致
                    newAmount = request.Shares * request.PricePerShare + request.Fees;
                    newNotes = $"買入 {request.Ticker} × {request.Shares}";

                    linkedCurrencyTransaction.SetTransactionDate(request.TransactionDate);
                    linkedCurrencyTransaction.SetAmounts(
                        linkedCurrencyTransaction.TransactionType,
                        newAmount,
                        linkedCurrencyTransaction.HomeAmount,
                        linkedCurrencyTransaction.ExchangeRate);
                    linkedCurrencyTransaction.SetNotes(newNotes);
                    await currencyTransactionRepository.UpdateAsync(linkedCurrencyTransaction, cancellationToken);
                }
            }
        }

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
