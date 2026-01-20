using InvestmentTracker.Application.Interfaces;
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
    public async Task<bool> ExecuteAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId, cancellationToken);
        if (transaction == null)
            return false;

        // Verify ledger belongs to current user
        var ledger = await ledgerRepository.GetByIdAsync(transaction.CurrencyLedgerId, cancellationToken);
        if (ledger == null || ledger.UserId != currentUserService.UserId)
            throw new UnauthorizedAccessException("You do not have access to this transaction");

        // Prevent deleting transactions linked to stock purchases
        if (transaction.RelatedStockTransactionId.HasValue)
            throw new InvalidOperationException("Cannot delete transactions linked to stock purchases. Delete the stock transaction instead.");

        await transactionRepository.SoftDeleteAsync(transactionId, cancellationToken);

        return true;
    }
}
