using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Performance;

/// <summary>
/// 取得目前使用者所有投資組合可用於績效計算的年度清單。
/// </summary>
public class GetAggregateAvailableYearsUseCase(
    IPortfolioRepository portfolioRepository,
    IStockTransactionRepository transactionRepository,
    ICurrentUserService currentUserService)
{
    public async Task<AvailableYearsDto> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var currentYear = DateTime.UtcNow.Year;

        var portfolios = await portfolioRepository.GetByUserIdAsync(userId, cancellationToken);
        if (portfolios.Count == 0)
        {
            return new AvailableYearsDto
            {
                Years = [],
                EarliestYear = null,
                CurrentYear = currentYear
            };
        }

        var allTransactions = new List<StockTransaction>();
        foreach (var portfolio in portfolios)
        {
            var transactions = await transactionRepository.GetByPortfolioIdAsync(portfolio.Id, cancellationToken);
            allTransactions.AddRange(transactions);
        }

        var validTransactions = allTransactions
            .Where(t => !t.IsDeleted)
            .ToList();

        if (validTransactions.Count == 0)
        {
            return new AvailableYearsDto
            {
                Years = [],
                EarliestYear = null,
                CurrentYear = currentYear
            };
        }

        var earliestYear = validTransactions.Min(t => t.TransactionDate.Year);

        var years = Enumerable.Range(earliestYear, currentYear - earliestYear + 1)
            .OrderDescending()
            .ToList();

        return new AvailableYearsDto
        {
            Years = years,
            EarliestYear = earliestYear,
            CurrentYear = currentYear
        };
    }
}
