using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.Performance;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Moq;

namespace InvestmentTracker.Application.Tests.UseCases;

public class CalculateAggregateYearPerformanceUseCaseTests
{
    private readonly Mock<IPortfolioRepository> _portfolioRepositoryMock = new();
    private readonly Mock<IHistoricalPerformanceService> _historicalPerformanceServiceMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

    private readonly PortfolioCalculator _portfolioCalculator = new();
    private readonly IReturnCalculator _returnCalculator = new ReturnCalculator();

    [Fact]
    public async Task ExecuteAsync_MissingPricesHaveSameTickerButDifferentPriceType_ReturnsBothMissingPrices()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "USD");
        var portfolioB = CreatePortfolio(userId, "USD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        var year = 2024;
        var yearEndDate = new DateTime(year, 12, 31);
        var yearStartReferenceDate = new DateTime(year - 1, 12, 31);

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "USD",
                TransactionCount = 1,
                MissingPrices =
                [
                    new MissingPriceDto
                    {
                        Ticker = "AAPL",
                        PriceType = "YearEnd",
                        Date = yearEndDate
                    }
                ]
            });

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioB.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "USD",
                TransactionCount = 1,
                MissingPrices =
                [
                    new MissingPriceDto
                    {
                        Ticker = "AAPL",
                        PriceType = "YearStart",
                        Date = yearStartReferenceDate
                    }
                ]
            });

        var useCase = CreateUseCase();

        var request = new CalculateYearPerformanceRequest { Year = year };

        // Act
        var result = await useCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().HaveCount(2);
        result.MissingPrices.Should().Contain(p =>
            p.Ticker == "AAPL" &&
            p.PriceType == "YearStart" &&
            p.Date == yearStartReferenceDate);
        result.MissingPrices.Should().Contain(p =>
            p.Ticker == "AAPL" &&
            p.PriceType == "YearEnd" &&
            p.Date == yearEndDate);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateMissingPricesWithSameTickerPriceTypeAndDate_DeduplicatesIntoSingleItem()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "USD");
        var portfolioB = CreatePortfolio(userId, "USD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        var year = 2024;
        var duplicateDate = new DateTime(year, 12, 31);

        var duplicateMissingPrice = new MissingPriceDto
        {
            Ticker = "AAPL",
            PriceType = "YearEnd",
            Date = duplicateDate
        };

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "USD",
                TransactionCount = 1,
                MissingPrices = [duplicateMissingPrice]
            });

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioB.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "USD",
                TransactionCount = 1,
                MissingPrices =
                [
                    new MissingPriceDto
                    {
                        Ticker = "aapl",
                        PriceType = "yearend",
                        Date = duplicateDate
                    }
                ]
            });

        var useCase = CreateUseCase();

        var request = new CalculateYearPerformanceRequest { Year = year };

        // Act
        var result = await useCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().HaveCount(1);
        result.MissingPrices.Single().Ticker.Should().Be("AAPL");
        result.MissingPrices.Single().PriceType.Should().Be("YearEnd");
        result.MissingPrices.Single().Date.Should().Be(duplicateDate);
    }

    [Fact]
    public async Task ExecuteAsync_WithMidYearContribution_AggregateModifiedDietzAndTwrShouldDiffer()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "USD");
        var portfolioB = CreatePortfolio(userId, "USD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        var year = 2024;
        var midYear = new DateTime(year, 6, 30);

        // Portfolio A:
        // - start 1000, mid-year contribution 1000, end 2200
        // - TWR is purely market move = +10%
        // - Modified Dietz should be lower because contribution happens mid-year
        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "USD",
                StartValueSource = 1000m,
                EndValueSource = 2200m,
                StartValueHome = 30000m,
                EndValueHome = 66000m,
                NetContributionsSource = 1000m,
                NetContributionsHome = 30000m,
                TimeWeightedReturnPercentageSource = 10d,
                TimeWeightedReturnPercentage = 10d,
                EarliestTransactionDateInYear = midYear,
                TransactionCount = 1,
                MissingPrices = []
            });

        // Portfolio B: mark as active to ensure aggregate branch executes (not single-portfolio early return).
        // Keep financial impact at zero so expected values are controlled by portfolio A.
        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioB.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "USD",
                StartValueSource = 0m,
                EndValueSource = 0m,
                StartValueHome = 0m,
                EndValueHome = 0m,
                NetContributionsSource = 0m,
                NetContributionsHome = 0m,
                TimeWeightedReturnPercentageSource = null,
                TimeWeightedReturnPercentage = null,
                EarliestTransactionDateInYear = midYear,
                TransactionCount = 1,
                MissingPrices = []
            });

        var useCase = CreateUseCase();

        var request = new CalculateYearPerformanceRequest { Year = year };

        // Act
        var result = await useCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.TimeWeightedReturnPercentageSource.Should().BeApproximately(10d, 0.0001d);
        result.TimeWeightedReturnPercentage.Should().BeApproximately(10d, 0.0001d);

        var periodStart = new DateTime(year, 1, 1);
        var periodEnd = new DateTime(year, 12, 31);
        var totalDays = (periodEnd.Date - periodStart.Date).Days;
        var daysSinceStart = (midYear.Date - periodStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;

        var expectedModifiedDietz = (2200m - 1000m - 1000m) / (1000m + 1000m * weight);
        var expectedModifiedDietzPct = (double)(expectedModifiedDietz * 100m);

        result.ModifiedDietzPercentageSource.Should().BeApproximately(expectedModifiedDietzPct, 0.0001d);
        result.ModifiedDietzPercentage.Should().BeApproximately(expectedModifiedDietzPct, 0.0001d);

        result.ModifiedDietzPercentageSource.Should().NotBeApproximately(result.TimeWeightedReturnPercentageSource!.Value, 0.01d);
        result.ModifiedDietzPercentage.Should().NotBeApproximately(result.TimeWeightedReturnPercentage!.Value, 0.01d);
    }

    private CalculateAggregateYearPerformanceUseCase CreateUseCase()
    {
        return new CalculateAggregateYearPerformanceUseCase(
            _portfolioRepositoryMock.Object,
            _historicalPerformanceServiceMock.Object,
            _currentUserServiceMock.Object,
            _portfolioCalculator,
            _returnCalculator);
    }

    private static Portfolio CreatePortfolio(Guid userId, string baseCurrency)
    {
        return new Portfolio(
            userId: userId,
            boundCurrencyLedgerId: Guid.NewGuid(),
            baseCurrency: baseCurrency,
            homeCurrency: "TWD",
            displayName: "Test Portfolio");
    }
}
