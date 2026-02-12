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
    private readonly Mock<IReturnCalculator> _returnCalculatorMock = new();

    private readonly PortfolioCalculator _portfolioCalculator = new();

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

    private CalculateAggregateYearPerformanceUseCase CreateUseCase()
    {
        return new CalculateAggregateYearPerformanceUseCase(
            _portfolioRepositoryMock.Object,
            _historicalPerformanceServiceMock.Object,
            _currentUserServiceMock.Object,
            _portfolioCalculator,
            _returnCalculatorMock.Object);
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
