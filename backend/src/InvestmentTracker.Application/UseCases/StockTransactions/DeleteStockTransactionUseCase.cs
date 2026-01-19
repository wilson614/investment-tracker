using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

/// <summary>
/// 軟刪除股票交易的 Use Case。
/// </summary>
public class DeleteStockTransactionUseCase
{
    private readonly IStockTransactionRepository _transactionRepository;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly ICurrencyTransactionRepository _currencyTransactionRepository;
    private readonly ICurrentUserService _currentUserService;

    public DeleteStockTransactionUseCase(
        IStockTransactionRepository transactionRepository,
        IPortfolioRepository portfolioRepository,
        ICurrencyTransactionRepository currencyTransactionRepository,
        ICurrentUserService currentUserService)
    {
        _transactionRepository = transactionRepository;
        _portfolioRepository = portfolioRepository;
        _currencyTransactionRepository = currencyTransactionRepository;
        _currentUserService = currentUserService;
    }

    public async Task ExecuteAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(transactionId, cancellationToken)
            ?? throw new InvalidOperationException($"Transaction {transactionId} not found");

        // 透過投資組合確認存取權限
        var portfolio = await _portfolioRepository.GetByIdAsync(transaction.PortfolioId, cancellationToken)
            ?? throw new InvalidOperationException($"Portfolio {transaction.PortfolioId} not found");

        if (portfolio.UserId != _currentUserService.UserId)
        {
            throw new UnauthorizedAccessException("You do not have access to this transaction");
        }

        // 找出並刪除連動的外幣交易（若存在）
        var linkedCurrencyTransaction = await _currencyTransactionRepository.GetByStockTransactionIdAsync(
            transactionId, cancellationToken);
        if (linkedCurrencyTransaction != null)
        {
            await _currencyTransactionRepository.SoftDeleteAsync(linkedCurrencyTransaction.Id, cancellationToken);
        }

        // 軟刪除股票交易
        await _transactionRepository.SoftDeleteAsync(transactionId, cancellationToken);
    }
}
