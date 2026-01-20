using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyTransactions;

/// <summary>
/// 軟刪除外幣交易（Currency Transaction）的 Use Case。
/// </summary>
public class DeleteCurrencyTransactionUseCase(
    ICurrencyTransactionRepository transactionRepository,
    ICurrencyLedgerRepository ledgerRepository,
    ICurrentUserService currentUserService)
{
    public async Task ExecuteAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyTransaction", transactionId);

        // Verify ledger belongs to current user
        var ledger = await ledgerRepository.GetByIdAsync(transaction.CurrencyLedgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", transaction.CurrencyLedgerId);

        if (ledger.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        // Prevent deleting transactions linked to stock purchases
        if (transaction.RelatedStockTransactionId.HasValue)
            throw new BusinessRuleException("Cannot delete transactions linked to stock purchases. Delete the stock transaction instead.");

        await transactionRepository.SoftDeleteAsync(transactionId, cancellationToken);
    }
}
