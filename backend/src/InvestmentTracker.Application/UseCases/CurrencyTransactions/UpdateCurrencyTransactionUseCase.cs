using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyTransactions;

/// <summary>
/// Use case for updating an existing currency transaction.
/// </summary>
public class UpdateCurrencyTransactionUseCase
{
    private readonly ICurrencyTransactionRepository _transactionRepository;
    private readonly ICurrencyLedgerRepository _ledgerRepository;
    private readonly ICurrentUserService _currentUserService;

    public UpdateCurrencyTransactionUseCase(
        ICurrencyTransactionRepository transactionRepository,
        ICurrencyLedgerRepository ledgerRepository,
        ICurrentUserService currentUserService)
    {
        _transactionRepository = transactionRepository;
        _ledgerRepository = ledgerRepository;
        _currentUserService = currentUserService;
    }

    public async Task<CurrencyTransactionDto?> ExecuteAsync(
        Guid transactionId,
        UpdateCurrencyTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(transactionId, cancellationToken);
        if (transaction == null)
            return null;

        // Verify ledger belongs to current user
        var ledger = await _ledgerRepository.GetByIdAsync(transaction.CurrencyLedgerId, cancellationToken);
        if (ledger == null || ledger.UserId != _currentUserService.UserId)
            throw new UnauthorizedAccessException("You do not have access to this transaction");

        // Prevent editing transactions linked to stock purchases
        if (transaction.RelatedStockTransactionId.HasValue)
            throw new InvalidOperationException("Cannot edit transactions linked to stock purchases. Edit the stock transaction instead.");

        transaction.SetTransactionDate(request.TransactionDate);
        transaction.SetAmounts(
            request.TransactionType,
            request.ForeignAmount,
            request.HomeAmount,
            request.ExchangeRate);
        transaction.SetNotes(request.Notes);

        await _transactionRepository.UpdateAsync(transaction, cancellationToken);

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
