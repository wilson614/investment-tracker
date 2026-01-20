using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyLedger;

/// <summary>
/// 刪除（停用）外幣帳本（Currency Ledger）的 Use Case。
/// </summary>
public class DeleteCurrencyLedgerUseCase(
    ICurrencyLedgerRepository ledgerRepository,
    ICurrentUserService currentUserService)
{
    public async Task<bool> ExecuteAsync(
        Guid ledgerId,
        CancellationToken cancellationToken = default)
    {
        var ledger = await ledgerRepository.GetByIdAsync(ledgerId, cancellationToken);
        if (ledger == null)
            return false;

        if (ledger.UserId != currentUserService.UserId)
            throw new UnauthorizedAccessException("You do not have access to this currency ledger");

        await ledgerRepository.DeleteAsync(ledgerId, cancellationToken);

        return true;
    }
}
