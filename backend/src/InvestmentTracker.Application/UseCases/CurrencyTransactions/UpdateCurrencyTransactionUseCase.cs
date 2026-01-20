using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyTransactions;

/// <summary>
/// 更新外幣交易（Currency Transaction）的 Use Case。
/// </summary>
public class UpdateCurrencyTransactionUseCase(
    ICurrencyTransactionRepository transactionRepository,
    ICurrencyLedgerRepository ledgerRepository,
    ICurrentUserService currentUserService)
{
    public async Task<CurrencyTransactionDto> ExecuteAsync(
        Guid transactionId,
        UpdateCurrencyTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyTransaction", transactionId);

        // Verify ledger belongs to current user
        var ledger = await ledgerRepository.GetByIdAsync(transaction.CurrencyLedgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", transaction.CurrencyLedgerId);

        if (ledger.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        // Prevent editing transactions linked to stock purchases
        if (transaction.RelatedStockTransactionId.HasValue)
            throw new BusinessRuleException("Cannot edit transactions linked to stock purchases. Edit the stock transaction instead.");

        transaction.SetTransactionDate(request.TransactionDate);
        transaction.SetAmounts(
            request.TransactionType,
            request.ForeignAmount,
            request.HomeAmount,
            request.ExchangeRate);
        transaction.SetNotes(request.Notes);

        await transactionRepository.UpdateAsync(transaction, cancellationToken);

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
