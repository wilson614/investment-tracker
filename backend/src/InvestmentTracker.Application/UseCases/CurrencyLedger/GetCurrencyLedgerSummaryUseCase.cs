using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.CurrencyLedger;

/// <summary>
/// 取得外幣帳本摘要（包含餘額、平均匯率、損益等計算欄位）的 Use Case。
/// </summary>
public class GetCurrencyLedgerSummaryUseCase(
    ICurrencyLedgerRepository ledgerRepository,
    CurrencyLedgerService currencyLedgerService,
    ICurrentUserService currentUserService)
{
    public async Task<CurrencyLedgerSummaryDto> ExecuteAsync(
        Guid ledgerId,
        CancellationToken cancellationToken = default)
    {
        var ledger = await ledgerRepository.GetByIdWithTransactionsAsync(ledgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", ledgerId);

        if (ledger.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        var transactions = ledger.Transactions.ToList();

        var balance = currencyLedgerService.CalculateBalance(transactions);
        var avgExchangeRate = currencyLedgerService.CalculateAverageExchangeRate(transactions);
        var totalExchanged = currencyLedgerService.CalculateTotalExchanged(transactions);
        var totalSpentOnStocks = currencyLedgerService.CalculateTotalSpentOnStocks(transactions);
        var totalInterest = currencyLedgerService.CalculateTotalInterest(transactions);
        var totalCost = currencyLedgerService.CalculateTotalCost(transactions);
        var realizedPnl = currencyLedgerService.CalculateRealizedPnl(transactions);

        var recentTransactions = transactions
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
            .Take(10)
            .Select(MapTransactionToDto)
            .ToList();

        return new CurrencyLedgerSummaryDto
        {
            Ledger = MapLedgerToDto(ledger),
            Balance = balance,
            AverageExchangeRate = avgExchangeRate,
            TotalExchanged = totalExchanged,
            TotalSpentOnStocks = totalSpentOnStocks,
            TotalInterest = totalInterest,
            TotalCost = totalCost,
            RealizedPnl = realizedPnl,
            CurrentExchangeRate = null, // 由前端獨立呼叫匯率 API 以避免延遲
            CurrentValueHome = null,
            UnrealizedPnlHome = null,
            UnrealizedPnlPercentage = null,
            RecentTransactions = recentTransactions
        };
    }

    public async Task<IReadOnlyList<CurrencyLedgerSummaryDto>> GetAllForUserAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");
        var ledgers = await ledgerRepository.GetByUserIdAsync(userId, cancellationToken);

        var results = new List<CurrencyLedgerSummaryDto>();

        foreach (var ledger in ledgers)
        {
            var fullLedger = await ledgerRepository.GetByIdWithTransactionsAsync(ledger.Id, cancellationToken);
            if (fullLedger == null)
                continue;

            var transactions = fullLedger.Transactions.ToList();
            var balance = currencyLedgerService.CalculateBalance(transactions);
            var avgExchangeRate = currencyLedgerService.CalculateAverageExchangeRate(transactions);
            var totalExchanged = currencyLedgerService.CalculateTotalExchanged(transactions);
            var totalSpentOnStocks = currencyLedgerService.CalculateTotalSpentOnStocks(transactions);
            var totalInterest = currencyLedgerService.CalculateTotalInterest(transactions);
            var totalCost = currencyLedgerService.CalculateTotalCost(transactions);
            var realizedPnl = currencyLedgerService.CalculateRealizedPnl(transactions);

            results.Add(new CurrencyLedgerSummaryDto
            {
                Ledger = MapLedgerToDto(fullLedger),
                Balance = balance,
                AverageExchangeRate = avgExchangeRate,
                TotalExchanged = totalExchanged,
                TotalSpentOnStocks = totalSpentOnStocks,
                TotalInterest = totalInterest,
                TotalCost = totalCost,
                RealizedPnl = realizedPnl,
                CurrentExchangeRate = null,
                CurrentValueHome = null,
                UnrealizedPnlHome = null,
                UnrealizedPnlPercentage = null,
                RecentTransactions = []
            });
        }

        return results;
    }

    private static CurrencyLedgerDto MapLedgerToDto(Domain.Entities.CurrencyLedger ledger)
    {
        return new CurrencyLedgerDto
        {
            Id = ledger.Id,
            CurrencyCode = ledger.CurrencyCode,
            Name = ledger.Name,
            HomeCurrency = ledger.HomeCurrency,
            IsActive = ledger.IsActive,
            CreatedAt = ledger.CreatedAt,
            UpdatedAt = ledger.UpdatedAt
        };
    }

    private static CurrencyTransactionDto MapTransactionToDto(CurrencyTransaction transaction)
    {
        return new CurrencyTransactionDto
        {
            Id = transaction.Id,
            CurrencyLedgerId = transaction.CurrencyLedgerId,
            TransactionDate = transaction.TransactionDate,
            TransactionType = transaction.TransactionType,
            ForeignAmount = transaction.ForeignAmount,
            HomeAmount = transaction.HomeAmount,
            ExchangeRate = transaction.ExchangeRate,
            RelatedStockTransactionId = transaction.RelatedStockTransactionId,
            Notes = transaction.Notes,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt
        };
    }
}
