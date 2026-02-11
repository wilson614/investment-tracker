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

        // 計算交易金額（供匯率計算使用；台股小計採無條件捨去）
        var subtotalForRate = request.Shares * request.PricePerShare;
        if (resolvedMarket == StockMarket.TW)
            subtotalForRate = Math.Floor(subtotalForRate);
        var amountForRate = subtotalForRate + request.Fees;

        // 匯率改為系統計算（不接受手動輸入）
        decimal? exchangeRate;
        if (resolvedCurrency == Currency.TWD)
        {
            exchangeRate = 1.0m;
        }
        else if (request.TransactionType == TransactionType.Buy)
        {
            var ledgerTransactionsForRate = await currencyTransactionRepository.GetByLedgerIdOrderedAsync(
                boundLedger.Id, cancellationToken);

            // 排除與本交易關聯的交易，避免計算到舊的連動扣款/入帳
            var ledgerTransactionsWithoutCurrent = ledgerTransactionsForRate
                .Where(t => t.RelatedStockTransactionId != transaction.Id)
                .ToList();

            var lifoRate = currencyLedgerService.CalculateExchangeRateForPurchase(
                ledgerTransactionsWithoutCurrent,
                request.TransactionDate,
                amountForRate);

            if (lifoRate > 0)
            {
                exchangeRate = lifoRate;
            }
            else
            {
                var fxResult = await txDateFxService.GetOrFetchAsync(
                    portfolio.BaseCurrency, portfolio.HomeCurrency, request.TransactionDate, cancellationToken);

                if (fxResult == null)
                    throw new BusinessRuleException("無法計算匯率，請先在帳本中建立換匯紀錄");

                exchangeRate = fxResult.Rate;
            }
        }
        else
        {
            var fxResult = await txDateFxService.GetOrFetchAsync(
                portfolio.BaseCurrency, portfolio.HomeCurrency, request.TransactionDate, cancellationToken);

            if (fxResult == null)
                throw new BusinessRuleException("無法計算匯率，請先在帳本中建立換匯紀錄");

            exchangeRate = fxResult.Rate;
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

        // FR-009: 同步更新連動的 CurrencyTransaction
        // - 同一筆 StockTransaction 可能有多筆連動交易（TopUp + Spend/OtherIncome）
        // - GetByStockTransactionIdAsync 只回傳 Spend/OtherIncome，避免抓到其他入帳交易
        var linkedCurrencyTransaction = await currencyTransactionRepository.GetByStockTransactionIdAsync(
            transaction.Id, cancellationToken);

        var linkedCurrencyTransactions = await currencyTransactionRepository.GetByStockTransactionIdAllAsync(
            transaction.Id, cancellationToken);

        var linkedTopUp = linkedCurrencyTransactions
            .FirstOrDefault(t => t.TransactionType != CurrencyTransactionType.Spend
                && t.TransactionType != CurrencyTransactionType.OtherIncome);

        // 取得連動帳本（以 Portfolio 綁定帳本為準）
        var linkedLedger = boundLedger;

        // 若交易不再是 Buy/Sell，刪除所有連動交易（包含 Deposit）
        if (request.TransactionType is not (TransactionType.Buy or TransactionType.Sell))
        {
            foreach (var linked in linkedCurrencyTransactions)
            {
                await currencyTransactionRepository.SoftDeleteAsync(linked.Id, cancellationToken);

                if (linked.TransactionType is CurrencyTransactionType.InitialBalance
                    or CurrencyTransactionType.Deposit
                    or CurrencyTransactionType.Withdraw)
                {
                    await txSnapshotService.DeleteSnapshotAsync(
                        transaction.PortfolioId,
                        linked.Id,
                        cancellationToken);
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

        // 計算新金額（依市場規則：台股 flooring）
        decimal newAmount;
        CurrencyTransactionType newType;
        string newNotes;

        if (request.TransactionType == TransactionType.Buy)
        {
            var subtotal = request.Shares * request.PricePerShare;
            if (resolvedMarket == StockMarket.TW)
                subtotal = Math.Floor(subtotal);

            newAmount = subtotal + request.Fees;
            newType = CurrencyTransactionType.Spend;
            newNotes = $"買入 {request.Ticker} × {request.Shares}";
        }
        else
        {
            var subtotal = request.Shares * request.PricePerShare;
            if (resolvedMarket == StockMarket.TW)
                subtotal = Math.Floor(subtotal);

            newAmount = subtotal - request.Fees;
            newType = CurrencyTransactionType.OtherIncome;
            newNotes = $"賣出 {request.Ticker} × {request.Shares}";
        }

        // Buy/Sell：確保 linked spend/income 存在並同步
        if (newType == CurrencyTransactionType.OtherIncome && newAmount <= 0)
        {
            // Sell 淨收入為 0 或負時刪除連動交易
            if (linkedCurrencyTransaction != null)
            {
                await currencyTransactionRepository.SoftDeleteAsync(linkedCurrencyTransaction.Id, cancellationToken);
                linkedCurrencyTransaction = null;
            }
        }
        else
        {
            if (linkedCurrencyTransaction == null)
            {
                decimal? linkedHomeAmount = null;
                decimal? linkedExchangeRate = null;
                if (linkedLedger.CurrencyCode == linkedLedger.HomeCurrency)
                {
                    linkedExchangeRate = 1.0m;
                    linkedHomeAmount = newAmount;
                }

                linkedCurrencyTransaction = new CurrencyTransaction(
                    linkedLedger.Id,
                    request.TransactionDate,
                    newType,
                    newAmount,
                    homeAmount: linkedHomeAmount,
                    exchangeRate: linkedExchangeRate,
                    relatedStockTransactionId: transaction.Id,
                    notes: newNotes);

                await currencyTransactionRepository.AddAsync(linkedCurrencyTransaction, cancellationToken);
            }
            else
            {
                linkedCurrencyTransaction.SetTransactionDate(request.TransactionDate);

                if (linkedLedger.CurrencyCode == linkedLedger.HomeCurrency)
                {
                    linkedCurrencyTransaction.SetAmounts(newType, newAmount, homeAmount: newAmount, exchangeRate: 1.0m);
                }
                else
                {
                    linkedCurrencyTransaction.SetAmounts(
                        newType,
                        newAmount,
                        linkedCurrencyTransaction.HomeAmount,
                        linkedCurrencyTransaction.ExchangeRate);
                }

                linkedCurrencyTransaction.SetNotes(newNotes);
                await currencyTransactionRepository.UpdateAsync(linkedCurrencyTransaction, cancellationToken);
            }
        }

        // Update 流程不處理自動補足；若存在舊的 top-up 連動交易，直接刪除
        if (linkedTopUp != null)
        {
            await currencyTransactionRepository.SoftDeleteAsync(linkedTopUp.Id, cancellationToken);

            if (linkedTopUp.TransactionType is CurrencyTransactionType.InitialBalance
                or CurrencyTransactionType.Deposit
                or CurrencyTransactionType.Withdraw)
            {
                await txSnapshotService.DeleteSnapshotAsync(
                    transaction.PortfolioId,
                    linkedTopUp.Id,
                    cancellationToken);
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
