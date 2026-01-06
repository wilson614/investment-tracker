using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.CurrencyTransactions;

/// <summary>
/// Use case for creating a new currency transaction.
/// </summary>
public class CreateCurrencyTransactionUseCase
{
    private readonly ICurrencyTransactionRepository _transactionRepository;
    private readonly ICurrencyLedgerRepository _ledgerRepository;
    private readonly CurrencyLedgerService _currencyLedgerService;
    private readonly ICurrentUserService _currentUserService;

    public CreateCurrencyTransactionUseCase(
        ICurrencyTransactionRepository transactionRepository,
        ICurrencyLedgerRepository ledgerRepository,
        CurrencyLedgerService currencyLedgerService,
        ICurrentUserService currentUserService)
    {
        _transactionRepository = transactionRepository;
        _ledgerRepository = ledgerRepository;
        _currencyLedgerService = currencyLedgerService;
        _currentUserService = currentUserService;
    }

    public async Task<CurrencyTransactionDto> ExecuteAsync(
        CreateCurrencyTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Verify ledger exists and belongs to current user
        var ledger = await _ledgerRepository.GetByIdWithTransactionsAsync(
            request.CurrencyLedgerId, cancellationToken)
            ?? throw new InvalidOperationException($"Currency ledger {request.CurrencyLedgerId} not found");

        if (ledger.UserId != _currentUserService.UserId)
            throw new UnauthorizedAccessException("You do not have access to this currency ledger");

        // For Spend transactions, validate sufficient balance
        if (request.TransactionType == Domain.Enums.CurrencyTransactionType.Spend)
        {
            if (!_currencyLedgerService.ValidateSpend(ledger.Transactions, request.ForeignAmount))
            {
                throw new InvalidOperationException("Insufficient balance for this spend transaction");
            }
        }

        // For ExchangeSell transactions, validate sufficient balance
        if (request.TransactionType == Domain.Enums.CurrencyTransactionType.ExchangeSell)
        {
            var currentBalance = _currencyLedgerService.CalculateBalance(ledger.Transactions);
            if (currentBalance < request.ForeignAmount)
            {
                throw new InvalidOperationException("Insufficient balance for this sell transaction");
            }
        }

        var transaction = new CurrencyTransaction(
            request.CurrencyLedgerId,
            request.TransactionDate,
            request.TransactionType,
            request.ForeignAmount,
            request.HomeAmount,
            request.ExchangeRate,
            request.RelatedStockTransactionId,
            request.Notes);

        await _transactionRepository.AddAsync(transaction, cancellationToken);

        return MapToDto(transaction);
    }

    private static CurrencyTransactionDto MapToDto(CurrencyTransaction transaction)
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
