using FluentAssertions;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.Performance;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using Moq;

namespace InvestmentTracker.Application.Tests.UseCases;

public class GetAggregateAvailableYearsUseCaseTests
{
    private readonly Mock<IPortfolioRepository> _portfolioRepositoryMock = new();
    private readonly Mock<IStockTransactionRepository> _transactionRepositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public async Task ExecuteAsync_NoPortfolios_ReturnsEmptyAvailableYearsDto()
    {
        // Arrange
        _currentUserServiceMock
            .Setup(x => x.UserId)
            .Returns(_userId);

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Portfolio>());

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Years.Should().BeEmpty();
        result.EarliestYear.Should().BeNull();
        result.CurrentYear.Should().Be(DateTime.UtcNow.Year);

        _transactionRepositoryMock.Verify(
            x => x.GetByPortfolioIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PortfoliosWithoutTransactions_ReturnsEmptyAvailableYearsDto()
    {
        // Arrange
        _currentUserServiceMock
            .Setup(x => x.UserId)
            .Returns(_userId);

        var portfolioA = CreatePortfolio();
        var portfolioB = CreatePortfolio();

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        _transactionRepositoryMock
            .Setup(x => x.GetByPortfolioIdAsync(portfolioA.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTransaction>());

        _transactionRepositoryMock
            .Setup(x => x.GetByPortfolioIdAsync(portfolioB.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTransaction>());

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Years.Should().BeEmpty();
        result.EarliestYear.Should().BeNull();
        result.CurrentYear.Should().Be(DateTime.UtcNow.Year);

        _transactionRepositoryMock.Verify(
            x => x.GetByPortfolioIdAsync(portfolioA.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        _transactionRepositoryMock.Verify(
            x => x.GetByPortfolioIdAsync(portfolioB.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PortfoliosWithTransactions_ReturnsDescendingContinuousYearsFromEarliestYear()
    {
        // Arrange
        _currentUserServiceMock
            .Setup(x => x.UserId)
            .Returns(_userId);

        var portfolioA = CreatePortfolio();
        var portfolioB = CreatePortfolio();

        _portfolioRepositoryMock
            .Setup(x => x.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([portfolioA, portfolioB]);

        var txA1 = CreateTransaction(portfolioA.Id, new DateTime(2022, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        var txADeleted = CreateTransaction(portfolioA.Id, new DateTime(2021, 5, 10, 0, 0, 0, DateTimeKind.Utc));
        txADeleted.MarkAsDeleted();

        var txB1 = CreateTransaction(portfolioB.Id, new DateTime(2024, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        _transactionRepositoryMock
            .Setup(x => x.GetByPortfolioIdAsync(portfolioA.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([txA1, txADeleted]);

        _transactionRepositoryMock
            .Setup(x => x.GetByPortfolioIdAsync(portfolioB.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([txB1]);

        var useCase = CreateUseCase();

        // Act
        var result = await useCase.ExecuteAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.EarliestYear.Should().Be(2022);

        var currentYear = DateTime.UtcNow.Year;
        var expectedYears = Enumerable.Range(2022, currentYear - 2022 + 1)
            .OrderDescending()
            .ToList();

        result.CurrentYear.Should().Be(currentYear);
        result.Years.Should().Equal(expectedYears);
    }

    private GetAggregateAvailableYearsUseCase CreateUseCase()
    {
        return new GetAggregateAvailableYearsUseCase(
            _portfolioRepositoryMock.Object,
            _transactionRepositoryMock.Object,
            _currentUserServiceMock.Object);
    }

    private Portfolio CreatePortfolio()
    {
        return new Portfolio(
            userId: _userId,
            boundCurrencyLedgerId: Guid.NewGuid(),
            baseCurrency: "USD",
            homeCurrency: "TWD",
            displayName: "Test Portfolio");
    }

    private static StockTransaction CreateTransaction(Guid portfolioId, DateTime date)
    {
        return new StockTransaction(
            portfolioId: portfolioId,
            transactionDate: date,
            ticker: "VT",
            transactionType: Domain.Enums.TransactionType.Buy,
            shares: 1m,
            pricePerShare: 100m,
            exchangeRate: 30m,
            fees: 0m);
    }
}
