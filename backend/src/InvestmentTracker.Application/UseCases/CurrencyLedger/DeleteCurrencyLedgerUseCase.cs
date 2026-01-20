using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyLedger;

/// <summary>
/// 刪除（停用）外幣帳本（Currency Ledger）的 Use Case。
/// </summary>
public class DeleteCurrencyLedgerUseCase(
    ICurrencyLedgerRepository ledgerRepository,
    ICurrentUserService currentUserService)
{
    public async Task ExecuteAsync(
        Guid ledgerId,
        CancellationToken cancellationToken = default)
    {
        var ledger = await ledgerRepository.GetByIdAsync(ledgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", ledgerId);

        if (ledger.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        await ledgerRepository.DeleteAsync(ledgerId, cancellationToken);
    }
}
