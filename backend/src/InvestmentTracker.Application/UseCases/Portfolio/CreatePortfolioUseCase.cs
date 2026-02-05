using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// 建立投資組合的 Use Case。
/// </summary>
public class CreatePortfolioUseCase(
    IPortfolioRepository portfolioRepository,
    ICurrencyLedgerRepository currencyLedgerRepository,
    ICurrencyTransactionRepository currencyTransactionRepository,
    ICurrentUserService currentUserService,
    IAppDbTransactionManager transactionManager,
    ILogger<CreatePortfolioUseCase> logger)
{
    public async Task<PortfolioDto> ExecuteAsync(
        CreatePortfolioRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var currencyCode = request.CurrencyCode.Trim().ToUpperInvariant();

        await using var transaction = await transactionManager.BeginTransactionAsync(cancellationToken);

        try
        {
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

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Created portfolio {PortfolioId} for user {UserId} with ledger {LedgerId} and base currency {BaseCurrency}",
                portfolio.Id,
                userId,
                ledger.Id,
                currencyCode);

            return portfolio.ToDto();
        }
        catch (Exception ex)
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (Exception rollbackEx)
            {
                logger.LogError(
                    rollbackEx,
                    "Failed to rollback CreatePortfolio transaction for user {UserId} currency {BaseCurrency}",
                    userId,
                    currencyCode);
            }

            if (ex is BusinessRuleException or AccessDeniedException)
            {
                logger.LogWarning(
                    "CreatePortfolio failed for user {UserId} currency {BaseCurrency}: {Message}",
                    userId,
                    currencyCode,
                    ex.Message);
            }
            else
            {
                logger.LogError(
                    ex,
                    "CreatePortfolio failed for user {UserId} currency {BaseCurrency}",
                    userId,
                    currencyCode);
            }

            throw;
        }
    }
}
