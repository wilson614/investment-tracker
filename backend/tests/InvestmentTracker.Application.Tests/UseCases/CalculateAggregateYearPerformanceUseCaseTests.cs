using System.Diagnostics;
using System.Globalization;
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

        result.HasOpeningBaseline.Should().BeFalse();
        result.UsesPartialHistoryAssumption.Should().BeFalse();
        result.CoverageStartDate.Should().BeNull();
        result.CoverageDays.Should().BeNull();
        result.XirrReliability.Should().Be("Unavailable");
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

        result.HasOpeningBaseline.Should().BeFalse();
        result.UsesPartialHistoryAssumption.Should().BeFalse();
        result.CoverageStartDate.Should().BeNull();
        result.CoverageDays.Should().BeNull();
        result.XirrReliability.Should().Be("Unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingPricesButSeededBaselineValues_StillAggregatesReturnsAndPreservesWarnings()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "TWD");
        var portfolioB = CreatePortfolio(userId, "TWD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        const int year = 2025;
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = new DateTime(year, 12, 31);

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "TWD",
                StartValueSource = 980m,
                EndValueSource = 1000m,
                StartValueHome = 980m,
                EndValueHome = 1000m,
                NetContributionsSource = 0m,
                NetContributionsHome = 0m,
                TimeWeightedReturnPercentageSource = 2.0408163265d,
                TimeWeightedReturnPercentage = 2.0408163265d,
                TotalReturnPercentageSource = 2.0408163265d,
                TotalReturnPercentage = 2.0408163265d,
                EarliestTransactionDateInYear = yearStart,
                TransactionCount = 0,
                CoverageStartDate = yearStart,
                CoverageDays = 365,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                MissingPrices =
                [
                    new MissingPriceDto
                    {
                        Ticker = "2330",
                        PriceType = "YearStart",
                        Date = yearStart.AddDays(-1)
                    }
                ],
                YearStartHoldingProjections =
                [
                    new CurrentHoldingProjectionDto
                    {
                        Ticker = "2330",
                        Shares = 10m,
                        CostSource = 980m,
                        CostHome = 980m,
                        MarketValueSource = 980m,
                        MarketValueHome = 980m,
                        ValuationSource = PositionValuationSource.CostBasisFallback
                    }
                ],
                YearEndHoldingProjections =
                [
                    new CurrentHoldingProjectionDto
                    {
                        Ticker = "2330",
                        Shares = 10m,
                        CostSource = 980m,
                        CostHome = 980m,
                        MarketValueSource = 1000m,
                        MarketValueHome = 1000m,
                        ValuationSource = PositionValuationSource.MarketPrice
                    }
                ]
            });

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioB.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "TWD",
                StartValueSource = 500m,
                EndValueSource = 550m,
                StartValueHome = 500m,
                EndValueHome = 550m,
                NetContributionsSource = 0m,
                NetContributionsHome = 0m,
                TimeWeightedReturnPercentageSource = 10d,
                TimeWeightedReturnPercentage = 10d,
                TotalReturnPercentageSource = 10d,
                TotalReturnPercentage = 10d,
                EarliestTransactionDateInYear = yearStart,
                TransactionCount = 0,
                CoverageStartDate = yearStart,
                CoverageDays = 365,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                MissingPrices =
                [
                    new MissingPriceDto
                    {
                        Ticker = "AAPL",
                        PriceType = "YearEnd",
                        Date = yearEnd
                    }
                ],
                YearStartHoldingProjections =
                [
                    new CurrentHoldingProjectionDto
                    {
                        Ticker = "AAPL",
                        Shares = 5m,
                        CostSource = 500m,
                        CostHome = 500m,
                        MarketValueSource = 500m,
                        MarketValueHome = 500m,
                        ValuationSource = PositionValuationSource.CostBasisFallback
                    }
                ],
                YearEndHoldingProjections =
                [
                    new CurrentHoldingProjectionDto
                    {
                        Ticker = "AAPL",
                        Shares = 5m,
                        CostSource = 500m,
                        CostHome = 500m,
                        MarketValueSource = 550m,
                        MarketValueHome = 550m,
                        ValuationSource = PositionValuationSource.MarketPrice
                    }
                ]
            });

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CalculateYearPerformanceRequest { Year = year }, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().HaveCount(2);
        result.MissingPrices.Should().Contain(p => p.Ticker == "2330" && p.PriceType == "YearStart");
        result.MissingPrices.Should().Contain(p => p.Ticker == "AAPL" && p.PriceType == "YearEnd");

        result.StartValueSource.Should().BeNull();
        result.EndValueSource.Should().BeNull();
        result.StartValueHome.Should().Be(1480m);
        result.EndValueHome.Should().Be(1550m);
        result.NetContributionsSource.Should().BeNull();
        result.NetContributionsHome.Should().Be(0m);

        result.TotalReturnPercentageSource.Should().BeNull();
        result.TotalReturnPercentage.Should().BeApproximately(4.7297297297d, 0.0001d);
        result.ModifiedDietzPercentageSource.Should().BeNull();
        result.ModifiedDietzPercentage.Should().BeApproximately(4.7297297297d, 0.0001d);
        result.TimeWeightedReturnPercentageSource.Should().BeNull();
        result.TimeWeightedReturnPercentage.Should().BeApproximately(4.7297297297d, 0.0001d);

        result.HasOpeningBaseline.Should().BeTrue();
        result.XirrReliability.Should().Be("High");
        result.Xirr.Should().BeNull();
        result.XirrSource.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithLowReliabilityResult_SuppressesAggregateXirr()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "USD");
        var portfolioB = CreatePortfolio(userId, "USD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        const int year = 2024;
        var yearStart = new DateTime(year, 1, 1);
        var shortCoverageDate = new DateTime(year, 12, 15);

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "USD",
                StartValueSource = 100m,
                EndValueSource = 110m,
                StartValueHome = 3000m,
                EndValueHome = 3300m,
                NetContributionsSource = 0m,
                NetContributionsHome = 0m,
                XirrSource = 0.10d,
                Xirr = 0.10d,
                EarliestTransactionDateInYear = shortCoverageDate,
                TransactionCount = 1,
                CoverageStartDate = yearStart,
                CoverageDays = 17,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "Low",
                MissingPrices = []
            });

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
                EarliestTransactionDateInYear = shortCoverageDate,
                TransactionCount = 1,
                CoverageStartDate = yearStart,
                CoverageDays = 17,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "Low",
                MissingPrices = []
            });

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CalculateYearPerformanceRequest { Year = year }, CancellationToken.None);

        // Assert
        result.XirrReliability.Should().Be("Low");
        result.Xirr.Should().BeNull();
        result.XirrPercentage.Should().BeNull();
        result.XirrSource.Should().BeNull();
        result.XirrPercentageSource.Should().BeNull();
        result.ShouldDegradeReturnDisplay.Should().BeFalse();
        result.ReturnDisplayDegradeReasonCode.Should().BeNull();
        result.ReturnDisplayDegradeReasonMessage.Should().BeNull();
        result.HasOpeningBaseline.Should().BeTrue();
        result.CoverageStartDate.Should().Be(yearStart);
        result.CoverageDays.Should().Be((new DateTime(year, 12, 31) - yearStart).Days + 1);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnavailableReliabilityAndNoOpeningBaseline_ShouldExposeAggregateReturnDisplayDegradeSignal()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "TWD");
        var portfolioB = CreatePortfolio(userId, "TWD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        const int year = 2025;
        var lateTradeDate = new DateTime(year, 12, 30);

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "TWD",
                StartValueSource = 0m,
                EndValueSource = 105686.84m,
                StartValueHome = 0m,
                EndValueHome = 105686.84m,
                NetContributionsSource = 100000m,
                NetContributionsHome = 100000m,
                ModifiedDietzPercentageSource = 2070.01d,
                ModifiedDietzPercentage = 2070.01d,
                TimeWeightedReturnPercentageSource = 5.68684d,
                TimeWeightedReturnPercentage = 5.68684d,
                EarliestTransactionDateInYear = lateTradeDate,
                TransactionCount = 1,
                CoverageStartDate = lateTradeDate,
                CoverageDays = 2,
                HasOpeningBaseline = false,
                UsesPartialHistoryAssumption = true,
                XirrReliability = "Unavailable",
                MissingPrices = []
            });

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioB.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "TWD",
                StartValueSource = 0m,
                EndValueSource = 0m,
                StartValueHome = 0m,
                EndValueHome = 0m,
                NetContributionsSource = 0m,
                NetContributionsHome = 0m,
                EarliestTransactionDateInYear = lateTradeDate,
                TransactionCount = 1,
                CoverageStartDate = lateTradeDate,
                CoverageDays = 2,
                HasOpeningBaseline = false,
                UsesPartialHistoryAssumption = true,
                XirrReliability = "Unavailable",
                MissingPrices = []
            });

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CalculateYearPerformanceRequest { Year = year }, CancellationToken.None);

        // Assert
        result.XirrReliability.Should().Be("Unavailable");
        result.Xirr.Should().BeNull();
        result.XirrSource.Should().BeNull();

        result.ShouldDegradeReturnDisplay.Should().BeTrue();
        result.ReturnDisplayDegradeReasonCode.Should().Be("LOW_CONFIDENCE_NO_OPENING_BASELINE_AND_LOW_COVERAGE");
        result.ReturnDisplayDegradeReasonMessage.Should().NotBeNullOrWhiteSpace();

        result.HasOpeningBaseline.Should().BeFalse();
        result.UsesPartialHistoryAssumption.Should().BeTrue();
        result.CoverageStartDate.Should().Be(lateTradeDate);
        result.CoverageDays.Should().Be(2);

        result.StartValueSource.Should().BeNull();
        result.EndValueSource.Should().BeNull();
        result.NetContributionsSource.Should().BeNull();
        result.StartValueHome.Should().Be(0m);
        result.EndValueHome.Should().Be(105686.84m);
        result.NetContributionsHome.Should().Be(100000m);

        result.ModifiedDietzPercentageSource.Should().BeNull();
        result.ModifiedDietzPercentage.Should().BeApproximately(2070.01d, 0.01d);
        result.TimeWeightedReturnPercentageSource.Should().BeNull();
        result.TimeWeightedReturnPercentage.Should().BeApproximately(5.68684d, 0.0001d);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnavailableReliabilityAndNoOpeningBaselineButSufficientCoverage_ShouldExposeNoOpeningBaselineDegradeReason()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "TWD");
        var portfolioB = CreatePortfolio(userId, "TWD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        const int year = 2025;
        var coverageStartDate = new DateTime(year, 10, 3);

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "TWD",
                StartValueSource = 0m,
                EndValueSource = 105686.84m,
                StartValueHome = 0m,
                EndValueHome = 105686.84m,
                NetContributionsSource = 100000m,
                NetContributionsHome = 100000m,
                ModifiedDietzPercentageSource = 2070.01d,
                ModifiedDietzPercentage = 2070.01d,
                TimeWeightedReturnPercentageSource = 5.68684d,
                TimeWeightedReturnPercentage = 5.68684d,
                EarliestTransactionDateInYear = coverageStartDate,
                TransactionCount = 1,
                CoverageStartDate = coverageStartDate,
                CoverageDays = 90,
                HasOpeningBaseline = false,
                UsesPartialHistoryAssumption = true,
                XirrReliability = "Unavailable",
                MissingPrices = []
            });

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioB.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "TWD",
                StartValueSource = 0m,
                EndValueSource = 0m,
                StartValueHome = 0m,
                EndValueHome = 0m,
                NetContributionsSource = 0m,
                NetContributionsHome = 0m,
                EarliestTransactionDateInYear = coverageStartDate,
                TransactionCount = 1,
                CoverageStartDate = coverageStartDate,
                CoverageDays = 90,
                HasOpeningBaseline = false,
                UsesPartialHistoryAssumption = true,
                XirrReliability = "Unavailable",
                MissingPrices = []
            });

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CalculateYearPerformanceRequest { Year = year }, CancellationToken.None);

        // Assert
        result.XirrReliability.Should().Be("Unavailable");
        result.HasOpeningBaseline.Should().BeFalse();
        result.CoverageDays.Should().Be(90);

        result.ShouldDegradeReturnDisplay.Should().BeTrue();
        result.ReturnDisplayDegradeReasonCode.Should().Be("LOW_CONFIDENCE_NO_OPENING_BASELINE");
        result.ReturnDisplayDegradeReasonCode.Should().NotBe("LOW_CONFIDENCE_NO_OPENING_BASELINE_AND_LOW_COVERAGE");
        result.ReturnDisplayDegradeReasonMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_WithLowReliabilityAndOpeningBaselineButLowCoverage_ShouldExposeLowCoverageDegradeReason()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "USD");
        var portfolioB = CreatePortfolio(userId, "USD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        var year = DateTime.UtcNow.Year;
        var today = DateTime.UtcNow.Date;
        const int expectedCoverageDays = 30;
        var coverageStartDate = today.AddDays(-(expectedCoverageDays - 1));

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "USD",
                StartValueSource = 100m,
                EndValueSource = 110m,
                StartValueHome = 3000m,
                EndValueHome = 3300m,
                NetContributionsSource = 0m,
                NetContributionsHome = 0m,
                XirrSource = 0.10d,
                Xirr = 0.10d,
                EarliestTransactionDateInYear = coverageStartDate,
                TransactionCount = 1,
                CoverageStartDate = coverageStartDate,
                CoverageDays = expectedCoverageDays,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "Low",
                MissingPrices = []
            });

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
                EarliestTransactionDateInYear = coverageStartDate,
                TransactionCount = 1,
                CoverageStartDate = coverageStartDate,
                CoverageDays = expectedCoverageDays,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "Low",
                MissingPrices = []
            });

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CalculateYearPerformanceRequest { Year = year }, CancellationToken.None);

        // Assert
        result.XirrReliability.Should().Be("Low");
        result.HasOpeningBaseline.Should().BeTrue();
        result.CoverageDays.Should().Be(expectedCoverageDays);
        result.CoverageDays.Should().BeLessThan(90);

        result.ShouldDegradeReturnDisplay.Should().BeTrue();
        result.ReturnDisplayDegradeReasonCode.Should().Be("LOW_CONFIDENCE_LOW_COVERAGE");
        result.ReturnDisplayDegradeReasonCode.Should().NotBe("LOW_CONFIDENCE_NO_OPENING_BASELINE_AND_LOW_COVERAGE");
        result.ReturnDisplayDegradeReasonMessage.Should().NotBeNullOrWhiteSpace();
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
                CoverageStartDate = new DateTime(year, 1, 1),
                CoverageDays = 366,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
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
                CoverageStartDate = new DateTime(year, 1, 1),
                CoverageDays = 366,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                MissingPrices = []
            });

        var useCase = CreateUseCase();

        var request = new CalculateYearPerformanceRequest { Year = year };

        // Act
        var result = await useCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.TimeWeightedReturnPercentageSource.Should().BeNull();
        result.TimeWeightedReturnPercentage.Should().BeApproximately(10d, 0.0001d);

        var periodStart = new DateTime(year, 1, 1);
        var periodEnd = new DateTime(year, 12, 31);
        var totalDays = (periodEnd.Date - periodStart.Date).Days;
        var daysSinceStart = (midYear.Date - periodStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;

        var expectedModifiedDietz = (2200m - 1000m - 1000m) / (1000m + 1000m * weight);
        var expectedModifiedDietzPct = (double)(expectedModifiedDietz * 100m);

        result.ModifiedDietzPercentageSource.Should().BeNull();
        result.ModifiedDietzPercentage.Should().BeApproximately(expectedModifiedDietzPct, 0.0001d);

        result.ModifiedDietzPercentage.Should().NotBeApproximately(result.TimeWeightedReturnPercentage!.Value, 0.01d);

        result.HasOpeningBaseline.Should().BeTrue();
        result.UsesPartialHistoryAssumption.Should().BeFalse();
        result.CoverageStartDate.Should().Be(new DateTime(year, 1, 1));
        result.CoverageDays.Should().Be((periodEnd - periodStart).Days + 1);
        result.XirrReliability.Should().Be("High");
        result.Xirr.Should().BeNull();
        result.XirrPercentage.Should().BeNull();
        result.XirrSource.Should().BeNull();
        result.XirrPercentageSource.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenOnePortfolioTriggersRecentLargeInflowWarning_ShouldExposeAggregateWarning()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "TWD");
        var portfolioB = CreatePortfolio(userId, "TWD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        const int year = 2024;
        var eventDate = new DateTime(year, 12, 5);

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "TWD",
                StartValueSource = 2000m,
                EndValueSource = 4001m,
                StartValueHome = 2000m,
                EndValueHome = 4001m,
                NetContributionsSource = 2001m,
                NetContributionsHome = 2001m,
                TimeWeightedReturnPercentageSource = 10d,
                TimeWeightedReturnPercentage = 10d,
                EarliestTransactionDateInYear = eventDate,
                TransactionCount = 1,
                CoverageStartDate = new DateTime(year, 1, 1),
                CoverageDays = 366,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                HasRecentLargeInflowWarning = true,
                RecentLargeInflowWarningMessage = "近期大額資金異動可能導致資金加權報酬率短期波動。",
                MissingPrices = []
            });

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioB.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "TWD",
                StartValueSource = 0m,
                EndValueSource = 0m,
                StartValueHome = 0m,
                EndValueHome = 0m,
                NetContributionsSource = 0m,
                NetContributionsHome = 0m,
                EarliestTransactionDateInYear = eventDate,
                TransactionCount = 1,
                CoverageStartDate = new DateTime(year, 1, 1),
                CoverageDays = 366,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                HasRecentLargeInflowWarning = false,
                RecentLargeInflowWarningMessage = null,
                MissingPrices = []
            });

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CalculateYearPerformanceRequest { Year = year }, CancellationToken.None);

        // Assert
        result.HasRecentLargeInflowWarning.Should().BeTrue();
        result.RecentLargeInflowWarningMessage.Should().Be("近期大額資金異動可能導致資金加權報酬率短期波動。");
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoPortfolioTriggersRecentLargeInflowWarning_ShouldNotExposeAggregateWarning()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "USD");
        var portfolioB = CreatePortfolio(userId, "USD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        const int year = 2024;
        var eventDate = new DateTime(year, 6, 30);

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "USD",
                StartValueSource = 1000m,
                EndValueSource = 1100m,
                StartValueHome = 31000m,
                EndValueHome = 34100m,
                NetContributionsSource = 100m,
                NetContributionsHome = 3100m,
                TimeWeightedReturnPercentageSource = 10d,
                TimeWeightedReturnPercentage = 10d,
                EarliestTransactionDateInYear = eventDate,
                TransactionCount = 1,
                CoverageStartDate = new DateTime(year, 1, 1),
                CoverageDays = 366,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                HasRecentLargeInflowWarning = false,
                RecentLargeInflowWarningMessage = null,
                MissingPrices = []
            });

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
                EarliestTransactionDateInYear = eventDate,
                TransactionCount = 1,
                CoverageStartDate = new DateTime(year, 1, 1),
                CoverageDays = 366,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                HasRecentLargeInflowWarning = false,
                RecentLargeInflowWarningMessage = null,
                MissingPrices = []
            });

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CalculateYearPerformanceRequest { Year = year }, CancellationToken.None);

        // Assert
        result.HasRecentLargeInflowWarning.Should().BeFalse();
        result.RecentLargeInflowWarningMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_AggregateHistoricalTotalCostVariants_ShouldNotChangeAggregateTwrOrMd()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "TWD");
        var portfolioB = CreatePortfolio(userId, "TWD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        const int year = 2026;
        var eventDate = new DateTime(year, 6, 30);

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "TWD",
                StartValueSource = 10000m,
                EndValueSource = 12000m,
                StartValueHome = 10000m,
                EndValueHome = 12000m,
                NetContributionsSource = 0m,
                NetContributionsHome = 0m,
                TimeWeightedReturnPercentageSource = 20d,
                TimeWeightedReturnPercentage = 20d,
                XirrSource = 0.20d,
                Xirr = 0.20d,
                EarliestTransactionDateInYear = eventDate,
                TransactionCount = 1,
                CoverageStartDate = new DateTime(year, 1, 1),
                CoverageDays = 365,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                MissingPrices = []
            });

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioB.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "TWD",
                StartValueSource = 5000m,
                EndValueSource = 5000m,
                StartValueHome = 5000m,
                EndValueHome = 5000m,
                NetContributionsSource = 0m,
                NetContributionsHome = 0m,
                TimeWeightedReturnPercentageSource = 0d,
                TimeWeightedReturnPercentage = 0d,
                XirrSource = 0d,
                Xirr = 0d,
                EarliestTransactionDateInYear = eventDate,
                TransactionCount = 1,
                CoverageStartDate = new DateTime(year, 1, 1),
                CoverageDays = 365,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                MissingPrices = []
            });

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CalculateYearPerformanceRequest { Year = year }, CancellationToken.None);

        // Assert
        result.TimeWeightedReturnPercentageSource.Should().BeNull();
        result.TimeWeightedReturnPercentage.Should().BeApproximately(13.3333333333d, 0.0001d);

        result.ModifiedDietzPercentageSource.Should().BeNull();
        result.ModifiedDietzPercentage.Should().BeApproximately(13.3333333333d, 0.0001d);

        result.Xirr.Should().BeNull();
        result.XirrPercentage.Should().BeNull();
        result.XirrSource.Should().BeNull();
        result.XirrPercentageSource.Should().BeNull();

        result.HasOpeningBaseline.Should().BeTrue();
        result.UsesPartialHistoryAssumption.Should().BeFalse();
        result.XirrReliability.Should().Be("High");
    }

    [Fact]
    public async Task ExecuteAsync_AggregateUsesInternalHoldingProjectionsForBaselineValuationConsistency()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "USD");
        var portfolioB = CreatePortfolio(userId, "USD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        const int year = 2025;
        var yearStart = new DateTime(year, 1, 1);

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "USD",
                StartValueSource = 1000m,
                EndValueSource = 1100m,
                StartValueHome = 30000m,
                EndValueHome = 33000m,
                NetContributionsSource = 0m,
                NetContributionsHome = 0m,
                TimeWeightedReturnPercentageSource = 10d,
                TimeWeightedReturnPercentage = 10d,
                EarliestTransactionDateInYear = yearStart,
                TransactionCount = 0,
                CoverageStartDate = yearStart,
                CoverageDays = 365,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                MissingPrices = [],
                YearStartHoldingProjections =
                [
                    new CurrentHoldingProjectionDto
                    {
                        Ticker = "AAPL",
                        Shares = 10m,
                        CostSource = 1000m,
                        CostHome = 30000m,
                        MarketValueSource = 1000m,
                        MarketValueHome = 30000m,
                        ValuationSource = PositionValuationSource.CostBasisFallback
                    }
                ],
                YearEndHoldingProjections =
                [
                    new CurrentHoldingProjectionDto
                    {
                        Ticker = "AAPL",
                        Shares = 10m,
                        CostSource = 1000m,
                        CostHome = 30000m,
                        MarketValueSource = 1100m,
                        MarketValueHome = 33000m,
                        ValuationSource = PositionValuationSource.MarketPrice
                    }
                ],
                LedgerStartValueHome = 0m,
                LedgerEndValueHome = 0m
            });

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioB.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "USD",
                // Intentionally inconsistent top-level values: aggregate should prefer projection values below.
                StartValueSource = 200m,
                EndValueSource = 200m,
                StartValueHome = 6000m,
                EndValueHome = 6000m,
                NetContributionsSource = 0m,
                NetContributionsHome = 0m,
                TimeWeightedReturnPercentageSource = 0d,
                TimeWeightedReturnPercentage = 0d,
                EarliestTransactionDateInYear = yearStart,
                TransactionCount = 0,
                CoverageStartDate = yearStart,
                CoverageDays = 365,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                MissingPrices = [],
                YearStartHoldingProjections =
                [
                    new CurrentHoldingProjectionDto
                    {
                        Ticker = "MSFT",
                        Shares = 2m,
                        CostSource = 200m,
                        CostHome = 6000m,
                        MarketValueSource = 200m,
                        MarketValueHome = 50000m,
                        ValuationSource = PositionValuationSource.CostBasisFallback
                    }
                ],
                YearEndHoldingProjections =
                [
                    new CurrentHoldingProjectionDto
                    {
                        Ticker = "MSFT",
                        Shares = 2m,
                        CostSource = 200m,
                        CostHome = 6000m,
                        MarketValueSource = 220m,
                        MarketValueHome = 55000m,
                        ValuationSource = PositionValuationSource.MarketPrice
                    }
                ],
                LedgerStartValueHome = 3000m,
                LedgerEndValueHome = 3000m
            });

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CalculateYearPerformanceRequest { Year = year }, CancellationToken.None);

        // Assert: home canonical uses internal projection + ledger sum
        // ((30000+0) + (50000+3000) -> (33000+0) + (55000+3000)).
        result.StartValueHome.Should().Be(83000m);
        result.EndValueHome.Should().Be(91000m);
        result.TotalReturnPercentage.Should().BeApproximately(9.6385542169d, 0.0001d);

        // Product decision: aggregate source-side remains suppressed/null.
        result.SourceCurrency.Should().BeNull();
        result.StartValueSource.Should().BeNull();
        result.EndValueSource.Should().BeNull();
        result.NetContributionsSource.Should().BeNull();
        result.TotalReturnPercentageSource.Should().BeNull();
        result.ModifiedDietzPercentageSource.Should().BeNull();
        result.TimeWeightedReturnPercentageSource.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenHoldingProjectionHasUnavailableRows_ExcludesUnavailableFromAggregateHomeValuation()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "TWD");
        var portfolioB = CreatePortfolio(userId, "TWD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        const int year = 2025;
        var yearStart = new DateTime(year, 1, 1);

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioA.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "TWD",
                StartValueHome = 1000m,
                EndValueHome = 1200m,
                NetContributionsHome = 0m,
                EarliestTransactionDateInYear = yearStart,
                CoverageStartDate = yearStart,
                CoverageDays = 365,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                YearStartHoldingProjections =
                [
                    new CurrentHoldingProjectionDto
                    {
                        Ticker = "AAPL",
                        Shares = 1m,
                        CostSource = 1000m,
                        CostHome = 1000m,
                        MarketValueSource = 1000m,
                        MarketValueHome = 1000m,
                        ValuationSource = PositionValuationSource.CostBasisFallback
                    }
                ],
                YearEndHoldingProjections =
                [
                    new CurrentHoldingProjectionDto
                    {
                        Ticker = "AAPL",
                        Shares = 1m,
                        CostSource = 1000m,
                        CostHome = 1000m,
                        MarketValueSource = 1200m,
                        MarketValueHome = 1200m,
                        ValuationSource = PositionValuationSource.MarketPrice
                    }
                ]
            });

        _historicalPerformanceServiceMock
            .Setup(x => x.CalculateYearPerformanceAsync(portfolioB.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearPerformanceDto
            {
                Year = year,
                SourceCurrency = "TWD",
                StartValueHome = 9999m,
                EndValueHome = 9999m,
                NetContributionsHome = 0m,
                EarliestTransactionDateInYear = yearStart,
                CoverageStartDate = yearStart,
                CoverageDays = 365,
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                YearStartHoldingProjections =
                [
                    new CurrentHoldingProjectionDto
                    {
                        Ticker = "MISSING",
                        Shares = 10m,
                        CostSource = 9999m,
                        CostHome = 9999m,
                        MarketValueSource = 0m,
                        MarketValueHome = 0m,
                        ValuationSource = PositionValuationSource.Unavailable
                    }
                ],
                YearEndHoldingProjections =
                [
                    new CurrentHoldingProjectionDto
                    {
                        Ticker = "MISSING",
                        Shares = 10m,
                        CostSource = 9999m,
                        CostHome = 9999m,
                        MarketValueSource = 0m,
                        MarketValueHome = 0m,
                        ValuationSource = PositionValuationSource.Unavailable
                    }
                ]
            });

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CalculateYearPerformanceRequest { Year = year }, CancellationToken.None);

        // Assert
        result.StartValueHome.Should().Be(1000m);
        result.EndValueHome.Should().Be(1200m);
        result.TotalReturnPercentage.Should().BeApproximately(20d, 0.0001d);
        result.SourceCurrency.Should().BeNull();
        result.StartValueSource.Should().BeNull();
        result.EndValueSource.Should().BeNull();
        result.NetContributionsSource.Should().BeNull();
        result.TotalReturnPercentageSource.Should().BeNull();
        result.ModifiedDietzPercentageSource.Should().BeNull();
        result.TimeWeightedReturnPercentageSource.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_CurrentYearYtdModifiedDietz_UsesActualElapsedDaysFromYearStartInsteadOfFixed365()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolioA = CreatePortfolio(userId, "USD");
        var portfolioB = CreatePortfolio(userId, "USD");

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        var year = DateTime.UtcNow.Year;
        var yearStart = new DateTime(year, 1, 1);
        var ytdEnd = DateTime.UtcNow.Date;
        var flowDate = ytdEnd;

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
                EarliestTransactionDateInYear = flowDate,
                TransactionCount = 1,
                CoverageStartDate = yearStart,
                CoverageDays = Math.Max(1, (ytdEnd - yearStart).Days + 1),
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                MissingPrices = []
            });

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
                EarliestTransactionDateInYear = flowDate,
                TransactionCount = 1,
                CoverageStartDate = yearStart,
                CoverageDays = Math.Max(1, (ytdEnd - yearStart).Days + 1),
                HasOpeningBaseline = true,
                UsesPartialHistoryAssumption = false,
                XirrReliability = "High",
                MissingPrices = []
            });

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(new CalculateYearPerformanceRequest { Year = year }, CancellationToken.None);

        // Assert
        var totalDaysActual = (ytdEnd - yearStart).Days;
        var daysSinceStart = (flowDate.Date - yearStart.Date).Days;

        if (totalDaysActual <= 0)
        {
            result.ModifiedDietzPercentageSource.Should().BeNull();
            result.ModifiedDietzPercentage.Should().BeNull();
            return;
        }

        var actualWeight = (totalDaysActual - daysSinceStart) / (decimal)totalDaysActual;
        var expectedActual = (2200m - 1000m - 1000m) / (1000m + 1000m * actualWeight);
        var expectedActualPct = (double)(expectedActual * 100m);

        result.ModifiedDietzPercentageSource.Should().BeNull();
        result.ModifiedDietzPercentage.Should().BeApproximately(expectedActualPct, 0.0001d);

        result.HasRecentLargeInflowWarning.Should().BeFalse();
        result.RecentLargeInflowWarningMessage.Should().BeNull();

        var fixed365Weight = (365m - daysSinceStart) / 365m;
        var expectedFixed365 = (2200m - 1000m - 1000m) / (1000m + 1000m * fixed365Weight);
        var expectedFixed365Pct = (double)(expectedFixed365 * 100m);

        if (totalDaysActual != 365)
        {
            result.ModifiedDietzPercentage!.Value.Should().NotBeApproximately(expectedFixed365Pct, 0.0001d);
        }
    }

    [Fact]
    public async Task ExecuteAsync_AggregateYearPerformance100Portfolios_PerformanceBaseline_ShouldEmitTimingMetrics()
    {
        // Arrange
        const int benchmarkPortfolioCount = 100;
        const int measuredRuns = 3;
        const int year = 2025;

        SetupAggregatePerformanceBenchmarkScenario(benchmarkPortfolioCount, year);
        var useCase = CreateUseCase();
        var request = new CalculateYearPerformanceRequest { Year = year };

        // Warm-up run to reduce one-time JIT effects.
        var warmupResult = await useCase.ExecuteAsync(request, CancellationToken.None);
        AssertAggregatePerformanceResult(warmupResult, benchmarkPortfolioCount, year);

        var measurements = new List<TimeSpan>(capacity: measuredRuns);

        // Act
        for (var i = 0; i < measuredRuns; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await useCase.ExecuteAsync(request, CancellationToken.None);
            stopwatch.Stop();

            AssertAggregatePerformanceResult(result, benchmarkPortfolioCount, year);
            measurements.Add(stopwatch.Elapsed);
        }

        var medianElapsed = GetMedian(measurements);
        var minElapsed = measurements.Min();
        var maxElapsed = measurements.Max();
        var sampleSeries = string.Join(", ", measurements.Select(value => value.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)));

        Console.WriteLine(
            "[PerfBaseline][CalculateAggregateYearPerformance] portfolios={0}; runs={1}; minMs={2:F2}; medianMs={3:F2}; maxMs={4:F2}; samplesMs=[{5}]",
            benchmarkPortfolioCount,
            measurements.Count,
            minElapsed.TotalMilliseconds,
            medianElapsed.TotalMilliseconds,
            maxElapsed.TotalMilliseconds,
            sampleSeries);

        // Assert
        medianElapsed.Should().BeGreaterThan(TimeSpan.Zero);
    }

    private void SetupAggregatePerformanceBenchmarkScenario(int portfolioCount, int year)
    {
        var userId = Guid.NewGuid();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);

        var portfolios = Enumerable.Range(1, portfolioCount)
            .Select(_ => CreatePortfolio(userId, "USD"))
            .ToList();

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolios);

        foreach (var (portfolio, index) in portfolios.Select((value, i) => (value, i)))
        {
            var seed = index + 1;
            var startValue = 10000m + seed * 17m;
            var contribution = 2000m + seed * 3m;
            var endValue = startValue + contribution + 500m + seed;
            var eventDate = new DateTime(year, (seed % 12) + 1, (seed % 28) + 1);

            _historicalPerformanceServiceMock
                .Setup(x => x.CalculateYearPerformanceAsync(portfolio.Id, It.IsAny<CalculateYearPerformanceRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new YearPerformanceDto
                {
                    Year = year,
                    SourceCurrency = "USD",
                    StartValueSource = startValue,
                    EndValueSource = endValue,
                    StartValueHome = startValue * 31m,
                    EndValueHome = endValue * 31m,
                    NetContributionsSource = contribution,
                    NetContributionsHome = contribution * 31m,
                    TimeWeightedReturnPercentageSource = 8d + (seed % 5),
                    TimeWeightedReturnPercentage = 8d + (seed % 5),
                    EarliestTransactionDateInYear = eventDate,
                    TransactionCount = 5,
                    CashFlowCount = 0,
                    CoverageStartDate = new DateTime(year, 1, 1),
                    CoverageDays = 365,
                    HasOpeningBaseline = true,
                    UsesPartialHistoryAssumption = false,
                    XirrReliability = "High",
                    MissingPrices = []
                });
        }
    }

    private static void AssertAggregatePerformanceResult(YearPerformanceDto result, int expectedPortfolioCount, int expectedYear)
    {
        result.Year.Should().Be(expectedYear);
        result.TransactionCount.Should().Be(expectedPortfolioCount * 5);
        result.CashFlowCount.Should().Be(0);
        result.MissingPrices.Should().BeEmpty();
        result.SourceCurrency.Should().BeNull();
        result.XirrReliability.Should().Be("High");
        result.Xirr.Should().BeNull();
        result.XirrPercentage.Should().BeNull();
        result.XirrSource.Should().BeNull();
        result.XirrPercentageSource.Should().BeNull();
        result.StartValueSource.Should().BeNull();
        result.EndValueSource.Should().BeNull();
        result.NetContributionsSource.Should().BeNull();
        result.TimeWeightedReturnPercentageSource.Should().BeNull();
    }

    private static TimeSpan GetMedian(IReadOnlyCollection<TimeSpan> values)
    {
        values.Should().NotBeEmpty();

        var ordered = values
            .OrderBy(value => value)
            .ToArray();

        return ordered[ordered.Length / 2];
    }

    private CalculateAggregateYearPerformanceUseCase CreateUseCase()
    {
        return new CalculateAggregateYearPerformanceUseCase(
            _portfolioRepositoryMock.Object,
            _historicalPerformanceServiceMock.Object,
            _currentUserServiceMock.Object,
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
