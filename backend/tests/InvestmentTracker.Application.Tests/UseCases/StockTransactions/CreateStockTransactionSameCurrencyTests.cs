using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.StockTransactions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Moq;
using DomainCurrencyLedger = InvestmentTracker.Domain.Entities.CurrencyLedger;
using DomainPortfolio = InvestmentTracker.Domain.Entities.Portfolio;

namespace InvestmentTracker.Application.Tests.UseCases.StockTransactions;

public class CreateStockTransactionSameCurrencyTests
{
    [Fact]
    public async Task ExecuteAsync_WhenLedgerCurrencyMatchesHomeCurrencyAndLifoRateIsZero_ShouldUseOneAndSkipFxLookup()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var boundLedgerId = Guid.NewGuid();
        var transactionDate = DateTime.UtcNow.Date.AddDays(-2);

        var portfolio = new DomainPortfolio(
            userId: userId,
            boundCurrencyLedgerId: boundLedgerId,
            baseCurrency: "TWD",
            homeCurrency: "TWD",
            displayName: "Same Currency Portfolio");
        typeof(DomainPortfolio).GetProperty(nameof(DomainPortfolio.Id))!.SetValue(portfolio, portfolioId);

        var boundLedger = new DomainCurrencyLedger(
            userId: userId,
            currencyCode: "USD",
            name: "USD Ledger",
            homeCurrency: "USD");
        typeof(DomainCurrencyLedger).GetProperty(nameof(DomainCurrencyLedger.Id))!.SetValue(boundLedger, boundLedgerId);

        var seedLedgerTransactions = new List<CurrencyTransaction>
        {
            new(
                currencyLedgerId: boundLedgerId,
                transactionDate: transactionDate.AddDays(-1),
                transactionType: CurrencyTransactionType.Deposit,
                foreignAmount: 1000m,
                homeAmount: 1000m,
                exchangeRate: 1.0m,
                notes: "seed")
        };

        var transactionRepository = new Mock<IStockTransactionRepository>();
        var portfolioRepository = new Mock<IPortfolioRepository>();
        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>();
        var currencyTransactionRepository = new Mock<ICurrencyTransactionRepository>();
        var currentUserService = new Mock<ICurrentUserService>();
        var txDateFxService = new Mock<ITransactionDateExchangeRateService>();
        var monthlySnapshotService = new Mock<IMonthlySnapshotService>();
        var txSnapshotService = new Mock<ITransactionPortfolioSnapshotService>();
        var transactionManager = new Mock<IAppDbTransactionManager>();
        var appTransaction = new Mock<IAppDbTransaction>();

