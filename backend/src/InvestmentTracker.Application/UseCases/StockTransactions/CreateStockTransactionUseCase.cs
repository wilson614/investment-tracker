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
        decimal? exchangeRate = request.ExchangeRate;
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

            // Validate sufficient balance before creating any transactions
            if (!_currencyLedgerService.ValidateSpend(currencyTransactions, requiredAmount.Value))
            {
                throw new InvalidOperationException("Insufficient balance");
            }

            // If exchange rate not provided, calculate from recent exchanges (LIFO)
            // Only considers actual exchange transactions, not interest/bonuses
            if (!exchangeRate.HasValue || exchangeRate.Value <= 0)
            {
                var calculatedRate = _currencyLedgerService.CalculateExchangeRateForPurchase(
                    currencyTransactions, request.TransactionDate, requiredAmount.Value);

                if (calculatedRate <= 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot calculate exchange rate from currency ledger. No transactions found on or before {request.TransactionDate:yyyy-MM-dd}");
                }

                exchangeRate = calculatedRate;
            }
        }
        else if (exchangeRate.HasValue && exchangeRate.Value <= 0)
        {
            throw new InvalidOperationException("Exchange rate must be greater than zero");
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
                    $"持股不足。可賣出: {currentPosition.TotalShares:F4}，欲賣出: {request.Shares:F4}");
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
                notes: $"買入 {request.Ticker} × {request.Shares}");

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
            HasExchangeRate = transaction.HasExchangeRate,
            RealizedPnlHome = transaction.RealizedPnlHome,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt
        };
    }
}
