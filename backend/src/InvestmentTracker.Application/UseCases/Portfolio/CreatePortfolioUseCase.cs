using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// 建立投資組合的 Use Case。
/// </summary>
public class CreatePortfolioUseCase(
    IPortfolioRepository portfolioRepository,
    ICurrencyLedgerRepository currencyLedgerRepository,
    ICurrencyTransactionRepository currencyTransactionRepository,
    ICurrentUserService currentUserService)
{
    public async Task<PortfolioDto> ExecuteAsync(
        CreatePortfolioRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var currencyCode = request.CurrencyCode.Trim().ToUpperInvariant();

        var existingPortfolios = await portfolioRepository.GetByUserIdAsync(userId, cancellationToken);
        if (existingPortfolios.Any(p => string.Equals(p.BaseCurrency, currencyCode, StringComparison.OrdinalIgnoreCase)))
            throw new BusinessRuleException($"A portfolio for {currencyCode} already exists");

        if (await currencyLedgerRepository.ExistsByCurrencyCodeAsync(userId, currencyCode, cancellationToken))
            throw new BusinessRuleException($"A currency ledger for {currencyCode} already exists");

        var ledgerName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? $"{currencyCode} Ledger"
            : request.DisplayName.Trim();

        var ledger = new Domain.Entities.CurrencyLedger(
            userId,
            currencyCode,
            ledgerName,
            request.HomeCurrency);

        await currencyLedgerRepository.AddAsync(ledger, cancellationToken);

        if (request.InitialBalance is > 0)
        {
            decimal? homeAmount = null;
            decimal? exchangeRate = null;

            if (string.Equals(ledger.CurrencyCode, ledger.HomeCurrency, StringComparison.OrdinalIgnoreCase))
            {
                exchangeRate = 1.0m;
                homeAmount = request.InitialBalance.Value;
            }

            var deposit = new CurrencyTransaction(
                ledger.Id,
                DateTime.UtcNow,
                CurrencyTransactionType.Deposit,
                request.InitialBalance.Value,
                homeAmount: homeAmount,
                exchangeRate: exchangeRate,
                notes: "Initial balance");

            await currencyTransactionRepository.AddAsync(deposit, cancellationToken);
        }

        var portfolio = new Domain.Entities.Portfolio(
            userId,
            ledger.Id,
            currencyCode,
            request.HomeCurrency,
            request.DisplayName);

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            portfolio.SetDescription(request.Description);
        }

        await portfolioRepository.AddAsync(portfolio, cancellationToken);

        return portfolio.ToDto();
    }
}