        StockTransaction? createdTransaction = null;

        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        currencyLedgerRepository
            .Setup(x => x.GetByIdAsync(boundLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(boundLedger);

        currencyTransactionRepository
            .Setup(x => x.GetByLedgerIdOrderedAsync(boundLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedLedgerTransactions);

        currencyTransactionRepository
            .Setup(x => x.AddAsync(It.IsAny<CurrencyTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyTransaction tx, CancellationToken _) => tx);

        transactionRepository
            .Setup(x => x.AddAsync(It.IsAny<StockTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<StockTransaction, CancellationToken>((tx, _) => createdTransaction = tx)
            .ReturnsAsync((StockTransaction tx, CancellationToken _) => tx);

        currentUserService
            .SetupGet(x => x.UserId)
            .Returns(userId);

        monthlySnapshotService
            .Setup(x => x.InvalidateFromMonthAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        txSnapshotService
            .Setup(x => x.UpsertSnapshotAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        transactionManager
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(appTransaction.Object);

        appTransaction
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        appTransaction
            .Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var useCase = new CreateStockTransactionUseCase(
            transactionRepository.Object,
            portfolioRepository.Object,
            currencyLedgerRepository.Object,
            currencyTransactionRepository.Object,
            new PortfolioCalculator(),
            new CurrencyLedgerService(),
            currentUserService.Object,
            txDateFxService.Object,
            monthlySnapshotService.Object,
            txSnapshotService.Object,
            transactionManager.Object);

        var request = new CreateStockTransactionRequest
        {
            PortfolioId = portfolioId,
            TransactionDate = transactionDate,
            Ticker = "AAPL",
            TransactionType = TransactionType.Buy,
            Shares = 10m,
            PricePerShare = 50m,
            Fees = 5m,
            Currency = Currency.USD
        };

        // Act
        var result = await useCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.ExchangeRate.Should().Be(1.0m);
        createdTransaction.Should().NotBeNull();
        createdTransaction!.ExchangeRate.Should().Be(1.0m);

        txDateFxService.Verify(
            x => x.GetOrFetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPortfolioBaseCurrencyMatchesHomeCurrencyButLedgerCurrencyDiffers_ShouldUseLedgerHomeFxPair()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var boundLedgerId = Guid.NewGuid();
        var transactionDate = DateTime.UtcNow.Date.AddDays(-2);

        var portfolio = new DomainPortfolio(
            userId: userId,
            boundCurrencyLedgerId: boundLedgerId,
            baseCurrency: "USD",
            homeCurrency: "USD",
            displayName: "Ledger Fx Portfolio");
        typeof(DomainPortfolio).GetProperty(nameof(DomainPortfolio.Id))!.SetValue(portfolio, portfolioId);

        var boundLedger = new DomainCurrencyLedger(
            userId: userId,
            currencyCode: "EUR",
            name: "EUR Ledger",
            homeCurrency: "USD");
        typeof(DomainCurrencyLedger).GetProperty(nameof(DomainCurrencyLedger.Id))!.SetValue(boundLedger, boundLedgerId);

        var transactionRepository = new Mock<IStockTransactionRepository>();
        var portfolioRepository = new Mock<IPortfolioRepository>();
        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>();
        var currencyTransactionRepository = new Mock<ICurrencyTransactionRepository>();
        var currentUserService = new Mock<ICurrentUserService>();
        var txDateFxService = new Mock<ITransactionDateExchangeRateService>();
        var monthlySnapshotService = new Mock<IMonthlySnapshotService>();
        var txSnapshotService = new Mock<ITransactionPortfolioSnapshotService>();
        var transactionManager = new Mock<IAppDbTransactionManager>();
        var appTransaction = new Mock<IAppDbTransaction>();

        StockTransaction? createdTransaction = null;

        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        currencyLedgerRepository
            .Setup(x => x.GetByIdAsync(boundLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(boundLedger);

        currencyTransactionRepository
            .Setup(x => x.GetByLedgerIdOrderedAsync(boundLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        currencyTransactionRepository
            .Setup(x => x.AddAsync(It.IsAny<CurrencyTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyTransaction tx, CancellationToken _) => tx);

        transactionRepository
            .Setup(x => x.AddAsync(It.IsAny<StockTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<StockTransaction, CancellationToken>((tx, _) => createdTransaction = tx)
            .ReturnsAsync((StockTransaction tx, CancellationToken _) => tx);

        currentUserService
            .SetupGet(x => x.UserId)
            .Returns(userId);

        txDateFxService
            .Setup(x => x.GetOrFetchAsync("EUR", "USD", transactionDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionDateExchangeRateResult
            {
                Rate = 1.25m,
                CurrencyPair = "EURUSD",
                RequestedDate = transactionDate,
                ActualDate = transactionDate,
                Source = "test",
                FromCache = true
            });

        monthlySnapshotService
            .Setup(x => x.InvalidateFromMonthAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        txSnapshotService
            .Setup(x => x.UpsertSnapshotAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        transactionManager
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(appTransaction.Object);

        appTransaction
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        appTransaction
            .Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var useCase = new CreateStockTransactionUseCase(
            transactionRepository.Object,
            portfolioRepository.Object,
            currencyLedgerRepository.Object,
            currencyTransactionRepository.Object,
            new PortfolioCalculator(),
            new CurrencyLedgerService(),
            currentUserService.Object,
            txDateFxService.Object,
            monthlySnapshotService.Object,
            txSnapshotService.Object,
            transactionManager.Object);

        var request = new CreateStockTransactionRequest
        {
            PortfolioId = portfolioId,
            TransactionDate = transactionDate,
            Ticker = "SAP",
            TransactionType = TransactionType.Buy,
            Shares = 2m,
            PricePerShare = 100m,
            Fees = 0m,
            Market = StockMarket.EU,
            Currency = Currency.EUR,
            BalanceAction = BalanceAction.Margin
        };

        // Act
        var result = await useCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.ExchangeRate.Should().Be(1.25m);
        createdTransaction.Should().NotBeNull();
        createdTransaction!.ExchangeRate.Should().Be(1.25m);

        txDateFxService.Verify(
            x => x.GetOrFetchAsync("EUR", "USD", transactionDate, It.IsAny<CancellationToken>()),
            Times.Once);
        txDateFxService.Verify(
            x => x.GetOrFetchAsync("USD", "USD", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBuyUsesTopUp_ShouldPersistLinkedSpendBeforeWritingTopUpSnapshot()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var boundLedgerId = Guid.NewGuid();
        var transactionDate = DateTime.UtcNow.Date.AddDays(-2);

        var portfolio = new DomainPortfolio(
            userId: userId,
            boundCurrencyLedgerId: boundLedgerId,
            baseCurrency: "USD",
            homeCurrency: "USD",
            displayName: "Same Currency Portfolio");
        typeof(DomainPortfolio).GetProperty(nameof(DomainPortfolio.Id))!.SetValue(portfolio, portfolioId);

        var boundLedger = new DomainCurrencyLedger(
            userId: userId,
            currencyCode: "USD",
            name: "USD Ledger",
            homeCurrency: "USD");
        typeof(DomainCurrencyLedger).GetProperty(nameof(DomainCurrencyLedger.Id))!.SetValue(boundLedger, boundLedgerId);

        var seedLedgerTransactions = new List<CurrencyTransaction>
        {
            new(
                currencyLedgerId: boundLedgerId,
                transactionDate: transactionDate.AddDays(-1),
                transactionType: CurrencyTransactionType.Deposit,
                foreignAmount: 1000m,
                homeAmount: 1000m,
                exchangeRate: 1.0m,
                notes: "seed")
        };

        var eventLog = new List<string>();
        var addedCurrencyTransactions = new List<CurrencyTransaction>();
        var transactionRepository = new Mock<IStockTransactionRepository>();
        var portfolioRepository = new Mock<IPortfolioRepository>();
        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>();
        var currencyTransactionRepository = new Mock<ICurrencyTransactionRepository>();
        var currentUserService = new Mock<ICurrentUserService>();
        var txDateFxService = new Mock<ITransactionDateExchangeRateService>();
        var monthlySnapshotService = new Mock<IMonthlySnapshotService>();
        var txSnapshotService = new Mock<ITransactionPortfolioSnapshotService>();
        var transactionManager = new Mock<IAppDbTransactionManager>();
        var appTransaction = new Mock<IAppDbTransaction>();

        StockTransaction? createdTransaction = null;

        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        currencyLedgerRepository
            .Setup(x => x.GetByIdAsync(boundLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(boundLedger);

        currencyTransactionRepository
            .Setup(x => x.GetByLedgerIdOrderedAsync(boundLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, CancellationToken _) => seedLedgerTransactions.OrderBy(tx => tx.TransactionDate).ToList());

        currencyTransactionRepository
            .Setup(x => x.AddAsync(It.IsAny<CurrencyTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<CurrencyTransaction, CancellationToken>((tx, _) =>
            {
                seedLedgerTransactions.Add(tx);
                addedCurrencyTransactions.Add(tx);
                eventLog.Add($"currency:{tx.TransactionType}:{tx.Id}");
            })
            .ReturnsAsync((CurrencyTransaction tx, CancellationToken _) => tx);

        transactionRepository
            .Setup(x => x.AddAsync(It.IsAny<StockTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<StockTransaction, CancellationToken>((tx, _) => createdTransaction = tx)
            .ReturnsAsync((StockTransaction tx, CancellationToken _) => tx);

        currentUserService
            .SetupGet(x => x.UserId)
            .Returns(userId);

        monthlySnapshotService
            .Setup(x => x.InvalidateFromMonthAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        txSnapshotService
            .Setup(x => x.UpsertSnapshotAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, DateTime, CancellationToken>((_, transactionId, _, _) => eventLog.Add($"snapshot:{transactionId}"))
            .Returns(Task.CompletedTask);

        transactionManager
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(appTransaction.Object);

        appTransaction
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        appTransaction
            .Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var useCase = new CreateStockTransactionUseCase(
            transactionRepository.Object,
            portfolioRepository.Object,
            currencyLedgerRepository.Object,
            currencyTransactionRepository.Object,
            new PortfolioCalculator(),
            new CurrencyLedgerService(),
            currentUserService.Object,
            txDateFxService.Object,
            monthlySnapshotService.Object,
            txSnapshotService.Object,
            transactionManager.Object);

        var request = new CreateStockTransactionRequest
        {
            PortfolioId = portfolioId,
            TransactionDate = transactionDate,
            Ticker = "AAPL",
            TransactionType = TransactionType.Buy,
            Shares = 30m,
            PricePerShare = 50m,
            Fees = 5m,
            Currency = Currency.USD,
            BalanceAction = BalanceAction.TopUp,
            TopUpTransactionType = CurrencyTransactionType.Deposit
        };

        // Act
        await useCase.ExecuteAsync(request, CancellationToken.None);

        // Assert
        createdTransaction.Should().NotBeNull();

        var topUpTx = addedCurrencyTransactions.Should().ContainSingle(
            tx => tx.TransactionType == CurrencyTransactionType.Deposit && tx.RelatedStockTransactionId == createdTransaction!.Id).Subject;
        var linkedSpendTx = addedCurrencyTransactions.Should().ContainSingle(
            tx => tx.TransactionType == CurrencyTransactionType.Spend && tx.RelatedStockTransactionId == createdTransaction!.Id).Subject;

        eventLog.IndexOf($"currency:{topUpTx.TransactionType}:{topUpTx.Id}").Should().BeLessThan(
            eventLog.IndexOf($"currency:{linkedSpendTx.TransactionType}:{linkedSpendTx.Id}"));
        eventLog.IndexOf($"currency:{linkedSpendTx.TransactionType}:{linkedSpendTx.Id}").Should().BeLessThan(
            eventLog.IndexOf($"snapshot:{topUpTx.Id}"),
            "TopUp snapshot 應在同日連動 Spend 寫入後再建立");
        eventLog.IndexOf($"snapshot:{topUpTx.Id}").Should().BeLessThan(
            eventLog.IndexOf($"snapshot:{createdTransaction.Id}"));
    }
}
