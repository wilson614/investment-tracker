using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

/// <summary>
/// Use case for creating a new stock transaction.
/// </summary>
public class CreateStockTransactionUseCase
{
    private readonly IStockTransactionRepository _transactionRepository;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly ICurrencyLedgerRepository _currencyLedgerRepository;
    private readonly ICurrencyTransactionRepository _currencyTransactionRepository;
    private readonly PortfolioCalculator _portfolioCalculator;
    private readonly CurrencyLedgerService _currencyLedgerService;
    private readonly ICurrentUserService _currentUserService;

    public CreateStockTransactionUseCase(
        IStockTransactionRepository transactionRepository,
        IPortfolioRepository portfolioRepository,
        ICurrencyLedgerRepository currencyLedgerRepository,
        ICurrencyTransactionRepository currencyTransactionRepository,
        PortfolioCalculator portfolioCalculator,
        CurrencyLedgerService currencyLedgerService,
        ICurrentUserService currentUserService)
    {
        _transactionRepository = transactionRepository;
        _portfolioRepository = portfolioRepository;
        _currencyLedgerRepository = currencyLedgerRepository;
        _currencyTransactionRepository = currencyTransactionRepository;
        _portfolioCalculator = portfolioCalculator;
        _currencyLedgerService = currencyLedgerService;
        _currentUserService = currentUserService;
    }

    public async Task<StockTransactionDto> ExecuteAsync(
        CreateStockTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Verify portfolio exists and belongs to current user
        var portfolio = await _portfolioRepository.GetByIdAsync(request.PortfolioId, cancellationToken)
            ?? throw new InvalidOperationException($"Portfolio {request.PortfolioId} not found");

        if (portfolio.UserId != _currentUserService.UserId)
        {
            throw new UnauthorizedAccessException("You do not have access to this portfolio");
        }

        // If fund source is CurrencyLedger, validate balance and deduct atomically
        Domain.Entities.CurrencyLedger? currencyLedger = null;
        if (request.FundSource == FundSource.CurrencyLedger && request.CurrencyLedgerId.HasValue)
        {
            currencyLedger = await _currencyLedgerRepository.GetByIdWithTransactionsAsync(
                request.CurrencyLedgerId.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Currency ledger {request.CurrencyLedgerId} not found");

            // Verify ledger belongs to current user
            if (currencyLedger.UserId != _currentUserService.UserId)
            {
                throw new UnauthorizedAccessException("You do not have access to this currency ledger");
            }

            // Calculate required amount (total cost in source currency)
            var requiredAmount = (request.Shares * request.PricePerShare) + request.Fees;

            // Calculate current balance
            var existingTransactions = await _currencyTransactionRepository.GetByLedgerIdOrderedAsync(
                currencyLedger.Id, cancellationToken);
            var currentBalance = _currencyLedgerService.CalculateBalance(existingTransactions);

            // Validate sufficient balance
            if (currentBalance < requiredAmount)
            {
                throw new InvalidOperationException(
                    $"Insufficient balance in currency ledger. Required: {requiredAmount:F4}, Available: {currentBalance:F4}");
            }

            // Create currency transaction to deduct the amount (Spend type)
            var currencyTransaction = new CurrencyTransaction(
                currencyLedger.Id,
                request.TransactionDate,
                CurrencyTransactionType.Spend,
                requiredAmount,
                notes: $"Stock purchase: {request.Ticker} x {request.Shares}");

            await _currencyTransactionRepository.AddAsync(currencyTransaction, cancellationToken);
        }

        // Create the transaction
        var transaction = new StockTransaction(
            request.PortfolioId,
            request.TransactionDate,
            request.Ticker,
            request.TransactionType,
            request.Shares,
            request.PricePerShare,
            request.ExchangeRate,
            request.Fees,
            request.FundSource,
            request.CurrencyLedgerId,
            request.Notes);

        await _transactionRepository.AddAsync(transaction, cancellationToken);

        return MapToDto(transaction);
    }

    private static StockTransactionDto MapToDto(StockTransaction transaction)
    {
        return new StockTransactionDto
        {
            Id = transaction.Id,
            PortfolioId = transaction.PortfolioId,
            TransactionDate = transaction.TransactionDate,
            Ticker = transaction.Ticker,
            TransactionType = transaction.TransactionType,
            Shares = transaction.Shares,
            PricePerShare = transaction.PricePerShare,
            ExchangeRate = transaction.ExchangeRate,
            Fees = transaction.Fees,
            FundSource = transaction.FundSource,
            CurrencyLedgerId = transaction.CurrencyLedgerId,
            Notes = transaction.Notes,
            TotalCostSource = transaction.TotalCostSource,
            TotalCostHome = transaction.TotalCostHome,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt
        };
    }
}
