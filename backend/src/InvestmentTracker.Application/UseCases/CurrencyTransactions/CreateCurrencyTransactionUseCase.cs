using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.CurrencyTransactions;

/// <summary>
/// 建立外幣交易（Currency Transaction）的 Use Case。
/// </summary>
public class CreateCurrencyTransactionUseCase
{
    private readonly ICurrencyTransactionRepository _transactionRepository;
    private readonly ICurrencyLedgerRepository _ledgerRepository;
    private readonly CurrencyLedgerService _currencyLedgerService;
    private readonly ICurrentUserService _currentUserService;

    public CreateCurrencyTransactionUseCase(
        ICurrencyTransactionRepository transactionRepository,
        ICurrencyLedgerRepository ledgerRepository,
        CurrencyLedgerService currencyLedgerService,
        ICurrentUserService currentUserService)
    {
        _transactionRepository = transactionRepository;
        _ledgerRepository = ledgerRepository;
        _currencyLedgerService = currencyLedgerService;
        _currentUserService = currentUserService;
    }

    public async Task<CurrencyTransactionDto> ExecuteAsync(
        CreateCurrencyTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Verify ledger exists and belongs to current user
        var ledger = await _ledgerRepository.GetByIdWithTransactionsAsync(
            request.CurrencyLedgerId, cancellationToken)
            ?? throw new InvalidOperationException($"Currency ledger {request.CurrencyLedgerId} not found");

        if (ledger.UserId != _currentUserService.UserId)
            throw new UnauthorizedAccessException("You do not have access to this currency ledger");

        // 備註：Spend 與 ExchangeSell 可能導致帳本餘額為負
        // IB 等券商支援融資/槓桿交易

        var transaction = new CurrencyTransaction(
            request.CurrencyLedgerId,
            request.TransactionDate,
            request.TransactionType,
            request.ForeignAmount,
            request.HomeAmount,
            request.ExchangeRate,
            request.RelatedStockTransactionId,
            request.Notes);

        await _transactionRepository.AddAsync(transaction, cancellationToken);

        return MapToDto(transaction);
    }

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
