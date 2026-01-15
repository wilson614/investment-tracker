using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

/// <summary>
/// Use case for updating an existing stock transaction.
/// </summary>
public class UpdateStockTransactionUseCase
{
    private readonly IStockTransactionRepository _transactionRepository;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly PortfolioCalculator _portfolioCalculator;

    public UpdateStockTransactionUseCase(
        IStockTransactionRepository transactionRepository,
        IPortfolioRepository portfolioRepository,
        ICurrentUserService currentUserService,
        PortfolioCalculator portfolioCalculator)
    {
        _transactionRepository = transactionRepository;
        _portfolioRepository = portfolioRepository;
        _currentUserService = currentUserService;
        _portfolioCalculator = portfolioCalculator;
    }

    public async Task<StockTransactionDto> ExecuteAsync(
        Guid transactionId,
        UpdateStockTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(transactionId, cancellationToken)
            ?? throw new InvalidOperationException($"Transaction {transactionId} not found");

        // Verify access through portfolio
        var portfolio = await _portfolioRepository.GetByIdAsync(transaction.PortfolioId, cancellationToken)
            ?? throw new InvalidOperationException($"Portfolio {transaction.PortfolioId} not found");

        if (portfolio.UserId != _currentUserService.UserId)
        {
            throw new UnauthorizedAccessException("You do not have access to this transaction");
        }

        // Track original values for calculations
        var originalTicker = transaction.Ticker;
        var originalType = transaction.TransactionType;

        // Update transaction properties (including ticker and type)
        transaction.SetTicker(request.Ticker);
        transaction.SetTransactionType(request.TransactionType);
        transaction.SetTransactionDate(request.TransactionDate);
        transaction.SetShares(request.Shares);
        transaction.SetPricePerShare(request.PricePerShare);
        transaction.SetExchangeRate(request.ExchangeRate);
        transaction.SetFees(request.Fees);
        transaction.SetFundSource(request.FundSource, request.CurrencyLedgerId);
        transaction.SetNotes(request.Notes);

        // Recalculate realized PnL for sell transactions (supports changing Buy/Sell)
        if (transaction.TransactionType == TransactionType.Sell)
        {
            // Get all transactions for this portfolio
            var allTransactions = await _transactionRepository.GetByPortfolioIdAsync(
                transaction.PortfolioId, cancellationToken);

            // Calculate position BEFORE this sell transaction using only earlier transactions
            var transactionsBeforeSell = allTransactions
                .Where(t => t.Id != transaction.Id) // Exclude current transaction
                .Where(t => t.TransactionDate < transaction.TransactionDate ||
                           (t.TransactionDate == transaction.TransactionDate && t.CreatedAt < transaction.CreatedAt))
                .ToList();

            var positionBeforeSell = _portfolioCalculator.CalculatePosition(
                transaction.Ticker, transactionsBeforeSell);

            // Validate sufficient shares
            if (positionBeforeSell.TotalShares < request.Shares)
            {
                throw new InvalidOperationException(
                    $"持股不足。可賣出: {positionBeforeSell.TotalShares:F4}，欲賣出: {request.Shares:F4}");
            }

            // Create a temporary transaction with updated values for PnL calculation
            var tempSellTransaction = new StockTransaction(
                transaction.PortfolioId,
                request.TransactionDate,
                transaction.Ticker,
                TransactionType.Sell,
                request.Shares,
                request.PricePerShare,
                request.ExchangeRate ?? 1.0m,
                request.Fees);

            var realizedPnl = _portfolioCalculator.CalculateRealizedPnl(positionBeforeSell, tempSellTransaction);
            transaction.SetRealizedPnl(realizedPnl);
        }
        else if (originalType == TransactionType.Sell)
        {
            // If changed from Sell to Buy, clear realized PnL
            transaction.SetRealizedPnl(null);
        }

        await _transactionRepository.UpdateAsync(transaction, cancellationToken);

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
