using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.CurrencyLedger;

/// <summary>
/// Use case for getting currency ledger summary.
/// </summary>
public class GetCurrencyLedgerSummaryUseCase
{
    private readonly ICurrencyLedgerRepository _ledgerRepository;
    private readonly CurrencyLedgerService _currencyLedgerService;
    private readonly ICurrentUserService _currentUserService;

    public GetCurrencyLedgerSummaryUseCase(
        ICurrencyLedgerRepository ledgerRepository,
        CurrencyLedgerService currencyLedgerService,
        ICurrentUserService currentUserService)
    {
        _ledgerRepository = ledgerRepository;
        _currencyLedgerService = currencyLedgerService;
        _currentUserService = currentUserService;
    }

    public async Task<CurrencyLedgerSummaryDto?> ExecuteAsync(
        Guid ledgerId,
        CancellationToken cancellationToken = default)
    {
        var ledger = await _ledgerRepository.GetByIdWithTransactionsAsync(ledgerId, cancellationToken);
        if (ledger == null)
            return null;

        if (ledger.UserId != _currentUserService.UserId)
            throw new UnauthorizedAccessException("You do not have access to this currency ledger");

        var transactions = ledger.Transactions.ToList();

        var balance = _currencyLedgerService.CalculateBalance(transactions);
        var avgExchangeRate = _currencyLedgerService.CalculateAverageExchangeRate(transactions);
        var totalExchanged = _currencyLedgerService.CalculateTotalExchanged(transactions);
        var totalSpentOnStocks = _currencyLedgerService.CalculateTotalSpentOnStocks(transactions);
        var totalInterest = _currencyLedgerService.CalculateTotalInterest(transactions);
        var totalCost = _currencyLedgerService.CalculateTotalCost(transactions);
        var realizedPnl = _currencyLedgerService.CalculateRealizedPnl(transactions);

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
            CurrentExchangeRate = null, // TODO: Integrate with exchange rate API
            CurrentValueHome = null,
            UnrealizedPnlHome = null,
            UnrealizedPnlPercentage = null,
            RecentTransactions = recentTransactions
        };
    }

    public async Task<IReadOnlyList<CurrencyLedgerSummaryDto>> GetAllForUserAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId
            ?? throw new UnauthorizedAccessException("User not authenticated");
        var ledgers = await _ledgerRepository.GetByUserIdAsync(userId, cancellationToken);

        var results = new List<CurrencyLedgerSummaryDto>();

        foreach (var ledger in ledgers)
        {
            var fullLedger = await _ledgerRepository.GetByIdWithTransactionsAsync(ledger.Id, cancellationToken);
            if (fullLedger != null)
            {
                var transactions = fullLedger.Transactions.ToList();
                var balance = _currencyLedgerService.CalculateBalance(transactions);
                var avgExchangeRate = _currencyLedgerService.CalculateAverageExchangeRate(transactions);
                var totalExchanged = _currencyLedgerService.CalculateTotalExchanged(transactions);
                var totalSpentOnStocks = _currencyLedgerService.CalculateTotalSpentOnStocks(transactions);
                var totalInterest = _currencyLedgerService.CalculateTotalInterest(transactions);
                var totalCost = _currencyLedgerService.CalculateTotalCost(transactions);
                var realizedPnl = _currencyLedgerService.CalculateRealizedPnl(transactions);

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
                    RecentTransactions = Array.Empty<CurrencyTransactionDto>()
                });
            }
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
