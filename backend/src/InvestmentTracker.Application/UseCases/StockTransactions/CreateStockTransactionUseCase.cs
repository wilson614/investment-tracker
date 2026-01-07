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

        // Determine exchange rate - auto-calculate from currency ledger if not provided
        decimal exchangeRate = request.ExchangeRate ?? 0m;
        Domain.Entities.CurrencyLedger? currencyLedger = null;
        decimal? requiredAmount = null;

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

            // Get existing currency transactions
            var currencyTransactions = await _currencyTransactionRepository.GetByLedgerIdOrderedAsync(
                currencyLedger.Id, cancellationToken);

            // Calculate required amount (total cost in source currency)
            requiredAmount = (request.Shares * request.PricePerShare) + request.Fees;

            // If exchange rate not provided, calculate from recent exchanges (LIFO)
            // Only considers actual exchange transactions, not interest/bonuses
            if (exchangeRate <= 0)
            {
                exchangeRate = _currencyLedgerService.CalculateExchangeRateForPurchase(
                    currencyTransactions, request.TransactionDate, requiredAmount.Value);

                if (exchangeRate <= 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot calculate exchange rate from currency ledger. No transactions found on or before {request.TransactionDate:yyyy-MM-dd}");
                }
            }
        }
        else if (exchangeRate <= 0)
        {
            throw new InvalidOperationException(
                "Exchange rate is required when not using a currency ledger as fund source");
        }

        // For sell transactions, validate share balance and calculate realized PnL
        decimal? realizedPnlHome = null;
        if (request.TransactionType == TransactionType.Sell)
        {
            // Get all transactions for this portfolio
            var existingTransactions = await _transactionRepository.GetByPortfolioIdAsync(
                request.PortfolioId, cancellationToken);

            // Calculate current position for this ticker
            var currentPosition = _portfolioCalculator.CalculatePosition(
                request.Ticker, existingTransactions);

            // Validate sufficient shares
            if (currentPosition.TotalShares < request.Shares)
            {
                throw new InvalidOperationException(
                    $"Insufficient shares. Available: {currentPosition.TotalShares:F4}, Requested: {request.Shares:F4}");
            }

            // Create a temporary sell transaction for PnL calculation
            var tempSellTransaction = new StockTransaction(
                request.PortfolioId,
                request.TransactionDate,
                request.Ticker,
                request.TransactionType,
                request.Shares,
                request.PricePerShare,
                exchangeRate,
                request.Fees,
                request.FundSource,
                request.CurrencyLedgerId,
                request.Notes);

            realizedPnlHome = _portfolioCalculator.CalculateRealizedPnl(currentPosition, tempSellTransaction);
        }

        // Create the transaction
        var transaction = new StockTransaction(
            request.PortfolioId,
            request.TransactionDate,
            request.Ticker,
            request.TransactionType,
            request.Shares,
            request.PricePerShare,
            exchangeRate,
            request.Fees,
            request.FundSource,
            request.CurrencyLedgerId,
            request.Notes);

        // Set realized PnL for sell transactions
        if (realizedPnlHome.HasValue)
        {
            transaction.SetRealizedPnl(realizedPnlHome.Value);
        }

        await _transactionRepository.AddAsync(transaction, cancellationToken);

        // Create linked currency transaction after stock transaction is created (to get the ID)
        if (currencyLedger != null && requiredAmount.HasValue)
        {
            var currencyTransaction = new CurrencyTransaction(
                currencyLedger.Id,
                request.TransactionDate,
                CurrencyTransactionType.Spend,
                requiredAmount.Value,
                relatedStockTransactionId: transaction.Id,
                notes: $"Stock purchase: {request.Ticker} x {request.Shares}");

            await _currencyTransactionRepository.AddAsync(currencyTransaction, cancellationToken);
        }

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
            RealizedPnlHome = transaction.RealizedPnlHome,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt
        };
    }
}
