using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyTransactions;

/// <summary>
/// Use case for soft-deleting a currency transaction.
/// </summary>
public class DeleteCurrencyTransactionUseCase
{
    private readonly ICurrencyTransactionRepository _transactionRepository;
    private readonly ICurrencyLedgerRepository _ledgerRepository;
    private readonly ICurrentUserService _currentUserService;

    public DeleteCurrencyTransactionUseCase(
        ICurrencyTransactionRepository transactionRepository,
        ICurrencyLedgerRepository ledgerRepository,
        ICurrentUserService currentUserService)
    {
        _transactionRepository = transactionRepository;
        _ledgerRepository = ledgerRepository;
        _currentUserService = currentUserService;
    }

    public async Task<bool> ExecuteAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(transactionId, cancellationToken);
        if (transaction == null)
            return false;

        // Verify ledger belongs to current user
        var ledger = await _ledgerRepository.GetByIdAsync(transaction.CurrencyLedgerId, cancellationToken);
        if (ledger == null || ledger.UserId != _currentUserService.UserId)
            throw new UnauthorizedAccessException("You do not have access to this transaction");

        // Prevent deleting transactions linked to stock purchases
        if (transaction.RelatedStockTransactionId.HasValue)
            throw new InvalidOperationException("Cannot delete transactions linked to stock purchases. Delete the stock transaction instead.");

        await _transactionRepository.SoftDeleteAsync(transactionId, cancellationToken);

        return true;
    }
}
