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
    ICurrentUserService currentUserService)
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
    }
}
