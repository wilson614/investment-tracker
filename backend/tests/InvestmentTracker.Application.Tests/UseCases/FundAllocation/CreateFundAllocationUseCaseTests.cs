using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.FundAllocation;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Moq;

namespace InvestmentTracker.Application.Tests.UseCases.FundAllocation;

public class CreateFundAllocationUseCaseTests
{
    private readonly Mock<IFundAllocationRepository> _fundAllocationRepositoryMock;
    private readonly Mock<IBankAccountRepository> _bankAccountRepositoryMock;
    private readonly Mock<IYahooHistoricalPriceService> _yahooHistoricalPriceServiceMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly TotalAssetsService _totalAssetsService;

    private readonly CreateFundAllocationUseCase _useCase;

    private readonly Guid _userId = Guid.NewGuid();

    public CreateFundAllocationUseCaseTests()
    {
        _fundAllocationRepositoryMock = new Mock<IFundAllocationRepository>();
        _bankAccountRepositoryMock = new Mock<IBankAccountRepository>();
        _yahooHistoricalPriceServiceMock = new Mock<IYahooHistoricalPriceService>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _totalAssetsService = new TotalAssetsService(new InterestEstimationService());

        _currentUserServiceMock
            .Setup(x => x.UserId)
            .Returns(_userId);

        _useCase = new CreateFundAllocationUseCase(
            _fundAllocationRepositoryMock.Object,
            _bankAccountRepositoryMock.Object,
            _yahooHistoricalPriceServiceMock.Object,
            _totalAssetsService,
            _currentUserServiceMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_TotalAllocationExceedsBankAssets_ShouldThrowBusinessRuleException()
    {
        // Arrange
        var existingAllocation = new global::InvestmentTracker.Domain.Entities.FundAllocation(
            userId: _userId,
            purpose: "緊急預備金",
            amount: 700_000m,
            note: "Existing allocation");

        _fundAllocationRepositoryMock
            .Setup(x => x.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<global::InvestmentTracker.Domain.Entities.FundAllocation> { existingAllocation });

        var bankAccount = new BankAccount(
            userId: _userId,
            bankName: "Test Bank",
            totalAssets: 1_000_000m,
            interestRate: 1.5m,
            interestCap: 100_000m,
            note: null,
            currency: "TWD");

        _bankAccountRepositoryMock
            .Setup(x => x.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BankAccount> { bankAccount });

        var request = new CreateFundAllocationRequest
        {
            Purpose = "家庭存款",
            Amount = 400_000m,
            Note = "Should exceed"
        };

        // Act
        var action = async () => await _useCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        var exception = await action.Should().ThrowAsync<BusinessRuleException>();
        exception.Which.Message.Should().Be("資金配置總額不得超過銀行資產總額。");

        _fundAllocationRepositoryMock.Verify(
            x => x.AddAsync(It.IsAny<global::InvestmentTracker.Domain.Entities.FundAllocation>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_TotalAllocationWithinBankAssets_ShouldCreateAllocation()
    {
        // Arrange
        var existingAllocation = new global::InvestmentTracker.Domain.Entities.FundAllocation(
            userId: _userId,
            purpose: "緊急預備金",
            amount: 600_000m,
            note: "Existing allocation");

        _fundAllocationRepositoryMock
            .Setup(x => x.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<global::InvestmentTracker.Domain.Entities.FundAllocation> { existingAllocation });

        var bankAccount = new BankAccount(
            userId: _userId,
            bankName: "Test Bank",
            totalAssets: 1_000_000m,
            interestRate: 1.5m,
            interestCap: 100_000m,
            note: null,
            currency: "TWD");

        _bankAccountRepositoryMock
            .Setup(x => x.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BankAccount> { bankAccount });

        _fundAllocationRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<global::InvestmentTracker.Domain.Entities.FundAllocation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((global::InvestmentTracker.Domain.Entities.FundAllocation allocation, CancellationToken _) => allocation);

        var request = new CreateFundAllocationRequest
        {
            Purpose = "旅遊基金",
            Amount = 300_000m,
            Note = "Valid allocation"
        };

        // Act
        var response = await _useCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Purpose.Should().Be("旅遊基金");
        response.Amount.Should().Be(300_000m);
        response.Note.Should().Be("Valid allocation");

        _fundAllocationRepositoryMock.Verify(
            x => x.AddAsync(It.IsAny<global::InvestmentTracker.Domain.Entities.FundAllocation>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
