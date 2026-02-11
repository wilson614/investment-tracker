using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.Performance;

/// <summary>
/// 計算目前使用者所有投資組合合併後的指定年度績效。
/// </summary>
public class CalculateAggregateYearPerformanceUseCase(
    IPortfolioRepository portfolioRepository,
    IHistoricalPerformanceService historicalPerformanceService,
    ICurrentUserService currentUserService,
    PortfolioCalculator portfolioCalculator,
    IReturnCalculator returnCalculator)
{
    public async Task<YearPerformanceDto> ExecuteAsync(
        CalculateYearPerformanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolios = await portfolioRepository.GetByUserIdAsync(userId, cancellationToken);
        if (portfolios.Count == 0)
            throw new EntityNotFoundException("Portfolio");

        var portfolioResults = new List<YearPerformanceDto>(portfolios.Count);
        foreach (var portfolio in portfolios)
        {
            var result = await historicalPerformanceService.CalculateYearPerformanceAsync(
                portfolio.Id,
                request,
                cancellationToken);

            portfolioResults.Add(result);
        }

        var earliestTransactionDateInYear = portfolioResults
            .Where(r => r.EarliestTransactionDateInYear.HasValue)
            .Select(r => r.EarliestTransactionDateInYear)
            .Min();

        var missingPrices = portfolioResults
            .SelectMany(r => r.MissingPrices)
            .DistinctBy(p => p.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingPrices.Count > 0)
        {
            return new YearPerformanceDto
            {
                Year = request.Year,
                SourceCurrency = ResolveSourceCurrency(portfolioResults),
                MissingPrices = missingPrices,
                CashFlowCount = 0,
                TransactionCount = portfolioResults.Sum(r => r.TransactionCount),
                EarliestTransactionDateInYear = earliestTransactionDateInYear
            };
        }

        var startValueHome = portfolioResults.Sum(r => r.StartValueHome ?? 0m);
        var endValueHome = portfolioResults.Sum(r => r.EndValueHome ?? 0m);
        var netContributionsHome = portfolioResults.Sum(r => r.NetContributionsHome);

        var startValueSource = portfolioResults.Sum(r => r.StartValueSource ?? 0m);
        var endValueSource = portfolioResults.Sum(r => r.EndValueSource ?? 0m);
        var netContributionsSource = portfolioResults.Sum(r => r.NetContributionsSource ?? 0m);

        var year = request.Year;
        var currentYear = DateTime.UtcNow.Year;
        var isYtd = year == currentYear;
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = isYtd ? DateTime.UtcNow.Date : new DateTime(year, 12, 31);

        var aggregateCashFlowsHome = BuildDerivedCashFlows(
            portfolioResults,
            yearStart,
            yearEnd,
            useSourceValues: false);

        var aggregateCashFlowsSource = BuildDerivedCashFlows(
            portfolioResults,
            yearStart,
            yearEnd,
            useSourceValues: true);

        var xirrHome = portfolioCalculator.CalculateXirr(aggregateCashFlowsHome);
        var xirrSource = portfolioCalculator.CalculateXirr(aggregateCashFlowsSource);

        var totalReturnHome = CalculateTotalReturnPercentage(startValueHome, endValueHome, netContributionsHome);
        var totalReturnSource = CalculateTotalReturnPercentage(startValueSource, endValueSource, netContributionsSource);

        var dietzCashFlowsHome = BuildDietzCashFlows(
            portfolioResults,
            yearStart,
            useSourceValues: false);

        var dietzCashFlowsSource = BuildDietzCashFlows(
            portfolioResults,
            yearStart,
            useSourceValues: true);

        var modifiedDietzHome = returnCalculator.CalculateModifiedDietz(
            startValue: startValueHome,
            endValue: endValueHome,
            periodStart: yearStart,
            periodEnd: yearEnd,
            cashFlows: dietzCashFlowsHome);

        var modifiedDietzSource = returnCalculator.CalculateModifiedDietz(
            startValue: startValueSource,
            endValue: endValueSource,
            periodStart: yearStart,
            periodEnd: yearEnd,
            cashFlows: dietzCashFlowsSource);

        var twrHome = ComputeWeightedAverageReturnPercentage(
            portfolioResults,
            returnSelector: r => r.TimeWeightedReturnPercentage,
            primaryWeightSelector: r => r.StartValueHome,
            fallbackWeightSelector: r => r.EndValueHome);

        var twrSource = ComputeWeightedAverageReturnPercentage(
            portfolioResults,
            returnSelector: r => r.TimeWeightedReturnPercentageSource,
            primaryWeightSelector: r => r.StartValueSource,
            fallbackWeightSelector: r => r.EndValueSource);

        return new YearPerformanceDto
        {
            Year = request.Year,
            // Home currency
            Xirr = xirrHome,
            XirrPercentage = xirrHome * 100,
            TotalReturnPercentage = totalReturnHome,
            ModifiedDietzPercentage = modifiedDietzHome.HasValue ? (double)(modifiedDietzHome.Value * 100m) : null,
            TimeWeightedReturnPercentage = twrHome,
            StartValueHome = startValueHome > 0 ? startValueHome : null,
            EndValueHome = endValueHome > 0 ? endValueHome : null,
            NetContributionsHome = netContributionsHome,
            // Source currency
            SourceCurrency = ResolveSourceCurrency(portfolioResults),
            XirrSource = xirrSource,
            XirrPercentageSource = xirrSource * 100,
            TotalReturnPercentageSource = totalReturnSource,
            ModifiedDietzPercentageSource = modifiedDietzSource.HasValue ? (double)(modifiedDietzSource.Value * 100m) : null,
            TimeWeightedReturnPercentageSource = twrSource,
            StartValueSource = startValueSource > 0 ? startValueSource : null,
            EndValueSource = endValueSource > 0 ? endValueSource : null,
            NetContributionsSource = netContributionsSource,
            // Common
            CashFlowCount = aggregateCashFlowsSource.Count,
            TransactionCount = portfolioResults.Sum(r => r.TransactionCount),
            EarliestTransactionDateInYear = earliestTransactionDateInYear,
            MissingPrices = []
        };
    }

    private static List<CashFlow> BuildDerivedCashFlows(
        IReadOnlyList<YearPerformanceDto> portfolioResults,
        DateTime yearStart,
        DateTime yearEnd,
        bool useSourceValues)
    {
        var cashFlows = new List<CashFlow>();

        foreach (var result in portfolioResults)
        {
            var startValue = useSourceValues
                ? result.StartValueSource ?? 0m
                : result.StartValueHome ?? 0m;

            var endValue = useSourceValues
                ? result.EndValueSource ?? 0m
                : result.EndValueHome ?? 0m;

            var netContribution = useSourceValues
                ? result.NetContributionsSource ?? 0m
                : result.NetContributionsHome;

            var contributionDate = result.EarliestTransactionDateInYear?.Date ?? yearStart.AddDays(1);

            if (startValue > 0)
                cashFlows.Add(new CashFlow(-startValue, yearStart));

            if (netContribution != 0)
                cashFlows.Add(new CashFlow(-netContribution, contributionDate));

            if (endValue > 0)
                cashFlows.Add(new CashFlow(endValue, yearEnd));
        }

        return cashFlows
            .OrderBy(cf => cf.Date)
            .ToList();
    }

    private static IReadOnlyList<ReturnCashFlow> BuildDietzCashFlows(
        IReadOnlyList<YearPerformanceDto> portfolioResults,
        DateTime yearStart,
        bool useSourceValues)
    {
        return portfolioResults
            .Select(result => new
            {
                Date = result.EarliestTransactionDateInYear?.Date ?? yearStart.AddDays(1),
                Amount = useSourceValues
                    ? result.NetContributionsSource ?? 0m
                    : result.NetContributionsHome
            })
            .Where(x => x.Amount != 0)
            .Select(x => new ReturnCashFlow(x.Date, x.Amount))
            .ToList();
    }

    private static double? ComputeWeightedAverageReturnPercentage(
        IReadOnlyList<YearPerformanceDto> portfolioResults,
        Func<YearPerformanceDto, double?> returnSelector,
        Func<YearPerformanceDto, decimal?> primaryWeightSelector,
        Func<YearPerformanceDto, decimal?> fallbackWeightSelector)
    {
        var withPrimaryWeights = CalculateWeightedAverage(
            portfolioResults,
            returnSelector,
            primaryWeightSelector);

        if (withPrimaryWeights.HasValue)
            return withPrimaryWeights;

        return CalculateWeightedAverage(
            portfolioResults,
            returnSelector,
            fallbackWeightSelector);
    }

    private static double? CalculateWeightedAverage(
        IReadOnlyList<YearPerformanceDto> portfolioResults,
        Func<YearPerformanceDto, double?> returnSelector,
        Func<YearPerformanceDto, decimal?> weightSelector)
    {
        var weightedData = portfolioResults
            .Select(r => new
            {
                Return = returnSelector(r),
                Weight = weightSelector(r) ?? 0m
            })
            .Where(x => x.Return.HasValue && x.Weight > 0)
            .ToList();

        if (weightedData.Count == 0)
            return null;

        var totalWeight = weightedData.Sum(x => x.Weight);
        if (totalWeight <= 0)
            return null;

        var weightedSum = weightedData.Sum(x => x.Return!.Value * (double)(x.Weight / totalWeight));
        return weightedSum;
    }

    private static string? ResolveSourceCurrency(IReadOnlyList<YearPerformanceDto> portfolioResults)
    {
        return portfolioResults
            .Select(r => r.SourceCurrency)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
    }

    private static double? CalculateTotalReturnPercentage(decimal startValue, decimal endValue, decimal netContributions)
    {
        if (startValue > 0)
            return (double)((endValue - startValue - netContributions) / startValue) * 100;

        if (netContributions > 0)
            return (double)((endValue - netContributions) / netContributions) * 100;

        return null;
    }
}
