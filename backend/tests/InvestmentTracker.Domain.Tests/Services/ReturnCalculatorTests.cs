using FluentAssertions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Domain.Tests.Services;

public class ReturnCalculatorTests
{
    private readonly IReturnCalculator _calculator = new ReturnCalculator();

    [Fact]
    public void CalculateModifiedDietz_WithStartValueAndSingleContribution_ReturnsExpectedRate()
    {
        var startValue = 10000m;
        var endValue = 12000m;

        var periodStart = new DateTime(2024, 1, 1);
        var periodEnd = new DateTime(2024, 12, 31);

        var cashFlowDate = new DateTime(2024, 7, 1);
        var cashFlows = new List<ReturnCashFlow>
        {
            new(cashFlowDate, 1000m)
        };

        var result = _calculator.CalculateModifiedDietz(startValue, endValue, periodStart, periodEnd, cashFlows);

        var totalDays = (periodEnd.Date - periodStart.Date).Days;
        var daysSinceStart = (cashFlowDate.Date - periodStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;
        var expected = (endValue - startValue - 1000m) / (startValue + 1000m * weight);

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(expected, 0.0000001m);
    }

    [Fact]
    public void CalculateModifiedDietz_WithZeroStartValue_UsesWeightedCashFlowsAsDenominator()
    {
        var startValue = 0m;
        var endValue = 1100m;

        var periodStart = new DateTime(2024, 1, 1);
        var periodEnd = new DateTime(2024, 12, 31);

        var cashFlowDate = new DateTime(2024, 2, 1);
        var cashFlows = new List<ReturnCashFlow>
        {
            new(cashFlowDate, 1000m)
        };

        var result = _calculator.CalculateModifiedDietz(startValue, endValue, periodStart, periodEnd, cashFlows);

        var totalDays = (periodEnd.Date - periodStart.Date).Days;
        var daysSinceStart = (cashFlowDate.Date - periodStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;
        var expected = (endValue - startValue - 1000m) / (startValue + 1000m * weight);

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(expected, 0.0000001m);
    }

    [Fact]
    public void CalculateModifiedDietz_WithNonPositiveDenominator_ReturnsNull()
    {
        var startValue = 1000m;
        var endValue = 0m;

        var periodStart = new DateTime(2024, 1, 1);
        var periodEnd = new DateTime(2024, 12, 31);

        var cashFlows = new List<ReturnCashFlow>
        {
            new(new DateTime(2024, 1, 1), -1000m)
        };

        var result = _calculator.CalculateModifiedDietz(startValue, endValue, periodStart, periodEnd, cashFlows);

        result.Should().BeNull();
    }

    [Fact]
    public void CalculateTimeWeightedReturn_WithSingleCashFlowEvent_LinksSubPeriodsByBeforeAfter()
    {
        var startValue = 1000m;
        var endValue = 1760m;

        var snapshots = new List<ReturnValuationSnapshot>
        {
            // Before cash flow: 1100 (10% growth), After cash flow: 1600 (+500 contribution)
            new(new DateTime(2024, 7, 1), 1100m, 1600m)
        };

        var result = _calculator.CalculateTimeWeightedReturn(startValue, endValue, snapshots);

        // (1100/1000) * (1760/1600) - 1 = 1.1 * 1.1 - 1 = 0.21
        var expected = (1100m / 1000m) * (1760m / 1600m) - 1m;

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(expected, 0.0000001m);
    }

    [Fact]
    public void CalculateTimeWeightedReturn_WithMultipleCashFlowEvents_LinksAllSubPeriods()
    {
        var startValue = 1000m;
        var endValue = 1628m;

        var snapshots = new List<ReturnValuationSnapshot>
        {
            // r1: 1100/1000 = 1.1, then +500 contribution
            new(new DateTime(2024, 3, 1), 1100m, 1600m),
            // r2: 1680/1600 = 1.05, then -200 withdrawal
            new(new DateTime(2024, 9, 1), 1680m, 1480m)
        };

        var result = _calculator.CalculateTimeWeightedReturn(startValue, endValue, snapshots);

        // (1100/1000) * (1680/1600) * (1628/1480) - 1
        var expected = (1100m / 1000m) * (1680m / 1600m) * (1628m / 1480m) - 1m;

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(expected, 0.0000001m);
    }

    [Fact]
    public void CalculateTimeWeightedReturn_WithZeroStartValue_SkipsUntilFirstPositiveStartValue()
    {
        var startValue = 0m;
        var endValue = 1100m;

        var snapshots = new List<ReturnValuationSnapshot>
        {
            // First investment event: value moves from 0 to 1000 due to contribution
            new(new DateTime(2024, 1, 10), 0m, 1000m)
        };

        var result = _calculator.CalculateTimeWeightedReturn(startValue, endValue, snapshots);

        // Only measure from 1000 to 1100
        var expected = 0.1m;

        result.Should().NotBeNull();
        result!.Value.Should().BeApproximately(expected, 0.0000001m);
    }
}
