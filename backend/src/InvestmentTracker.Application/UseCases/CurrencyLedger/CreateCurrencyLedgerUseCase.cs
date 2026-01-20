using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyLedger;

/// <summary>
/// 建立外幣帳本（Currency Ledger）的 Use Case。
/// </summary>
public class CreateCurrencyLedgerUseCase(
    ICurrencyLedgerRepository ledgerRepository,
    ICurrentUserService currentUserService)
{
    public async Task<CurrencyLedgerDto> ExecuteAsync(
        CreateCurrencyLedgerRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        // Check if ledger with same currency already exists
        if (await ledgerRepository.ExistsByCurrencyCodeAsync(userId, request.CurrencyCode, cancellationToken))
            throw new BusinessRuleException($"A currency ledger for {request.CurrencyCode} already exists");

        var ledger = new Domain.Entities.CurrencyLedger(
            userId,
            request.CurrencyCode,
            request.Name,
            request.HomeCurrency);

        await ledgerRepository.AddAsync(ledger, cancellationToken);

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
