using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CurrencyTransactions;

/// <summary>
/// 建立外幣交易（Currency Transaction）的 Use Case。
/// </summary>
public class CreateCurrencyTransactionUseCase(
    ICurrencyTransactionRepository transactionRepository,
    ICurrencyLedgerRepository ledgerRepository,
    IPortfolioRepository portfolioRepository,
    ITransactionPortfolioSnapshotService txSnapshotService,
    ICurrentUserService currentUserService,
    IAppDbTransactionManager transactionManager)
{
    public async Task<CurrencyTransactionDto> ExecuteAsync(
        CreateCurrencyTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Verify ledger exists and belongs to current user
        // NOTE:
        // - Create flow only needs ledger metadata (ownership / currency settings)
        // - Avoid pre-loading transactions here to prevent stale tracked navigation collections
        //   when other services query the same aggregate later in the same request scope.
        var ledger = await ledgerRepository.GetByIdAsync(
            request.CurrencyLedgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", request.CurrencyLedgerId);

        if (ledger.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        if (request.RelatedStockTransactionId.HasValue)
            throw new BusinessRuleException("RelatedStockTransactionId cannot be provided when creating currency transactions.");

        CurrencyTransactionTypePolicy.EnsureValidOrThrow(
            ledger.CurrencyCode,
            request.TransactionType,
            new CurrencyTransactionAmountPresence(
                HasAmount: request.ForeignAmount != default,
                HasTargetAmount: request.HomeAmount.HasValue));

        // 備註：Spend 與 ExchangeSell 可能導致帳本餘額為負
        // IB 等券商支援融資/槓桿交易

        var homeAmount = request.HomeAmount;
        var exchangeRate = request.ExchangeRate;

        if (ledger.CurrencyCode == ledger.HomeCurrency)
        {
            exchangeRate = 1.0m;
            homeAmount = request.ForeignAmount;
        }

        var transaction = new CurrencyTransaction(
            request.CurrencyLedgerId,
            request.TransactionDate,
            request.TransactionType,
            request.ForeignAmount,
            homeAmount,
            exchangeRate,
            request.RelatedStockTransactionId,
            request.Notes);

        await using var tx = await transactionManager.BeginTransactionAsync(cancellationToken);

        try
        {
            await transactionRepository.AddAsync(transaction, cancellationToken);

            if (IsExternalCashFlowType(transaction.TransactionType))
            {
                var userId = currentUserService.UserId
                    ?? throw new AccessDeniedException("User not authenticated");

                var boundPortfolios = (await portfolioRepository.GetByUserIdAsync(userId, cancellationToken))
                    .Where(p => p.BoundCurrencyLedgerId == transaction.CurrencyLedgerId)
                    .ToList();

                foreach (var portfolio in boundPortfolios)
                {
                    await txSnapshotService.UpsertSnapshotAsync(
                        portfolio.Id,
                        transaction.Id,
                        transaction.TransactionDate,
                        cancellationToken);
                }
            }

            await tx.CommitAsync(cancellationToken);
            return MapToDto(transaction);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
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
