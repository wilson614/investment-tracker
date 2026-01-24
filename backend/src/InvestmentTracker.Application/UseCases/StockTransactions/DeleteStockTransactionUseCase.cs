using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

/// <summary>
/// 軟刪除股票交易的 Use Case。
/// </summary>
public class DeleteStockTransactionUseCase(
    IStockTransactionRepository transactionRepository,
    IPortfolioRepository portfolioRepository,
    ICurrencyTransactionRepository currencyTransactionRepository,
    ICurrentUserService currentUserService,
    IMonthlySnapshotService monthlySnapshotService,
    ITransactionPortfolioSnapshotService txSnapshotService)
{
    public async Task ExecuteAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId, cancellationToken)
            ?? throw new EntityNotFoundException("Transaction", transactionId);

        // 透過投資組合確認存取權限
        var portfolio = await portfolioRepository.GetByIdAsync(transaction.PortfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", transaction.PortfolioId);

        if (portfolio.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        // 找出並刪除連動的外幣交易（若存在）
        var linkedCurrencyTransaction = await currencyTransactionRepository.GetByStockTransactionIdAsync(
            transactionId, cancellationToken);
        if (linkedCurrencyTransaction != null)
        {
            await currencyTransactionRepository.SoftDeleteAsync(linkedCurrencyTransaction.Id, cancellationToken);
        }

        // 軟刪除股票交易
        await transactionRepository.SoftDeleteAsync(transactionId, cancellationToken);

        // 交易異動後：使月度快取失效（從影響月份起）
        var affectedFromMonth = new DateOnly(transaction.TransactionDate.Year, transaction.TransactionDate.Month, 1);
        await monthlySnapshotService.InvalidateFromMonthAsync(
            transaction.PortfolioId, affectedFromMonth, cancellationToken);

        // US1: 刪除交易日快照
        await txSnapshotService.DeleteSnapshotAsync(
            transaction.PortfolioId,
            transaction.Id,
            cancellationToken);
    }
}
