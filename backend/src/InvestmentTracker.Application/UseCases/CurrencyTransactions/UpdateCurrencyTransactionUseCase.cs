using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyTransactions;

/// <summary>
/// 更新外幣交易（Currency Transaction）的 Use Case。
/// </summary>
public class UpdateCurrencyTransactionUseCase(
    ICurrencyTransactionRepository transactionRepository,
    ICurrencyLedgerRepository ledgerRepository,
    IPortfolioRepository portfolioRepository,
    ITransactionPortfolioSnapshotService txSnapshotService,
    ICurrentUserService currentUserService)
{
    public async Task<CurrencyTransactionDto> ExecuteAsync(
        Guid transactionId,
        UpdateCurrencyTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyTransaction", transactionId);

        // Verify ledger belongs to current user
        var ledger = await ledgerRepository.GetByIdAsync(transaction.CurrencyLedgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", transaction.CurrencyLedgerId);

        if (ledger.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        // Prevent editing transactions linked to stock purchases
        if (transaction.RelatedStockTransactionId.HasValue)
            throw new BusinessRuleException("Cannot edit transactions linked to stock purchases. Edit the stock transaction instead.");

        var wasExternalCashFlow = IsExternalCashFlowType(transaction.TransactionType);

        var homeAmount = request.HomeAmount;
        var exchangeRate = request.ExchangeRate;

        if (ledger.CurrencyCode == ledger.HomeCurrency)
        {
            exchangeRate = 1.0m;
            homeAmount = request.ForeignAmount;
        }

        transaction.SetTransactionDate(request.TransactionDate);
        transaction.SetAmounts(
            request.TransactionType,
            request.ForeignAmount,
            homeAmount,
            exchangeRate);
        transaction.SetNotes(request.Notes);

        await transactionRepository.UpdateAsync(transaction, cancellationToken);

        var isExternalCashFlow = IsExternalCashFlowType(transaction.TransactionType);
        if (wasExternalCashFlow || isExternalCashFlow)
        {
            var userId = currentUserService.UserId
                ?? throw new AccessDeniedException("User not authenticated");

            var boundPortfolios = (await portfolioRepository.GetByUserIdAsync(userId, cancellationToken))
                .Where(p => p.BoundCurrencyLedgerId == transaction.CurrencyLedgerId)
                .ToList();

            foreach (var portfolio in boundPortfolios)
            {
                if (isExternalCashFlow)
                {
                    await txSnapshotService.UpsertSnapshotAsync(
                        portfolio.Id,
                        transaction.Id,
                        transaction.TransactionDate,
                        cancellationToken);
                }
                else
                {
                    await txSnapshotService.DeleteSnapshotAsync(
                        portfolio.Id,
                        transaction.Id,
                        cancellationToken);
                }
            }
        }

        return MapToDto(transaction);
    }

    private static bool IsExternalCashFlowType(CurrencyTransactionType transactionType)
        => transactionType is CurrencyTransactionType.InitialBalance
            or CurrencyTransactionType.Deposit
            or CurrencyTransactionType.Withdraw;

    private static CurrencyTransactionDto MapToDto(CurrencyTransaction transaction)
    {
        return new CurrencyTransactionDto
        {
            Id = transaction.Id,
            CurrencyLedgerId = transaction.CurrencyLedgerId,
            TransactionDate = transaction.TransactionDate,
            TransactionType = transaction.TransactionType,
            ForeignAmount = transaction.ForeignAmount,
            HomeAmount = transaction.HomeAmount,
            ExchangeRate = transaction.ExchangeRate,
            RelatedStockTransactionId = transaction.RelatedStockTransactionId,
            Notes = transaction.Notes,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt
        };
    }
}
