using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyLedger;

/// <summary>
/// 更新外幣帳本（Currency Ledger）的 Use Case。
/// </summary>
public class UpdateCurrencyLedgerUseCase
{
    private readonly ICurrencyLedgerRepository _ledgerRepository;
    private readonly ICurrentUserService _currentUserService;

    public UpdateCurrencyLedgerUseCase(
        ICurrencyLedgerRepository ledgerRepository,
        ICurrentUserService currentUserService)
    {
        _ledgerRepository = ledgerRepository;
        _currentUserService = currentUserService;
    }

    public async Task<CurrencyLedgerDto?> ExecuteAsync(
        Guid ledgerId,
        UpdateCurrencyLedgerRequest request,
        CancellationToken cancellationToken = default)
    {
        var ledger = await _ledgerRepository.GetByIdAsync(ledgerId, cancellationToken);
        if (ledger == null)
            return null;

        if (ledger.UserId != _currentUserService.UserId)
            throw new UnauthorizedAccessException("You do not have access to this currency ledger");

        ledger.SetName(request.Name);

        await _ledgerRepository.UpdateAsync(ledger, cancellationToken);

        return new CurrencyLedgerDto
        {
            Id = ledger.Id,
            CurrencyCode = ledger.CurrencyCode,
            Name = ledger.Name,
            HomeCurrency = ledger.HomeCurrency,
            IsActive = ledger.IsActive,
            CreatedAt = ledger.CreatedAt,
            UpdatedAt = ledger.UpdatedAt
        };
    }
}
