using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyTransactions;

/// <summary>
/// 軟刪除外幣交易（Currency Transaction）的 Use Case。
/// </summary>
public class DeleteCurrencyTransactionUseCase(
    ICurrencyTransactionRepository transactionRepository,
    ICurrencyLedgerRepository ledgerRepository,
    IPortfolioRepository portfolioRepository,
    ITransactionPortfolioSnapshotService txSnapshotService,
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

        var wasExternalCashFlow = transaction.TransactionType is CurrencyTransactionType.InitialBalance
            or CurrencyTransactionType.Deposit
            or CurrencyTransactionType.Withdraw;

        await transactionRepository.SoftDeleteAsync(transactionId, cancellationToken);

        if (wasExternalCashFlow)
        {
            var userId = currentUserService.UserId
                ?? throw new AccessDeniedException("User not authenticated");

            var boundPortfolios = (await portfolioRepository.GetByUserIdAsync(userId, cancellationToken))
                .Where(p => p.BoundCurrencyLedgerId == transaction.CurrencyLedgerId)
                .ToList();

            foreach (var portfolio in boundPortfolios)
            {
                await txSnapshotService.DeleteSnapshotAsync(
                    portfolio.Id,
                    transaction.Id,
                    cancellationToken);
            }
        }
    }
}
