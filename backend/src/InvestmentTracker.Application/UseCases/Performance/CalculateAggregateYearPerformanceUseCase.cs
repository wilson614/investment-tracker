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
    private const int MinimumReliableCoverageDays = 90;
    private const string XirrReliabilityLow = "Low";
    private const string XirrReliabilityUnavailable = "Unavailable";
    private const string ReturnDisplayDegradeReasonNoOpeningBaseline = "LOW_CONFIDENCE_NO_OPENING_BASELINE";
    private const string ReturnDisplayDegradeReasonLowCoverage = "LOW_CONFIDENCE_LOW_COVERAGE";
    private const string ReturnDisplayDegradeReasonNoOpeningBaselineAndLowCoverage = "LOW_CONFIDENCE_NO_OPENING_BASELINE_AND_LOW_COVERAGE";
    private const string RecentLargeInflowWarningMessage = "近期大額資金異動可能導致資金加權報酬率短期波動。";

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

        var relevantPortfolioResults = portfolioResults
            .Where(HasPortfolioActivity)
            .ToList();

        if (relevantPortfolioResults.Count == 0)
            relevantPortfolioResults = portfolioResults;

        if (relevantPortfolioResults.Count == 1)
        {
            var single = relevantPortfolioResults[0] with { Year = request.Year };
            return ApplyAggregateXirrSuppression(single);
        }

        var earliestTransactionDateInYear = relevantPortfolioResults
            .Where(r => r.EarliestTransactionDateInYear.HasValue)
            .Select(r => r.EarliestTransactionDateInYear)
            .Min();

        var missingPrices = relevantPortfolioResults
            .SelectMany(r => r.MissingPrices)
            .DistinctBy(p => (
                Ticker: (p.Ticker ?? string.Empty).ToUpperInvariant(),
                PriceType: (p.PriceType ?? string.Empty).ToUpperInvariant(),
                Date: p.Date.Date))
            .ToList();

        var year = request.Year;
        var currentYear = DateTime.UtcNow.Year;
        var isYtd = year == currentYear;
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = isYtd ? DateTime.UtcNow.Date : new DateTime(year, 12, 31);

        var resolvedCoverageStartDate = relevantPortfolioResults
            .Where(r => r.CoverageStartDate.HasValue)
            .Select(r => (DateTime?)r.CoverageStartDate!.Value.Date)
            .Min();

        int? coverageDays = resolvedCoverageStartDate.HasValue
            ? Math.Max(0, (yearEnd.Date - resolvedCoverageStartDate.Value.Date).Days + 1)
            : null;

        var hasOpeningBaseline = relevantPortfolioResults
            .Where(r => r.HasOpeningBaseline.HasValue)
            .Select(r => r.HasOpeningBaseline!.Value)
            .DefaultIfEmpty(false)
            .All(x => x);

        var usesPartialHistoryAssumption = relevantPortfolioResults
            .Any(r => r.UsesPartialHistoryAssumption == true);

        var rawXirrReliability = ResolveAggregateXirrReliability(relevantPortfolioResults);
        var aggregateDegradeSignal = ResolveReturnDisplayDegradeSignal(
            rawXirrReliability,
            hasOpeningBaseline,
            coverageDays);

        var recentLargeInflowWarning = ResolveAggregateRecentLargeInflowWarning(relevantPortfolioResults);

        if (missingPrices.Count > 0)
        {
            return ApplyAggregateXirrSuppression(new YearPerformanceDto
            {
                Year = request.Year,
                SourceCurrency = ResolveSourceCurrency(relevantPortfolioResults),
                MissingPrices = missingPrices,
                CashFlowCount = 0,
                TransactionCount = relevantPortfolioResults.Sum(r => r.TransactionCount),
                EarliestTransactionDateInYear = earliestTransactionDateInYear,
                CoverageStartDate = resolvedCoverageStartDate,
                CoverageDays = coverageDays,
                HasOpeningBaseline = hasOpeningBaseline,
                UsesPartialHistoryAssumption = usesPartialHistoryAssumption,
                XirrReliability = rawXirrReliability,
                ShouldDegradeReturnDisplay = aggregateDegradeSignal.ShouldDegrade,
                ReturnDisplayDegradeReasonCode = aggregateDegradeSignal.ReasonCode,
                ReturnDisplayDegradeReasonMessage = aggregateDegradeSignal.ReasonMessage,
                HasRecentLargeInflowWarning = recentLargeInflowWarning.ShouldWarn,
                RecentLargeInflowWarningMessage = recentLargeInflowWarning.WarningMessage
            });
        }

        var startValueHome = relevantPortfolioResults.Sum(r => r.StartValueHome ?? 0m);
        var endValueHome = relevantPortfolioResults.Sum(r => r.EndValueHome ?? 0m);
        var netContributionsHome = relevantPortfolioResults.Sum(r => r.NetContributionsHome);

        var startValueSource = relevantPortfolioResults.Sum(r => r.StartValueSource ?? 0m);
        var endValueSource = relevantPortfolioResults.Sum(r => r.EndValueSource ?? 0m);
        var netContributionsSource = relevantPortfolioResults.Sum(r => r.NetContributionsSource ?? 0m);

        var aggregateCashFlowsHome = BuildDerivedCashFlows(
            relevantPortfolioResults,
            yearStart,
            yearEnd,
            useSourceValues: false);

        var aggregateCashFlowsSource = BuildDerivedCashFlows(
            relevantPortfolioResults,
            yearStart,
            yearEnd,
            useSourceValues: true);

        var xirrHome = portfolioCalculator.CalculateXirr(aggregateCashFlowsHome);
        var xirrSource = portfolioCalculator.CalculateXirr(aggregateCashFlowsSource);

        var totalReturnHome = CalculateTotalReturnPercentage(startValueHome, endValueHome, netContributionsHome);
        var totalReturnSource = CalculateTotalReturnPercentage(startValueSource, endValueSource, netContributionsSource);

        var dietzCashFlowsHome = BuildDietzCashFlows(
            relevantPortfolioResults,
            yearStart,
            useSourceValues: false);

        var dietzCashFlowsSource = BuildDietzCashFlows(
            relevantPortfolioResults,
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
            relevantPortfolioResults,
            returnSelector: r => r.TimeWeightedReturnPercentage,
            primaryWeightSelector: r => r.StartValueHome,
            fallbackWeightSelector: r => r.EndValueHome);

        var twrSource = ComputeWeightedAverageReturnPercentage(
            relevantPortfolioResults,
            returnSelector: r => r.TimeWeightedReturnPercentageSource,
            primaryWeightSelector: r => r.StartValueSource,
            fallbackWeightSelector: r => r.EndValueSource);

        var xirrPercentageHome = xirrHome.HasValue ? xirrHome.Value * 100 : (double?)null;
        var xirrPercentageSource = xirrSource.HasValue ? xirrSource.Value * 100 : (double?)null;

        return ApplyAggregateXirrSuppression(new YearPerformanceDto
        {
            Year = request.Year,
            // Home currency
            Xirr = xirrHome,
            XirrPercentage = xirrPercentageHome,
            TotalReturnPercentage = totalReturnHome,
            ModifiedDietzPercentage = modifiedDietzHome.HasValue ? (double)(modifiedDietzHome.Value * 100m) : null,
            TimeWeightedReturnPercentage = twrHome,
            StartValueHome = startValueHome,
            EndValueHome = endValueHome,
            NetContributionsHome = netContributionsHome,
            // Source currency
            SourceCurrency = ResolveSourceCurrency(relevantPortfolioResults),
            XirrSource = xirrSource,
            XirrPercentageSource = xirrPercentageSource,
            TotalReturnPercentageSource = totalReturnSource,
            ModifiedDietzPercentageSource = modifiedDietzSource.HasValue ? (double)(modifiedDietzSource.Value * 100m) : null,
            TimeWeightedReturnPercentageSource = twrSource,
            StartValueSource = startValueSource,
            EndValueSource = endValueSource,
            NetContributionsSource = netContributionsSource,
            // Common
            CashFlowCount = aggregateCashFlowsSource.Count,
            TransactionCount = relevantPortfolioResults.Sum(r => r.TransactionCount),
            EarliestTransactionDateInYear = earliestTransactionDateInYear,
            CoverageStartDate = resolvedCoverageStartDate,
            CoverageDays = coverageDays,
            HasOpeningBaseline = hasOpeningBaseline,
            UsesPartialHistoryAssumption = usesPartialHistoryAssumption,
            XirrReliability = rawXirrReliability,
            ShouldDegradeReturnDisplay = aggregateDegradeSignal.ShouldDegrade,
            ReturnDisplayDegradeReasonCode = aggregateDegradeSignal.ReasonCode,
            ReturnDisplayDegradeReasonMessage = aggregateDegradeSignal.ReasonMessage,
            HasRecentLargeInflowWarning = recentLargeInflowWarning.ShouldWarn,
            RecentLargeInflowWarningMessage = recentLargeInflowWarning.WarningMessage,
            MissingPrices = []
        });
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

    private static bool HasPortfolioActivity(YearPerformanceDto result)
    {
        return result.TransactionCount > 0
               || result.CashFlowCount > 0
               || result.EarliestTransactionDateInYear.HasValue
               || result.MissingPrices.Count > 0
               || (result.StartValueHome ?? 0m) != 0m
               || (result.EndValueHome ?? 0m) != 0m
               || result.NetContributionsHome != 0m
               || (result.StartValueSource ?? 0m) != 0m
               || (result.EndValueSource ?? 0m) != 0m
               || (result.NetContributionsSource ?? 0m) != 0m;
    }

    private static YearPerformanceDto ApplyAggregateXirrSuppression(YearPerformanceDto dto)
    {
        var shouldSuppress = string.Equals(dto.XirrReliability, "Low", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(dto.XirrReliability, "Unavailable", StringComparison.OrdinalIgnoreCase);

        if (shouldSuppress)
        {
            return dto with
            {
                Xirr = null,
                XirrPercentage = null,
                XirrSource = null,
                XirrPercentageSource = null
            };
        }

        var xirrPercentage = dto.Xirr.HasValue ? dto.Xirr.Value * 100 : (double?)null;
        var xirrPercentageSource = dto.XirrSource.HasValue ? dto.XirrSource.Value * 100 : (double?)null;

        return dto with
        {
            XirrPercentage = xirrPercentage,
            XirrPercentageSource = xirrPercentageSource
        };
    }

    private static string ResolveAggregateXirrReliability(IReadOnlyList<YearPerformanceDto> portfolioResults)
    {
        if (portfolioResults.Count == 0)
            return "Unavailable";

        if (portfolioResults.Any(r => string.Equals(r.XirrReliability, "Unavailable", StringComparison.OrdinalIgnoreCase)))
            return "Unavailable";

        if (portfolioResults.Any(r => string.Equals(r.XirrReliability, "Low", StringComparison.OrdinalIgnoreCase)))
            return "Low";

        if (portfolioResults.Any(r => string.Equals(r.XirrReliability, "Medium", StringComparison.OrdinalIgnoreCase)))
            return "Medium";

        if (portfolioResults.Any(r => string.Equals(r.XirrReliability, "High", StringComparison.OrdinalIgnoreCase)))
            return "High";

        return "Unavailable";
    }

    private static ReturnDisplayDegradeSignal ResolveReturnDisplayDegradeSignal(
        string? xirrReliability,
        bool hasOpeningBaseline,
        int? coverageDays)
    {
        var isLowConfidence = string.Equals(xirrReliability, XirrReliabilityLow, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(xirrReliability, XirrReliabilityUnavailable, StringComparison.OrdinalIgnoreCase);

        if (!isLowConfidence)
            return ReturnDisplayDegradeSignal.None;

        var hasLowCoverage = !coverageDays.HasValue || coverageDays.Value < MinimumReliableCoverageDays;

        if (!hasOpeningBaseline && hasLowCoverage)
        {
            return new ReturnDisplayDegradeSignal(
                ShouldDegrade: true,
                ReasonCode: ReturnDisplayDegradeReasonNoOpeningBaselineAndLowCoverage,
                ReasonMessage: "Low confidence aggregate performance: missing opening baseline and insufficient coverage.");
        }

        if (!hasOpeningBaseline)
        {
            return new ReturnDisplayDegradeSignal(
                ShouldDegrade: true,
                ReasonCode: ReturnDisplayDegradeReasonNoOpeningBaseline,
                ReasonMessage: "Low confidence aggregate performance: missing opening baseline.");
        }

        if (hasLowCoverage)
        {
            return new ReturnDisplayDegradeSignal(
                ShouldDegrade: true,
                ReasonCode: ReturnDisplayDegradeReasonLowCoverage,
                ReasonMessage: "Low confidence aggregate performance: insufficient coverage period.");
        }

        return ReturnDisplayDegradeSignal.None;
    }

    private static RecentLargeInflowWarningSignal ResolveAggregateRecentLargeInflowWarning(
        IReadOnlyList<YearPerformanceDto> portfolioResults)
    {
        if (!portfolioResults.Any(result => result.HasRecentLargeInflowWarning))
            return RecentLargeInflowWarningSignal.None;

        return new RecentLargeInflowWarningSignal(
            ShouldWarn: true,
            WarningMessage: RecentLargeInflowWarningMessage);
    }

    private static double? CalculateTotalReturnPercentage(decimal startValue, decimal endValue, decimal netContributions)
    {
        if (startValue != 0)
            return (double)((endValue - startValue - netContributions) / startValue) * 100;

        if (netContributions != 0)
            return (double)((endValue - netContributions) / netContributions) * 100;

        return null;
    }

    private readonly record struct ReturnDisplayDegradeSignal(
        bool ShouldDegrade,
        string? ReasonCode,
        string? ReasonMessage)
    {
        public static ReturnDisplayDegradeSignal None { get; } = new(
            ShouldDegrade: false,
            ReasonCode: null,
            ReasonMessage: null);
    }

    private readonly record struct RecentLargeInflowWarningSignal(
        bool ShouldWarn,
        string? WarningMessage)
    {
        public static RecentLargeInflowWarningSignal None { get; } = new(
            ShouldWarn: false,
            WarningMessage: null);
    }
}
