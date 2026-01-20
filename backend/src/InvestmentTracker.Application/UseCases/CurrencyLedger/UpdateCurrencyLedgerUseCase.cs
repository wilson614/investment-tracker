using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyLedger;

/// <summary>
/// 更新外幣帳本（Currency Ledger）的 Use Case。
/// </summary>
public class UpdateCurrencyLedgerUseCase(
    ICurrencyLedgerRepository ledgerRepository,
    ICurrentUserService currentUserService)
{
    public async Task<CurrencyLedgerDto> ExecuteAsync(
        Guid ledgerId,
        UpdateCurrencyLedgerRequest request,
        CancellationToken cancellationToken = default)
    {
        var ledger = await ledgerRepository.GetByIdAsync(ledgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", ledgerId);

        if (ledger.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        ledger.SetName(request.Name);

        await ledgerRepository.UpdateAsync(ledger, cancellationToken);

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
