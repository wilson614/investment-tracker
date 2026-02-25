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
        static bool CrossesZero(decimal from, decimal to) =>
            (from > 0m && to < 0m) || (from < 0m && to > 0m);

        if (startValue < 0m || endValue < 0m)
            return null;

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
            if (snapshot.ValueBefore < 0m || snapshot.ValueAfter < 0m)
                return null;

            if (CrossesZero(currentStartValue, snapshot.ValueBefore)
                || CrossesZero(snapshot.ValueBefore, snapshot.ValueAfter))
            {
                return null;
            }

            // In before/after snapshots, denominator = startValue + netCashFlow maps to
            // the current sub-period start (ValueAfter of previous event, or startValue for first segment).
            // When denominator == 0, this is a 0/0 blank segment (e.g. no position before first trade),
            // so skip the sub-period to avoid divide-by-zero and accidental factor collapse.
            var denominator = currentStartValue;
            if (denominator == 0m)
            {
                currentStartValue = snapshot.ValueAfter;
                continue;
            }

            factor *= snapshot.ValueBefore / denominator;
            hasAnyPeriod = true;
            currentStartValue = snapshot.ValueAfter;
        }

        if (CrossesZero(currentStartValue, endValue))
            return null;

        var tailDenominator = currentStartValue;
        if (tailDenominator == 0m)
            return hasAnyPeriod ? factor - 1m : null;

        factor *= endValue / tailDenominator;
        hasAnyPeriod = true;

        return factor - 1m;
    }
}
