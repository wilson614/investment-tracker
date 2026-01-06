using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyLedger;

/// <summary>
/// Use case for deleting (deactivating) a currency ledger.
/// </summary>
public class DeleteCurrencyLedgerUseCase
{
    private readonly ICurrencyLedgerRepository _ledgerRepository;
    private readonly ICurrentUserService _currentUserService;

    public DeleteCurrencyLedgerUseCase(
        ICurrencyLedgerRepository ledgerRepository,
        ICurrentUserService currentUserService)
    {
        _ledgerRepository = ledgerRepository;
        _currentUserService = currentUserService;
    }

    public async Task<bool> ExecuteAsync(
        Guid ledgerId,
        CancellationToken cancellationToken = default)
    {
        var ledger = await _ledgerRepository.GetByIdAsync(ledgerId, cancellationToken);
        if (ledger == null)
            return false;

        if (ledger.UserId != _currentUserService.UserId)
            throw new UnauthorizedAccessException("You do not have access to this currency ledger");

        await _ledgerRepository.DeleteAsync(ledgerId, cancellationToken);

        return true;
    }
}
