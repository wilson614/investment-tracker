using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Domain.Services;

/// <summary>
/// 報酬率計算器：Modified Dietz 與 Time-Weighted Return (TWR)。
/// </summary>
public class ReturnCalculator : IReturnCalculator
{
    /// <inheritdoc />
    public decimal? CalculateModifiedDietz(
        decimal startValue,
        decimal endValue,
        DateTime periodStart,
        DateTime periodEnd,
        IReadOnlyList<ReturnCashFlow> cashFlows)
    {
        var startDate = periodStart.Date;
        var endDate = periodEnd.Date;

        var totalDays = (endDate - startDate).Days;
        if (totalDays <= 0)
            return null;

        var totalCashFlow = 0m;
        var weightedCashFlow = 0m;

        foreach (var cashFlow in cashFlows)
        {
            var cashFlowDate = cashFlow.Date.Date;
            if (cashFlowDate < startDate || cashFlowDate > endDate)
                continue;

            var daysSinceStart = (cashFlowDate - startDate).Days;
            var weight = (totalDays - daysSinceStart) / (decimal)totalDays;

            totalCashFlow += cashFlow.Amount;
            weightedCashFlow += cashFlow.Amount * weight;
        }

        var numerator = endValue - startValue - totalCashFlow;
        var denominator = startValue + weightedCashFlow;

        if (denominator <= 0)
            return null;

        return numerator / denominator;
    }

    /// <inheritdoc />
    public decimal? CalculateTimeWeightedReturn(
        decimal startValue,
        decimal endValue,
        IReadOnlyList<ReturnValuationSnapshot> cashFlowSnapshots)
    {
        var orderedSnapshots = cashFlowSnapshots
            .Select((snapshot, index) => (snapshot, index))
            .OrderBy(x => x.snapshot.Date.Date)
            .ThenBy(x => x.index)
            .Select(x => x.snapshot)
            .ToList();

        var factor = 1m;
        var currentStartValue = startValue;
        var hasAnyPeriod = false;

        foreach (var snapshot in orderedSnapshots)
        {
            if (currentStartValue > 0)
            {
                factor *= snapshot.ValueBefore / currentStartValue;
                hasAnyPeriod = true;
            }

            currentStartValue = snapshot.ValueAfter;
        }

        if (currentStartValue > 0)
        {
            factor *= endValue / currentStartValue;
            hasAnyPeriod = true;
        }

        return hasAnyPeriod ? factor - 1 : null;
    }
}
