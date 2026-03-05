using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using InvestmentTracker.API.Middleware;
using InvestmentTracker.API.Tests.Integration;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InvestmentTracker.API.Tests.Controllers;

/// <summary>
/// Contract tests for stock import preview/execute endpoints.
/// </summary>
public class StockTransactionsImportControllerTests(CustomWebApplicationFactory factory)
    : IntegrationTestBase(factory)
{
    private readonly CustomWebApplicationFactory _factory = factory;

    private const string PreviewEndpoint = "/api/stocktransactions/import/preview";
    private const string ExecuteEndpoint = "/api/stocktransactions/import/execute";
    private const string StatusEndpointPrefix = "/api/stocktransactions/import/status";
    private const string SampleBrokerStatementFixtureFileName = "SampleBrokerStatement.csv";
    private const string SourceBrokerStatementFixtureAbsolutePath = "/workspaces/InvestmentTracker/證券app匯出範例.csv";

    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: true)
        }
    };

    private static readonly IReadOnlyDictionary<string, string> TwSecurityNameMappings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["融程電"] = "3416",
            ["瑞軒"] = "2489",
            ["鴻海"] = "2317",
            ["玉山金"] = "2884",
            ["緯軟"] = "4953",
            ["金寶"] = "2312",
            ["邁達特"] = "6112",
            ["中石化"] = "1314",
            ["中工"] = "2515"
        };

    private sealed record SampleBrokerStatementFixture(string CsvContent, int DataRowCount);

    [Fact]
    public async Task RegisterPreviewExecuteAndPerformance_UsingSampleCsv_ShouldProduceValidEndToEndData()
    {
        // Arrange
        var registerRequest = new RegisterRequest
        {
            Email = $"import-e2e-{Guid.NewGuid():N}@example.com",
            Password = "Password123!",
            DisplayName = "Import E2E"
        };

        var registerResponse = await Client.PostAsJsonAsync("/api/auth/register", registerRequest);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        auth.Should().NotBeNull();
        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        Factory.TestUserId = auth.User.Id;

        var portfoliosResponse = await Client.GetAsync("/api/portfolios");
        portfoliosResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var portfolios = await portfoliosResponse.Content.ReadFromJsonAsync<List<PortfolioDto>>();
        portfolios.Should().NotBeNullOrEmpty();

        var twdPortfolio = portfolios!
            .SingleOrDefault(p => string.Equals(p.BaseCurrency, "TWD", StringComparison.OrdinalIgnoreCase));
        twdPortfolio.Should().NotBeNull();

        var sampleFixture = await LoadSampleBrokerStatementFixtureAsync();

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = twdPortfolio!.Id,
            CsvContent = sampleFixture.CsvContent,
            SelectedFormat = "broker_statement"
        };

        // Act 1: preview
        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);

        // Assert preview
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewDto = await previewResponse.Content.ReadFromJsonAsync<StockImportPreviewResponseDto>();
        previewDto.Should().NotBeNull();
        previewDto!.SessionId.Should().NotBe(Guid.Empty);

        var previewSummary = previewDto.Summary;
        previewSummary.TotalRows.Should().Be(sampleFixture.DataRowCount);
        previewSummary.TotalRows.Should().Be(
            previewSummary.ValidRows + previewSummary.RequiresActionRows + previewSummary.InvalidRows);

        previewDto.Rows.Should().HaveCount(sampleFixture.DataRowCount);

        var executeRows = previewDto.Rows
            .Where(row => !string.Equals(row.Status, "invalid", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(row.ConfirmedTradeSide))
            .Select(row =>
            {
                var resolvedTicker = ResolveTickerForExecute(row);
                return new ExecuteStockImportRowRequest
                {
                    RowNumber = row.RowNumber,
                    Ticker = resolvedTicker,
                    ConfirmedTradeSide = row.ConfirmedTradeSide,
                    Exclude = string.IsNullOrWhiteSpace(resolvedTicker)
                };
            })
            .ToList();

        executeRows.Should().Contain(row => !row.Exclude);

        var excludedRows = executeRows.Count(row => row.Exclude);
        var excludedRatio = executeRows.Count == 0 ? 0m : (decimal)excludedRows / executeRows.Count;
        excludedRatio.Should().BeLessThanOrEqualTo(0.2m,
            "sample CSV should not generate an abnormal excluded-row ratio");

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = previewDto.SessionId,
            PortfolioId = twdPortfolio.Id,
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.Margin
            },
            Rows = executeRows
        };

        // Act 2: execute
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert execute
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);

        var executeDto = JsonSerializer.Deserialize<StockImportExecuteResponseDto>(
            executePayload,
            ApiJsonOptions);
        executeDto.Should().NotBeNull();
        executeDto!.Status.Should().Be("committed");

        var executeSummary = executeDto.Summary;
        var executeResultRows = executeDto.Results;

        executeSummary.InsertedRows.Should().BeGreaterThan(0);
        executeSummary.TotalRows.Should().Be(executeSummary.InsertedRows + executeSummary.FailedRows);
        executeSummary.TotalRows.Should().Be(executeResultRows.Count);
        executeSummary.ErrorCount.Should().Be(executeDto.Errors.Count);

        var executeResultJsonRows = executeJson.RootElement.GetProperty("results").EnumerateArray().ToList();
        executeResultJsonRows.Should().NotBeEmpty();

        foreach (var result in executeResultJsonRows)
        {
            result.TryGetProperty("rowNumber", out var resultRowNumber).Should().BeTrue();
            resultRowNumber.ValueKind.Should().Be(JsonValueKind.Number);

            result.TryGetProperty("success", out var resultSuccess).Should().BeTrue();
            (resultSuccess.ValueKind == JsonValueKind.True || resultSuccess.ValueKind == JsonValueKind.False)
                .Should().BeTrue();

            result.TryGetProperty("message", out var resultMessage).Should().BeTrue();
            resultMessage.ValueKind.Should().Be(JsonValueKind.String);
            resultMessage.GetString().Should().NotBeNullOrWhiteSpace();
        }

        var insertedResultRows = executeResultRows
            .Where(row => row.TransactionId.HasValue)
            .ToList();
        insertedResultRows.Count.Should().Be(executeSummary.InsertedRows);
        insertedResultRows.Should().OnlyContain(row =>
            row.TransactionId.HasValue
            && row.TransactionId.Value != Guid.Empty
            && string.IsNullOrWhiteSpace(row.ErrorCode));

        var failedResultRows = executeResultRows
            .Where(row => !row.Success)
            .ToList();
        failedResultRows.Count.Should().Be(executeSummary.FailedRows);
        failedResultRows.Should().OnlyContain(row =>
            !row.TransactionId.HasValue && !string.IsNullOrWhiteSpace(row.ErrorCode));

        var hasSellBeforeBuyTrace = executeResultRows.Any(row =>
            string.Equals(row.SellBeforeBuyDecision?.Strategy, "create_adjustment", StringComparison.Ordinal)
            && string.Equals(row.SellBeforeBuyDecision?.DecisionScope, "auto_default", StringComparison.Ordinal));
        hasSellBeforeBuyTrace.Should().BeTrue(
            "sample CSV includes sell-before-buy scenarios and execute result should expose decision trace");

        // Act 3: list imported transactions to assert net holdings semantics through API-observable data
        var transactionsResponse = await Client.GetAsync($"/api/stocktransactions?portfolioId={twdPortfolio.Id}");
        transactionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var importedTransactions = await transactionsResponse.Content.ReadFromJsonAsync<List<StockTransactionDto>>();
        importedTransactions.Should().NotBeNull();
        importedTransactions!.Should().NotBeEmpty();

        var seededAdjustmentCount = importedTransactions.Count(transaction =>
            transaction.TransactionType == TransactionType.Adjustment
            && !string.IsNullOrWhiteSpace(transaction.Notes)
            && transaction.Notes!.Contains("import-execute-adjustment", StringComparison.Ordinal));

        seededAdjustmentCount.Should().BeGreaterThan(0,
            "sample CSV includes sell-before-buy rows and should leave traceable auto-adjustment artifacts");

        importedTransactions.Count.Should().Be(
            executeSummary.InsertedRows + seededAdjustmentCount,
            "execute summary tracks requested rows while persisted transactions also include seeded sell-before-buy adjustments");

        var netSharesByTicker = importedTransactions
            .GroupBy(transaction => transaction.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(transaction => transaction.TransactionType switch
                {
                    TransactionType.Buy => transaction.Shares,
                    TransactionType.Adjustment => transaction.Shares,
                    TransactionType.Sell => -transaction.Shares,
                    _ => 0m
                }),
                StringComparer.OrdinalIgnoreCase);

        netSharesByTicker.Values.Should().Contain(value => value == 0m,
            "sample CSV contains round-trip trades that close out to zero shares");
        netSharesByTicker.Values.Should().Contain(value => value <= 0m,
            "sample CSV should include at least one ticker whose net shares are non-positive after import");

        var netPositiveTickers = netSharesByTicker
            .Where(entry => entry.Value > 0m)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var netNegativeTickers = netSharesByTicker
            .Where(entry => entry.Value < 0m)
            .Select(entry => entry.Key)
            .ToList();

        // Act 4: summary
        var summaryResponse = await Client.GetAsync($"/api/portfolios/{twdPortfolio.Id}/summary");
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await summaryResponse.Content.ReadFromJsonAsync<PortfolioSummaryDto>();
        summary.Should().NotBeNull();
        summary!.Positions.Should().NotBeEmpty();
        summary.Positions.Should().OnlyContain(position => position.TotalShares > 0m);

        var summaryTickers = summary.Positions
            .Select(position => position.Ticker)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var ticker in netPositiveTickers)
        {
            summaryTickers.Should().Contain(
                ticker,
                $"ticker {ticker} remains net-positive in imported transactions");
        }

        foreach (var ticker in netNegativeTickers)
        {
            summary.Positions.Should().NotContain(
                position => position.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase),
                $"ticker {ticker} has negative net shares ({netSharesByTicker[ticker]})");
        }

        summary.Positions.Should().Contain(position => position.TotalCostSource > 0m);

        // Act 4: available years
        var availableYearsResponse = await Client.GetAsync($"/api/portfolios/{twdPortfolio.Id}/performance/years");
        availableYearsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var availableYears = await availableYearsResponse.Content.ReadFromJsonAsync<AvailableYearsDto>();
        availableYears.Should().NotBeNull();
        availableYears!.Years.Should().Contain(2025);
        availableYears.Years.Should().Contain(2026);

        // Act 5: year performance (2026)
        const int requestedYear = 2026;

        var requestPrices = BuildYearPriceRequestFromSummaryPositions(summary.Positions);
        var yearPerformanceRequest = new CalculateYearPerformanceRequest
        {
            Year = requestedYear,
            YearStartPrices = requestPrices,
            YearEndPrices = requestPrices
        };

        var yearResponse = await Client.PostAsJsonAsync(
            $"/api/portfolios/{twdPortfolio.Id}/performance/year",
            yearPerformanceRequest);

        yearResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var yearPayload = await yearResponse.Content.ReadAsStringAsync();
        using var yearJson = JsonDocument.Parse(yearPayload);
        var yearRoot = yearJson.RootElement;

        AssertYearPerformanceCoverageSignalContract(yearRoot);

        var yearPerformance = JsonSerializer.Deserialize<YearPerformanceDto>(
            yearPayload,
            ApiJsonOptions);
        yearPerformance.Should().NotBeNull();
        yearPerformance!.Year.Should().Be(2026);
        yearPerformance.SourceCurrency.Should().NotBeNullOrWhiteSpace();
        yearPerformance.CashFlowCount.Should().Be(0);
        yearPerformance.TransactionCount.Should().BeGreaterThan(0);
        yearPerformance.NetContributionsHome.Should().BeInRange(-1_000_000_000m, 1_000_000_000m);

        yearPerformance.Xirr.Should().BeNull();
        yearPerformance.XirrPercentage.Should().BeNull();
        AssertFiniteIfHasValue(yearPerformance.TotalReturnPercentage, nameof(YearPerformanceDto.TotalReturnPercentage));
        AssertFiniteIfHasValue(yearPerformance.ModifiedDietzPercentage, nameof(YearPerformanceDto.ModifiedDietzPercentage));
        AssertFiniteIfHasValue(yearPerformance.TimeWeightedReturnPercentage, nameof(YearPerformanceDto.TimeWeightedReturnPercentage));

        // Act 6: monthly net worth
        var monthlyResponse = await Client.GetAsync(
            $"/api/portfolios/{twdPortfolio.Id}/performance/monthly?fromMonth={requestedYear}-01&toMonth={requestedYear}-12");

        monthlyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var monthlyHistory = await monthlyResponse.Content.ReadFromJsonAsync<MonthlyNetWorthHistoryDto>();
        monthlyHistory.Should().NotBeNull();
        monthlyHistory!.Data.Should().NotBeEmpty();
        monthlyHistory.TotalMonths.Should().Be(monthlyHistory.Data.Count);

        var monthlyItem = monthlyHistory.Data.First();
        monthlyItem.Month.Should().NotBeNullOrWhiteSpace();
        DateOnly.ParseExact(monthlyItem.Month, "yyyy-MM", CultureInfo.InvariantCulture);

        if (monthlyItem.Value.HasValue)
        {
            monthlyItem.Value.Value.Should().BeInRange(-1_000_000_000m, 1_000_000_000m);
        }

        if (monthlyItem.Contributions.HasValue)
        {
            monthlyItem.Contributions.Value.Should().BeInRange(-1_000_000_000m, 1_000_000_000m);
        }
    }

    [Fact]
    public async Task PreviewExecuteAndYearPerformance_WithoutOpeningBaselineWithLateTopUp_ShouldExposeMdExtremeRootCause()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync(
            "Stock Import MD Extreme Root Cause",
            currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneLateYearBuyRow(),
            SelectedFormat = "broker_statement"
        };

        // Act 1: preview
        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);

        // Assert preview
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewDto = await previewResponse.Content.ReadFromJsonAsync<StockImportPreviewResponseDto>();
        previewDto.Should().NotBeNull();
        previewDto!.Rows.Should().ContainSingle();

        var previewRow = previewDto.Rows.Single();
        previewRow.TradeDate.Should().NotBeNull();
        previewRow.TradeDate!.Value.Date.Should().Be(new DateTime(2025, 12, 30));
        previewRow.ConfirmedTradeSide.Should().Be("buy");

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = previewDto.SessionId,
            PortfolioId = portfolio.Id,
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = previewRow.RowNumber,
                    Ticker = previewRow.Ticker,
                    ConfirmedTradeSide = previewRow.ConfirmedTradeSide,
                    Exclude = false
                }
            ]
        };

        // Act 2: execute
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert execute
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executeDto = await executeResponse.Content.ReadFromJsonAsync<StockImportExecuteResponseDto>();
        executeDto.Should().NotBeNull();
        executeDto!.Status.Should().Be("committed");
        executeDto.Summary.InsertedRows.Should().Be(1);
        executeDto.Summary.FailedRows.Should().Be(0);
        executeDto.Results.Should().ContainSingle(result => result.Success && result.TransactionId.HasValue);

        var importedTransactionId = executeDto.Results.Single().TransactionId!.Value;

        using (var scope = Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var linkedCurrencyTransactions = await dbContext.CurrencyTransactions
                .Where(transaction => transaction.RelatedStockTransactionId == importedTransactionId)
                .ToListAsync();

            linkedCurrencyTransactions.Should().Contain(transaction =>
                transaction.TransactionType == CurrencyTransactionType.Deposit
                && transaction.Notes != null
                && transaction.Notes.Contains("補足買入", StringComparison.Ordinal));

            linkedCurrencyTransactions.Should().ContainSingle(transaction =>
                transaction.TransactionType == CurrencyTransactionType.Spend);
        }

        // Act 3: performance available years (UI-equivalent navigation step)
        var availableYearsResponse = await Client.GetAsync($"/api/portfolios/{portfolio.Id}/performance/years");
        availableYearsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var availableYears = await availableYearsResponse.Content.ReadFromJsonAsync<AvailableYearsDto>();
        availableYears.Should().NotBeNull();
        availableYears!.Years.Should().Contain(2025);

        // Act 4: performance year read with manual year-end price supplement
        // This mirrors UI補價流程：使用者提供年末價後再讀取年度績效。
        var yearResponse = await Client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.Id}/performance/year",
            new CalculateYearPerformanceRequest
            {
                Year = 2025,
                YearEndPrices = new Dictionary<string, YearEndPriceInfo>
                {
                    ["2330"] = new() { Price = 105.68684m, ExchangeRate = 1m }
                }
            });

        yearResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var yearPayload = await yearResponse.Content.ReadAsStringAsync();
        using var yearJson = JsonDocument.Parse(yearPayload);
        var yearRoot = yearJson.RootElement;

        AssertYearPerformanceCoverageSignalContract(yearRoot);

        yearRoot.GetProperty("coverageDays").GetInt32().Should().Be(2);
        yearRoot.GetProperty("hasOpeningBaseline").GetBoolean().Should().BeFalse();
        yearRoot.GetProperty("usesPartialHistoryAssumption").GetBoolean().Should().BeTrue();
        yearRoot.GetProperty("xirrReliability").GetString().Should().Be("Unavailable");
        yearRoot.GetProperty("shouldDegradeReturnDisplay").GetBoolean().Should().BeTrue();
        yearRoot.GetProperty("returnDisplayDegradeReasonCode").GetString()
            .Should().Be("LOW_CONFIDENCE_NO_OPENING_BASELINE_AND_LOW_COVERAGE");
        yearRoot.GetProperty("returnDisplayDegradeReasonMessage").GetString().Should().NotBeNullOrWhiteSpace();
        yearRoot.GetProperty("hasRecentLargeInflowWarning").GetBoolean().Should().BeTrue();
        yearRoot.GetProperty("recentLargeInflowWarningMessage").GetString()
            .Should().Be("近期大額資金異動可能導致資金加權報酬率短期波動。");

        var yearPerformance = JsonSerializer.Deserialize<YearPerformanceDto>(yearPayload, ApiJsonOptions);
        yearPerformance.Should().NotBeNull();

        yearPerformance!.TransactionCount.Should().Be(1);
        yearPerformance.CashFlowCount.Should().Be(0);

        yearPerformance.StartValueSource.Should().Be(0m);
        yearPerformance.EndValueSource.Should().BeApproximately(105686.84m, 0.001m);
        yearPerformance.NetContributionsSource.Should().Be(100000m);
        yearPerformance.StartValueHome.Should().Be(0m);
        yearPerformance.EndValueHome.Should().BeApproximately(105686.84m, 0.001m);
        yearPerformance.NetContributionsHome.Should().Be(100000m);

        yearPerformance.Xirr.Should().BeNull();
        yearPerformance.XirrPercentage.Should().BeNull();
        yearPerformance.XirrSource.Should().BeNull();
        yearPerformance.XirrPercentageSource.Should().BeNull();

        var periodStart = new DateTime(2025, 1, 1);
        var periodEnd = new DateTime(2025, 12, 31);
        var tradeDate = new DateTime(2025, 12, 30);
        var totalDays = (periodEnd.Date - periodStart.Date).Days;
        var daysSinceStart = (tradeDate.Date - periodStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;

        // Root-cause lock: denominator becomes tiny when year-start baseline is 0
        // and only a very-late external top-up cash flow is weighted into MD.
        var expectedDenominator = yearPerformance.StartValueSource!.Value + yearPerformance.NetContributionsSource!.Value * weight;
        var expectedNumerator = yearPerformance.EndValueSource!.Value
            - yearPerformance.StartValueSource!.Value
            - yearPerformance.NetContributionsSource!.Value;
        var expectedDietzPct = (double)((expectedNumerator / expectedDenominator) * 100m);

        totalDays.Should().Be(364);
        daysSinceStart.Should().Be(363);
        weight.Should().BeApproximately(1m / 364m, 0.0000001m);
        expectedDenominator.Should().BeApproximately(274.7252747m, 0.0001m);
        expectedDenominator.Should().BeLessThan(300m);
        expectedNumerator.Should().Be(5686.84m);

        yearPerformance.ModifiedDietzPercentageSource.Should().BeApproximately(expectedDietzPct, 0.0001d);
        yearPerformance.ModifiedDietzPercentageSource.Should().BeApproximately(2070.01d, 0.2d);
        yearPerformance.ModifiedDietzPercentageSource.Should().BeGreaterThan(1000d);
        yearPerformance.ModifiedDietzPercentage.Should().BeApproximately(expectedDietzPct, 0.0001d);

        yearPerformance.TimeWeightedReturnPercentageSource.Should().BeApproximately(5.68684d, 0.0001d);
        yearPerformance.TimeWeightedReturnPercentage.Should().BeApproximately(5.68684d, 0.0001d);
        (yearPerformance.ModifiedDietzPercentageSource!.Value - yearPerformance.TimeWeightedReturnPercentageSource!.Value)
            .Should().BeGreaterThan(2000d);

        yearPerformance.HasRecentLargeInflowWarning.Should().BeTrue();
        yearPerformance.RecentLargeInflowWarningMessage.Should().Be("近期大額資金異動可能導致資金加權報酬率短期波動。");
    }

    [Fact]
    public async Task RegisterPreviewExecuteAndPerformance_UsingSampleCsv_WithMarginDataPath_ShouldKeep2025TwrInReasonableRange()
    {
        // Arrange
        var registerRequest = new RegisterRequest
        {
            Email = $"import-margin-2025-{Guid.NewGuid():N}@example.com",
            Password = "Password123!",
            DisplayName = "Import Margin 2025"
        };

        var registerResponse = await Client.PostAsJsonAsync("/api/auth/register", registerRequest);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        auth.Should().NotBeNull();
        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        Factory.TestUserId = auth.User.Id;

        var portfoliosResponse = await Client.GetAsync("/api/portfolios");
        portfoliosResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var portfolios = await portfoliosResponse.Content.ReadFromJsonAsync<List<PortfolioDto>>();
        portfolios.Should().NotBeNullOrEmpty();

        var twdPortfolio = portfolios!
            .SingleOrDefault(p => string.Equals(p.BaseCurrency, "TWD", StringComparison.OrdinalIgnoreCase));
        twdPortfolio.Should().NotBeNull();

        var sampleFixture = await LoadSampleBrokerStatementFixtureAsync();

        var previewResponse = await Client.PostAsJsonAsync(
            PreviewEndpoint,
            new PreviewStockImportRequest
            {
                PortfolioId = twdPortfolio!.Id,
                CsvContent = sampleFixture.CsvContent,
                SelectedFormat = "broker_statement"
            });

        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewDto = await previewResponse.Content.ReadFromJsonAsync<StockImportPreviewResponseDto>();
        previewDto.Should().NotBeNull();
        previewDto!.Rows.Should().HaveCount(sampleFixture.DataRowCount);

        var executeRows = previewDto.Rows
            .Where(row => !string.Equals(row.Status, "invalid", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(row.ConfirmedTradeSide)
                && string.Equals(row.ConfirmedTradeSide, "buy", StringComparison.OrdinalIgnoreCase))
            .Select(row =>
            {
                var resolvedTicker = ResolveTickerForExecute(row);
                return new ExecuteStockImportRowRequest
                {
                    RowNumber = row.RowNumber,
                    Ticker = resolvedTicker,
                    ConfirmedTradeSide = row.ConfirmedTradeSide,
                    Exclude = string.IsNullOrWhiteSpace(resolvedTicker)
                };
            })
            .ToList();

        executeRows.Should().Contain(row => !row.Exclude);

        // Data-path lock: use buy-only rows + Margin to stabilize 2025 return path without synthetic top-up deposits.
        var executeResponse = await Client.PostAsJsonAsync(
            ExecuteEndpoint,
            new ExecuteStockImportRequest
            {
                SessionId = previewDto.SessionId,
                PortfolioId = twdPortfolio.Id,
                DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
                {
                    Action = BalanceAction.Margin
                },
                Rows = executeRows
            });

        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);
        var executeDto = JsonSerializer.Deserialize<StockImportExecuteResponseDto>(executePayload, ApiJsonOptions);
        executeDto.Should().NotBeNull();
        executeDto!.Summary.InsertedRows.Should().BeGreaterThan(0);

        using (var scope = Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var topUpDepositsIn2025 = await dbContext.CurrencyTransactions
                .Where(transaction => !transaction.IsDeleted
                    && transaction.CurrencyLedgerId == twdPortfolio.BoundCurrencyLedgerId
                    && transaction.TransactionType == CurrencyTransactionType.Deposit
                    && transaction.RelatedStockTransactionId != null
                    && transaction.Notes != null
                    && transaction.Notes.StartsWith("補足買入", StringComparison.Ordinal)
                    && transaction.TransactionDate.Year == 2025)
                .ToListAsync();

            // Root-cause data-path difference vs 2070.01% extreme test:
            // this sample path may or may not create top-up deposits depending on price/position state.
            // When top-up events exist, they must stay on import buy-link semantics and not include sell-link rows.
            if (topUpDepositsIn2025.Count > 0)
            {
                var importedBuyTransactionIds = await dbContext.StockTransactions
                    .Where(transaction => !transaction.IsDeleted
                        && transaction.PortfolioId == twdPortfolio.Id
                        && transaction.TransactionType == TransactionType.Buy)
                    .Select(transaction => transaction.Id)
                    .ToListAsync();

                topUpDepositsIn2025.Should().OnlyContain(transaction =>
                    transaction.RelatedStockTransactionId.HasValue
                    && importedBuyTransactionIds.Contains(transaction.RelatedStockTransactionId.Value)
                    && transaction.ForeignAmount > 0m
                    && transaction.TransactionDate.Year == 2025
                    && transaction.TransactionDate.Date <= new DateTime(2025, 12, 31));
            }
        }

        var availableYearsResponse = await Client.GetAsync($"/api/portfolios/{twdPortfolio.Id}/performance/years");
        availableYearsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var availableYears = await availableYearsResponse.Content.ReadFromJsonAsync<AvailableYearsDto>();
        availableYears.Should().NotBeNull();
        availableYears!.Years.Should().Contain(2025);

        var summaryResponse = await Client.GetAsync($"/api/portfolios/{twdPortfolio.Id}/summary");
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await summaryResponse.Content.ReadFromJsonAsync<PortfolioSummaryDto>();
        summary.Should().NotBeNull();

        var requestPrices = BuildYearPriceRequestFromSummaryPositions(summary!.Positions);

        var yearResponse = await Client.PostAsJsonAsync(
            $"/api/portfolios/{twdPortfolio.Id}/performance/year",
            new CalculateYearPerformanceRequest
            {
                Year = 2025,
                YearStartPrices = requestPrices,
                YearEndPrices = requestPrices
            });

        yearResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var yearPayload = await yearResponse.Content.ReadAsStringAsync();
        using var yearJson = JsonDocument.Parse(yearPayload);
        var yearRoot = yearJson.RootElement;
        AssertYearPerformanceCoverageSignalContract(yearRoot);

        var yearPerformance = JsonSerializer.Deserialize<YearPerformanceDto>(yearPayload, ApiJsonOptions);
        yearPerformance.Should().NotBeNull();

        yearPerformance!.Year.Should().Be(2025);
        yearPerformance.HasOpeningBaseline.Should().BeFalse();
        yearPerformance.UsesPartialHistoryAssumption.Should().BeTrue();
        yearPerformance.CoverageDays.Should().BeGreaterThan(60);
        yearPerformance.CoverageDays.Should().BeLessThan(90);

        // Regression lock: 2025 TWR should be computable and match rebuildable snapshot-chain calculation.
        yearPerformance.TimeWeightedReturnPercentageSource.Should().NotBeNull($"year payload: {yearPayload}");
        yearPerformance.TimeWeightedReturnPercentage.Should().NotBeNull($"year payload: {yearPayload}");

        double? expectedTwrPctSource = null;
        double? expectedTotalReturnPctSource = null;

        using (var scope = Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var portfolioEntity = await dbContext.Portfolios
                .Where(portfolio => portfolio.Id == twdPortfolio.Id)
                .SingleAsync();

            var yearStart = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var yearEnd = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

            var twrSnapshots = await dbContext.TransactionPortfolioSnapshots
                .Where(snapshot => snapshot.PortfolioId == twdPortfolio.Id
                    && snapshot.SnapshotDate >= yearStart
                    && snapshot.SnapshotDate <= yearEnd)
                .OrderBy(snapshot => snapshot.SnapshotDate)
                .ThenBy(snapshot => snapshot.CreatedAt)
                .Select(snapshot => new ReturnValuationSnapshot(
                    snapshot.SnapshotDate,
                    snapshot.PortfolioValueBeforeSource,
                    snapshot.PortfolioValueAfterSource))
                .ToListAsync();

            var returnCalculator = new ReturnCalculator();

            var expectedTwrSource = returnCalculator.CalculateTimeWeightedReturn(
                yearPerformance.StartValueSource ?? 0m,
                yearPerformance.EndValueSource ?? 0m,
                twrSnapshots);

            expectedTwrPctSource = expectedTwrSource.HasValue
                ? (double)(expectedTwrSource.Value * 100m)
                : null;

            var netContributionsSource = yearPerformance.NetContributionsSource ?? 0m;
            var startValueSource = yearPerformance.StartValueSource ?? 0m;
            var endValueSource = yearPerformance.EndValueSource ?? 0m;

            if (startValueSource > 0m)
            {
                expectedTotalReturnPctSource = (double)((endValueSource - startValueSource - netContributionsSource) / startValueSource) * 100d;
            }
            else if (netContributionsSource != 0m)
            {
                expectedTotalReturnPctSource = (double)((endValueSource - netContributionsSource) / netContributionsSource) * 100d;
            }
        }

        expectedTwrPctSource.Should().NotBeNull("snapshot chain should produce a deterministic TWR for 2025 margin data path");
        yearPerformance.TimeWeightedReturnPercentageSource.Should().BeApproximately(expectedTwrPctSource!.Value, 0.0001d);
        yearPerformance.TimeWeightedReturnPercentage.Should().BeApproximately(expectedTwrPctSource.Value, 0.0001d);
        yearPerformance.TimeWeightedReturnPercentageSource.Should().NotBeApproximately(-100d, 0.0001d,
            "sample broker margin path should not regress to TWR -100% after import baseline handling");

        if (yearPerformance.ModifiedDietzPercentageSource.HasValue)
        {
            yearPerformance.ModifiedDietzPercentageSource.Should().NotBeApproximately(469.02d, 0.01d,
                "sample broker margin path should not regress to MD 469.02% blow-up");
            yearPerformance.ModifiedDietzPercentageSource.Value.Should().BeGreaterThan(-100d);
            yearPerformance.ModifiedDietzPercentageSource.Value.Should().BeLessThan(200d,
                "MD should stay within a reasonable guardrail for this regression fixture");
        }

        if (yearPerformance.ModifiedDietzPercentage.HasValue)
        {
            yearPerformance.ModifiedDietzPercentage.Value.Should().BeGreaterThan(-100d);
            yearPerformance.ModifiedDietzPercentage.Value.Should().BeLessThan(200d);
        }

        yearPerformance.HasRecentLargeInflowWarning.Should().BeFalse();
        yearPerformance.RecentLargeInflowWarningMessage.Should().BeNull();

        // Regression lock: total return may be null under low-confidence/no-opening-baseline path,
        // but if present it should match source-path reconstruction.
        if (yearPerformance.TotalReturnPercentageSource.HasValue)
        {
            expectedTotalReturnPctSource.Should().NotBeNull();
            yearPerformance.TotalReturnPercentageSource!.Value.Should().BeApproximately(expectedTotalReturnPctSource!.Value, 0.0001d);
        }

        if (yearPerformance.TotalReturnPercentage.HasValue)
        {
            expectedTotalReturnPctSource.Should().NotBeNull();
            yearPerformance.TotalReturnPercentage!.Value.Should().BeApproximately(expectedTotalReturnPctSource!.Value, 0.0001d);
        }

        var aggregateResponse = await Client.PostAsJsonAsync(
            "/api/portfolios/aggregate/performance/year",
            new CalculateYearPerformanceRequest
            {
                Year = 2025,
                YearStartPrices = requestPrices,
                YearEndPrices = requestPrices
            });

        aggregateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var aggregatePerformance = await aggregateResponse.Content.ReadFromJsonAsync<YearPerformanceDto>();
        aggregatePerformance.Should().NotBeNull();

        aggregatePerformance!.HasRecentLargeInflowWarning.Should().BeFalse();
        aggregatePerformance.RecentLargeInflowWarningMessage.Should().BeNull();

        if (aggregatePerformance.TotalReturnPercentageSource.HasValue)
        {
            expectedTotalReturnPctSource.Should().NotBeNull();
            aggregatePerformance.TotalReturnPercentageSource!.Value.Should().BeApproximately(expectedTotalReturnPctSource!.Value, 0.0001d);
        }

        if (aggregatePerformance.TotalReturnPercentage.HasValue)
        {
            expectedTotalReturnPctSource.Should().NotBeNull();
            aggregatePerformance.TotalReturnPercentage!.Value.Should().BeApproximately(expectedTotalReturnPctSource!.Value, 0.0001d);
        }
    }

    [Fact]
    public async Task RegisterPreviewExecuteAndPerformance_UsingSampleCsv_DefaultUiPathWithoutBalanceDefaults_ShouldExpose2025RegressionSignal()
    {
        // Arrange: fresh account + default broker import path (no DefaultBalanceAction / no custom remediation)
        var registerRequest = new RegisterRequest
        {
            Email = $"import-default-path-2025-{Guid.NewGuid():N}@example.com",
            Password = "Password123!",
            DisplayName = "Import Default Path 2025"
        };

        var registerResponse = await Client.PostAsJsonAsync("/api/auth/register", registerRequest);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        auth.Should().NotBeNull();
        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        Factory.TestUserId = auth.User.Id;

        var portfoliosResponse = await Client.GetAsync("/api/portfolios");
        portfoliosResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var portfolios = await portfoliosResponse.Content.ReadFromJsonAsync<List<PortfolioDto>>();
        portfolios.Should().NotBeNullOrEmpty();

        var twdPortfolio = portfolios!
            .SingleOrDefault(p => string.Equals(p.BaseCurrency, "TWD", StringComparison.OrdinalIgnoreCase));
        twdPortfolio.Should().NotBeNull();

        var sampleFixture = await LoadSampleBrokerStatementFixtureAsync();

        var previewResponse = await Client.PostAsJsonAsync(
            PreviewEndpoint,
            new PreviewStockImportRequest
            {
                PortfolioId = twdPortfolio!.Id,
                CsvContent = sampleFixture.CsvContent,
                SelectedFormat = "broker_statement"
            });

        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var previewRoot = previewJson.RootElement;

        var previewDto = JsonSerializer.Deserialize<StockImportPreviewResponseDto>(previewPayload, ApiJsonOptions);
        previewDto.Should().NotBeNull();
        previewDto!.Rows.Should().HaveCount(sampleFixture.DataRowCount);

        var previewRowsByRowNumber = previewRoot.GetProperty("rows")
            .EnumerateArray()
            .ToDictionary(row => row.GetProperty("rowNumber").GetInt32());

        var executeRows = previewDto.Rows
            .Where(row => !string.Equals(row.Status, "invalid", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(row.ConfirmedTradeSide))
            .Select(row =>
            {
                var resolvedTicker = ResolveTickerForExecute(row);
                return new ExecuteStockImportRowRequest
                {
                    RowNumber = row.RowNumber,
                    Ticker = resolvedTicker,
                    ConfirmedTradeSide = row.ConfirmedTradeSide,
                    Exclude = string.IsNullOrWhiteSpace(resolvedTicker)
                };
            })
            .ToList();

        executeRows.Should().Contain(row => !row.Exclude);

        var executeResponse = await Client.PostAsJsonAsync(
            ExecuteEndpoint,
            new ExecuteStockImportRequest
            {
                SessionId = previewDto.SessionId,
                PortfolioId = twdPortfolio.Id,
                Rows = executeRows
            });

        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);
        var executeRoot = executeJson.RootElement;

        var executeDto = JsonSerializer.Deserialize<StockImportExecuteResponseDto>(executePayload, ApiJsonOptions);
        executeDto.Should().NotBeNull();
        executeDto!.Status.Should().Be("rejected",
            "broker_statement default path should rollback atomically when any row fails with BALANCE_ACTION_REQUIRED");
        executeDto.Summary.InsertedRows.Should().Be(0,
            "atomic rollback should avoid partial commit that can poison performance metrics");

        var balanceActionRequiredRowNumbers = executeRoot.GetProperty("results")
            .EnumerateArray()
            .Where(result => string.Equals(
                result.GetProperty("errorCode").GetString(),
                "BALANCE_ACTION_REQUIRED",
                StringComparison.Ordinal))
            .Select(result => result.GetProperty("rowNumber").GetInt32())
            .Distinct()
            .ToList();

        var missingBalanceDecisionSignalRows = new List<int>();

        foreach (var rowNumber in balanceActionRequiredRowNumbers)
        {
            previewRowsByRowNumber.Should().ContainKey(rowNumber,
                $"execute row {rowNumber} failed with BALANCE_ACTION_REQUIRED but preview row should exist");

            var previewRow = previewRowsByRowNumber[rowNumber];
            var actionsRequired = previewRow.GetProperty("actionsRequired")
                .EnumerateArray()
                .Select(action => action.GetString())
                .Where(action => !string.IsNullOrWhiteSpace(action))
                .Select(action => action!)
                .ToList();

            var hasSelectBalanceAction = actionsRequired.Contains("select_balance_action", StringComparer.Ordinal);
            var hasBalanceDecisionObject = previewRow.TryGetProperty("balanceDecision", out var balanceDecision)
                && balanceDecision.ValueKind == JsonValueKind.Object;

            if (!hasSelectBalanceAction && !hasBalanceDecisionObject)
            {
                missingBalanceDecisionSignalRows.Add(rowNumber);
            }
        }

        balanceActionRequiredRowNumbers.Should().NotBeEmpty(
            "this regression fixture should still surface rows requiring balance decisions on default path");

        if (balanceActionRequiredRowNumbers.Count > 0)
        {
            missingBalanceDecisionSignalRows.Should().NotBeEmpty(
                "rows that later require balance decision should expose reproducible preview evidence when signal is missing");
        }

        executeDto.Results.Should().NotContain(result => result.TransactionId.HasValue,
            "rejected broker_statement execute should not expose committed transaction ids after atomic rollback");

        // Note: integration tests use EF Core InMemory provider and AppDbTransactionManager falls back to a no-op transaction,
        // so we validate rollback semantics through API contract (status/summary/result ids) instead of DB persistence state here.
    }

    [Fact]
    public async Task Preview_Endpoint_IsAvailable_AndReturnsContractFields()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Preview Contract");
        var request = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement"
        };

        // Act
        var response = await Client.PostAsJsonAsync(PreviewEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);

        json.RootElement.TryGetProperty("sessionId", out var sessionId).Should().BeTrue();
        sessionId.ValueKind.Should().Be(JsonValueKind.String);

        json.RootElement.TryGetProperty("detectedFormat", out var detectedFormat).Should().BeTrue();
        detectedFormat.ValueKind.Should().Be(JsonValueKind.String);

        json.RootElement.TryGetProperty("selectedFormat", out var selectedFormat).Should().BeTrue();
        selectedFormat.ValueKind.Should().Be(JsonValueKind.String);

        json.RootElement.TryGetProperty("summary", out var summary).Should().BeTrue();
        summary.ValueKind.Should().Be(JsonValueKind.Object);

        summary.TryGetProperty("totalRows", out var totalRows).Should().BeTrue();
        totalRows.ValueKind.Should().Be(JsonValueKind.Number);
        summary.TryGetProperty("validRows", out var validRows).Should().BeTrue();
        validRows.ValueKind.Should().Be(JsonValueKind.Number);
        summary.TryGetProperty("requiresActionRows", out var requiresActionRows).Should().BeTrue();
        requiresActionRows.ValueKind.Should().Be(JsonValueKind.Number);
        summary.TryGetProperty("invalidRows", out var invalidRows).Should().BeTrue();
        invalidRows.ValueKind.Should().Be(JsonValueKind.Number);

        json.RootElement.TryGetProperty("rows", out var rows).Should().BeTrue();
        rows.ValueKind.Should().Be(JsonValueKind.Array);

        json.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.ValueKind.Should().Be(JsonValueKind.Array);

        rows.GetArrayLength().Should().BeGreaterThan(0);

        using (var scope = Factory.Services.CreateScope())
        {
            var sessionStore = scope.ServiceProvider.GetRequiredService<IStockImportSessionStore>();
            var persistedSession = await sessionStore.GetAsync(sessionId.GetGuid(), CancellationToken.None);
            persistedSession.Should().NotBeNull("preview should persist import session for later execute");
        }

        var firstRow = rows.EnumerateArray().First();
        firstRow.TryGetProperty("rowNumber", out var rowNumber).Should().BeTrue();
        rowNumber.ValueKind.Should().Be(JsonValueKind.Number);

        firstRow.TryGetProperty("tradeSide", out var tradeSide).Should().BeTrue();
        tradeSide.ValueKind.Should().Be(JsonValueKind.String);

        firstRow.TryGetProperty("status", out var status).Should().BeTrue();
        status.ValueKind.Should().Be(JsonValueKind.String);

        firstRow.TryGetProperty("actionsRequired", out var actionsRequired).Should().BeTrue();
        actionsRequired.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Preview_AcceptsBaselineContract_AndStillReturnsSuccess()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Baseline Contract");
        var request = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement",
            Baseline = new StockImportBaselineRequest
            {
                BaselineDate = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                OpeningCashBalance = 120000m,
                OpeningLedgerBalance = null,
                OpeningPositions =
                [
                    new StockImportOpeningPositionRequest
                    {
                        Ticker = "2330",
                        Quantity = 1000m,
                        TotalCost = 500000m
                    }
                ]
            }
        };

        // Act
        var response = await Client.PostAsJsonAsync(PreviewEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        json.RootElement.TryGetProperty("sessionId", out _).Should().BeTrue();
        json.RootElement.GetProperty("rows").ValueKind.Should().Be(JsonValueKind.Array);

        var sessionId = json.RootElement.GetProperty("sessionId").GetGuid();

        using var scope = Factory.Services.CreateScope();
        var sessionStore = scope.ServiceProvider.GetRequiredService<IStockImportSessionStore>();

        var persistedSession = await sessionStore.GetAsync(sessionId, CancellationToken.None);
        persistedSession.Should().NotBeNull();
        persistedSession!.Baseline.OpeningCashBalance.Should().Be(120000m);
        persistedSession.Baseline.OpeningLedgerBalance.Should().Be(120000m, "OpeningLedgerBalance should fall back to OpeningCashBalance when omitted");
        persistedSession.Baseline.AsOfDate.Should().Be(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        persistedSession.Baseline.BaselineDate.Should().Be(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        persistedSession.Baseline.CurrentHoldings.Should().ContainSingle();
        persistedSession.Baseline.CurrentHoldings.Single().Ticker.Should().Be("2330");
        persistedSession.Baseline.OpeningPositions.Should().ContainSingle();
        persistedSession.Baseline.OpeningPositions.Single().Ticker.Should().Be("2330");

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 1,
                    Ticker = "2330",
                    ConfirmedTradeSide = "buy",
                    Exclude = true
                }
            ]
        };

        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var consumedSession = await sessionStore.GetAsync(sessionId, CancellationToken.None);
        consumedSession.Should().BeNull("execute should consume preview session atomically");
    }

    [Fact]
    public async Task Preview_AcceptsCurrentHoldingsAsOfContract_AndPersistsLegacyCompatibleSnapshot()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Current Holdings Contract");
        var request = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement",
            Baseline = new StockImportBaselineRequest
            {
                AsOfDate = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                OpeningCashBalance = 120000m,
                OpeningLedgerBalance = null,
                CurrentHoldings =
                [
                    new StockImportOpeningPositionRequest
                    {
                        Ticker = "2330",
                        Quantity = 1000m,
                        TotalCost = 500000m,
                        HistoricalTotalCost = 490000m
                    }
                ]
            }
        };

        // Act
        var response = await Client.PostAsJsonAsync(PreviewEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        json.RootElement.TryGetProperty("sessionId", out _).Should().BeTrue();

        var sessionId = json.RootElement.GetProperty("sessionId").GetGuid();

        using var scope = Factory.Services.CreateScope();
        var sessionStore = scope.ServiceProvider.GetRequiredService<IStockImportSessionStore>();

        var persistedSession = await sessionStore.GetAsync(sessionId, CancellationToken.None);
        persistedSession.Should().NotBeNull();
        persistedSession!.Baseline.AsOfDate.Should().Be(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        persistedSession.Baseline.BaselineDate.Should().Be(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        persistedSession.Baseline.CurrentHoldings.Should().ContainSingle();
        persistedSession.Baseline.CurrentHoldings.Single().Ticker.Should().Be("2330");
        persistedSession.Baseline.CurrentHoldings.Single().Quantity.Should().Be(1000m);
        persistedSession.Baseline.CurrentHoldings.Single().TotalCost.Should().Be(500000m);
        persistedSession.Baseline.CurrentHoldings.Single().HistoricalTotalCost.Should().Be(490000m);

        persistedSession.Baseline.OpeningPositions.Should().ContainSingle();
        persistedSession.Baseline.OpeningPositions.Single().Ticker.Should().Be("2330");
        persistedSession.Baseline.OpeningPositions.Single().Quantity.Should().Be(1000m);
        persistedSession.Baseline.OpeningPositions.Single().TotalCost.Should().Be(500000m);
        persistedSession.Baseline.OpeningPositions.Single().HistoricalTotalCost.Should().Be(490000m);
    }

    [Fact]
    public async Task Preview_DetectsBrokerStatement_ForCathaySampleWithoutTickerHeader()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Cathay Format Detection");
        var request = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildCathayBrokerStatementCsvWithoutTickerHeader(),
            SelectedFormat = "broker_statement"
        };

        // Act
        var response = await Client.PostAsJsonAsync(PreviewEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        root.GetProperty("detectedFormat").GetString().Should().Be("broker_statement");
        root.GetProperty("selectedFormat").GetString().Should().Be("broker_statement");

        var rows = root.GetProperty("rows");
        rows.GetArrayLength().Should().Be(2);

        var firstRow = rows.EnumerateArray().First();
        firstRow.GetProperty("tradeSide").GetString().Should().Be("sell");
        firstRow.GetProperty("fees").GetDecimal().Should().Be(62m);
        firstRow.GetProperty("taxes").GetDecimal().Should().Be(469m);
    }

    [Fact]
    public async Task Preview_SumsMultipleTaxColumns_WhenBrokerCsvContainsBothTaxAndLevies()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Multi Tax Column Summation");
        var request = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithTwoTaxColumns(),
            SelectedFormat = "broker_statement"
        };

        // Act
        var response = await Client.PostAsJsonAsync(PreviewEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        var firstRow = json.RootElement.GetProperty("rows").EnumerateArray().Single();

        firstRow.GetProperty("fees").GetDecimal().Should().Be(12m);
        firstRow.GetProperty("taxes").GetDecimal().Should().Be(16m);
    }

    [Fact]
    public async Task PreviewThenExecute_IncludesTaxesInImportedFees_ForBothBuyAndSell()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync(
            "Stock Import Buy/Sell Tax Consistency",
            currencyCode: "TWD");
        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithBuyAndSellTaxes(),
            SelectedFormat = "broker_statement"
        };

        // Act 1: preview
        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);

        // Assert preview
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var previewRoot = previewJson.RootElement;

        var sessionId = previewRoot.GetProperty("sessionId").GetGuid();
        var previewRows = previewRoot.GetProperty("rows")
            .EnumerateArray()
            .Select(row => new
            {
                RowNumber = row.GetProperty("rowNumber").GetInt32(),
                TradeSide = row.GetProperty("tradeSide").GetString(),
                ConfirmedTradeSide = row.GetProperty("confirmedTradeSide").GetString(),
                Ticker = row.GetProperty("ticker").GetString(),
                Fees = row.GetProperty("fees").GetDecimal(),
                Taxes = row.GetProperty("taxes").GetDecimal()
            })
            .OrderBy(row => row.RowNumber)
            .ToList();

        previewRows.Should().HaveCount(2);

        var buyPreviewRow = previewRows.Single(row => row.TradeSide == "buy");
        var sellPreviewRow = previewRows.Single(row => row.TradeSide == "sell");

        buyPreviewRow.Fees.Should().Be(2m);
        buyPreviewRow.Taxes.Should().Be(3m);
        sellPreviewRow.Fees.Should().Be(4m);
        sellPreviewRow.Taxes.Should().Be(6m);

        // Act 2: execute
        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.Margin
            },
            Rows =
            [
                .. previewRows.Select(row => new ExecuteStockImportRowRequest
                {
                    RowNumber = row.RowNumber,
                    Ticker = row.Ticker,
                    ConfirmedTradeSide = row.ConfirmedTradeSide,
                    Exclude = false
                })
            ]
        };

        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert execute
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);
        var executeRoot = executeJson.RootElement;

        executeRoot.GetProperty("status").GetString().Should().Be("committed");
        executeRoot.GetProperty("summary").GetProperty("insertedRows").GetInt32().Should().Be(2);
        executeRoot.GetProperty("summary").GetProperty("failedRows").GetInt32().Should().Be(0);

        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var importedTransactions = await dbContext.StockTransactions
            .Where(transaction => transaction.PortfolioId == portfolio.Id && transaction.Ticker == "2330")
            .OrderBy(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.CreatedAt)
            .ToListAsync();

        importedTransactions.Should().HaveCount(2);

        var buyTransaction = importedTransactions.Single(transaction => transaction.TransactionType == TransactionType.Buy);
        var sellTransaction = importedTransactions.Single(transaction => transaction.TransactionType == TransactionType.Sell);

        buyTransaction.Fees.Should().Be(buyPreviewRow.Fees + buyPreviewRow.Taxes);
        sellTransaction.Fees.Should().Be(sellPreviewRow.Fees + sellPreviewRow.Taxes);

        var linkedCurrencyTransactions = await dbContext.CurrencyTransactions
            .Where(transaction => transaction.RelatedStockTransactionId == buyTransaction.Id || transaction.RelatedStockTransactionId == sellTransaction.Id)
            .ToListAsync();

        var buyLinkedTransaction = linkedCurrencyTransactions.Single(transaction => transaction.RelatedStockTransactionId == buyTransaction.Id);
        var sellLinkedTransaction = linkedCurrencyTransactions.Single(transaction => transaction.RelatedStockTransactionId == sellTransaction.Id);

        buyLinkedTransaction.ForeignAmount.Should().Be(1005m);
        sellLinkedTransaction.ForeignAmount.Should().Be(1650m);
    }

    [Fact]
    public async Task PreviewThenExecute_PreservesPreviewValues_ForDateQuantityPriceFees_AndUsesConfirmedTradeSide()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync(
            "Stock Import Preview->Execute Value Consistency",
            currencyCode: "TWD");
        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement"
        };

        // Act 1: preview
        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);

        // Assert preview contract + key value consistency snapshot
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var previewRoot = previewJson.RootElement;

        var sessionId = previewRoot.GetProperty("sessionId").GetGuid();
        var previewRows = previewRoot.GetProperty("rows");
        previewRows.GetArrayLength().Should().BeGreaterThan(0);

        var firstRow = previewRows.EnumerateArray().First();

        var rowNumber = firstRow.GetProperty("rowNumber").GetInt32();
        var tradeDate = firstRow.GetProperty("tradeDate").GetDateTime();
        var quantity = firstRow.GetProperty("quantity").GetDecimal();
        var unitPrice = firstRow.GetProperty("unitPrice").GetDecimal();
        var fees = firstRow.GetProperty("fees").GetDecimal();
        var ticker = firstRow.GetProperty("ticker").GetString();
        var confirmedTradeSide = firstRow.GetProperty("confirmedTradeSide").GetString();

        tradeDate.Date.Should().Be(new DateTime(2026, 1, 22));
        quantity.Should().Be(1000m);
        unitPrice.Should().Be(625m);
        fees.Should().Be(1425m);

        ticker.Should().NotBeNullOrWhiteSpace();
        confirmedTradeSide.Should().Be("buy");

        // Act 2: execute using preview snapshot row
        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.Margin
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = rowNumber,
                    Ticker = ticker,
                    ConfirmedTradeSide = confirmedTradeSide,
                    Exclude = false
                }
            ]
        };

        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert execute contract + confirmedTradeSide row result
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);
        var executeRoot = executeJson.RootElement;

        executeRoot.GetProperty("status").GetString().Should().Be("committed");

        var executeSummary = executeRoot.GetProperty("summary");
        executeSummary.GetProperty("insertedRows").GetInt32().Should().Be(1);
        executeSummary.GetProperty("failedRows").GetInt32().Should().Be(0);

        var executeResults = executeRoot.GetProperty("results");
        executeResults.GetArrayLength().Should().Be(1);

        var executeRow = executeResults.EnumerateArray().Single();
        executeRow.GetProperty("rowNumber").GetInt32().Should().Be(rowNumber);
        executeRow.GetProperty("success").GetBoolean().Should().BeTrue();
        executeRow.GetProperty("confirmedTradeSide").GetString().Should().Be(confirmedTradeSide);
        executeRow.GetProperty("transactionId").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Execute_Blocks_WhenSessionIsUnknown()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Unknown Session Blocking");
        var unknownSessionId = Guid.NewGuid();
        var request = new ExecuteStockImportRequest
        {
            SessionId = unknownSessionId,
            PortfolioId = portfolio.Id,
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 1,
                    Ticker = "2330",
                    ConfirmedTradeSide = "buy",
                    Exclude = false
                }
            ]
        };

        // Act
        var response = await Client.PostAsJsonAsync(ExecuteEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("Import session not found, expired, or already consumed");

        using var scope = Factory.Services.CreateScope();
        var sessionStore = scope.ServiceProvider.GetRequiredService<IStockImportSessionStore>();
        var executionState = await sessionStore.GetExecutionStateAsync(unknownSessionId, CancellationToken.None);
        executionState.Should().BeNull("unknown session execute should not persist failed execution state");
    }

    [Fact]
    public async Task Execute_SecondSubmitSameSession_ShouldReplayPreviousResultWithoutSecondWrite()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Execute Replay Same Session", currencyCode: "TWD");
        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var firstRow = previewJson.RootElement.GetProperty("rows").EnumerateArray().Single();
        var sessionId = previewJson.RootElement.GetProperty("sessionId").GetGuid();

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = firstRow.GetProperty("confirmedTradeSide").GetString(),
                    Exclude = false
                }
            ]
        };

        // Act 1
        var firstExecuteResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert 1
        firstExecuteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstExecuteResponse.Content.ReadAsStringAsync();
        var firstDto = JsonSerializer.Deserialize<StockImportExecuteResponseDto>(firstBody, ApiJsonOptions);
        firstDto.Should().NotBeNull();
        firstDto!.SessionId.Should().Be(sessionId);
        firstDto.IsReplay.Should().BeFalse();
        firstDto.Status.Should().Be("committed");

        // Act 2
        var secondExecuteResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert 2
        secondExecuteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await secondExecuteResponse.Content.ReadAsStringAsync();
        var secondDto = JsonSerializer.Deserialize<StockImportExecuteResponseDto>(secondBody, ApiJsonOptions);
        secondDto.Should().NotBeNull();
        secondDto!.SessionId.Should().Be(sessionId);
        secondDto.IsReplay.Should().BeTrue();
        secondDto.Status.Should().Be(firstDto.Status);
        secondDto.Summary.InsertedRows.Should().Be(firstDto.Summary.InsertedRows);
        secondDto.Results.Should().HaveCount(firstDto.Results.Count);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var importedTransactionsCount = await verifyDbContext.StockTransactions
            .CountAsync(transaction => transaction.PortfolioId == portfolio.Id);
        importedTransactionsCount.Should().Be(firstDto.Summary.InsertedRows, "replayed execute must not create additional rows");

        var persistedSession = await verifyDbContext.StockImportSessions
            .SingleAsync(session => session.SessionId == sessionId);
        persistedSession.ExecutionStatus.Should().Be("completed");
        persistedSession.ExecutionResultJson.Should().NotBeNullOrWhiteSpace();
        persistedSession.SessionSnapshotJson.Should().BeEmpty();
    }

    [Fact]
    public async Task GetImportStatus_ShouldReturnCompletedWithResult_AfterExecute()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Status Query Completed", currencyCode: "TWD");
        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var firstRow = previewJson.RootElement.GetProperty("rows").EnumerateArray().Single();
        var sessionId = previewJson.RootElement.GetProperty("sessionId").GetGuid();

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = firstRow.GetProperty("confirmedTradeSide").GetString(),
                    Exclude = false
                }
            ]
        };

        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var statusResponse = await Client.GetAsync($"{StatusEndpointPrefix}/{sessionId}");

        // Assert
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusBody = await statusResponse.Content.ReadAsStringAsync();
        var statusDto = JsonSerializer.Deserialize<StockImportExecuteStatusResponseDto>(statusBody, ApiJsonOptions);
        statusDto.Should().NotBeNull();
        statusDto!.SessionId.Should().Be(sessionId);
        statusDto.PortfolioId.Should().Be(portfolio.Id);
        statusDto.ExecutionStatus.Should().Be("completed");
        statusDto.CheckpointCursor.Should().NotBeNullOrWhiteSpace();
        statusDto.Rows.Should().NotBeEmpty();
        statusDto.Rows.Should().OnlyContain(row => row.Status == "completed" || row.Status == "failed");
        statusDto.CompletedAtUtc.Should().NotBeNull();
        statusDto.Result.Should().NotBeNull();
        statusDto.Result!.SessionId.Should().Be(sessionId);
        statusDto.Result.IsReplay.Should().BeFalse();
        statusDto.Rows.Should().HaveCount(statusDto.Result.Results.Count);
    }

    [Fact]
    public async Task GetImportStatus_ShouldReturnFailedWithSanitizedMessage_AfterExecuteInternalException()
    {
        // Arrange
        var forcedFailureMessage = "sensitive-internal-detail";
        var testUserId = Guid.NewGuid();
        using var failFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ICurrentUserService>(_ =>
                    new StaticCurrentUserService(testUserId));
                services.AddScoped<ICurrencyTransactionRepository>(_ =>
                    new ThrowingCurrencyTransactionRepository(forcedFailureMessage));
            });
        });

        using var client = failFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateTestToken(testUserId));

        await EnsureUserExistsAsync(failFactory.Services, testUserId);

        var portfolioRequest = new { Description = "Stock Import Failed Status Sanitization", CurrencyCode = "TWD" };
        var createPortfolioResponse = await client.PostAsJsonAsync("/api/portfolios", portfolioRequest);
        createPortfolioResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var portfolio = await createPortfolioResponse.Content.ReadFromJsonAsync<PortfolioDto>();
        portfolio.Should().NotBeNull();

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio!.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var firstRow = previewJson.RootElement.GetProperty("rows").EnumerateArray().Single();
        var sessionId = previewJson.RootElement.GetProperty("sessionId").GetGuid();

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = firstRow.GetProperty("confirmedTradeSide").GetString(),
                    Exclude = false
                }
            ]
        };

        // Act 1: execute triggers internal exception
        var executeResponse = await client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert 1: execute path should also return sanitized message
        executeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var executeError = await executeResponse.Content.ReadFromJsonAsync<ApiErrorResponse>();
        executeError.Should().NotBeNull();
        executeError!.Error.Should().Be("Import execution failed. Please retry later.");
        executeError.Error.Should().NotContain(forcedFailureMessage);

        // Act 2: query status should return sanitized message
        var statusResponse = await client.GetAsync($"{StatusEndpointPrefix}/{sessionId}");

        // Assert 2
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusBody = await statusResponse.Content.ReadAsStringAsync();
        var statusDto = JsonSerializer.Deserialize<StockImportExecuteStatusResponseDto>(statusBody, ApiJsonOptions);
        statusDto.Should().NotBeNull();
        statusDto!.SessionId.Should().Be(sessionId);
        statusDto.PortfolioId.Should().Be(portfolio.Id);
        statusDto.ExecutionStatus.Should().Be("failed");
        statusDto.CheckpointCursor.Should().BeNull();
        statusDto.Rows.Should().BeEmpty();
        statusDto.Message.Should().Be("Import execution failed. Please retry later.");
        statusDto.Message.Should().NotContain(forcedFailureMessage);
        statusDto.Result.Should().BeNull();
        statusDto.CompletedAtUtc.Should().NotBeNull();

        using var verifyScope = failFactory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedSession = await verifyDbContext.StockImportSessions
            .SingleAsync(session => session.SessionId == sessionId);
        persistedSession.ExecutionStatus.Should().Be("failed");
        persistedSession.Message.Should().Be("Import execution failed. Please retry later.");
    }

    [Fact]
    public async Task GetImportStatus_ShouldReturnPending_ForPreviewedNotExecutedSession()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Status Query Pending", currencyCode: "TWD");
        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var sessionId = previewJson.RootElement.GetProperty("sessionId").GetGuid();

        // Act
        var statusResponse = await Client.GetAsync($"{StatusEndpointPrefix}/{sessionId}");

        // Assert
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusBody = await statusResponse.Content.ReadAsStringAsync();
        var statusDto = JsonSerializer.Deserialize<StockImportExecuteStatusResponseDto>(statusBody, ApiJsonOptions);
        statusDto.Should().NotBeNull();
        statusDto!.SessionId.Should().Be(sessionId);
        statusDto.ExecutionStatus.Should().Be("pending");
        statusDto.CheckpointCursor.Should().BeNull();
        statusDto.Rows.Should().BeEmpty();
        statusDto.Result.Should().BeNull();

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedSession = await verifyDbContext.StockImportSessions
            .SingleAsync(session => session.SessionId == sessionId);
        persistedSession.ExecutionStatus.Should().Be("pending");
        persistedSession.SessionSnapshotJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetImportStatus_ShouldReturnProcessing_WhenExecutionMarkerIsStartedBeforeLongTransaction()
    {
        // Arrange
        var testUserId = Guid.NewGuid();
        using var processingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ICurrentUserService>(_ =>
                    new StaticCurrentUserService(testUserId));
                services.AddScoped<IStockTransactionRepository, BlockingStockTransactionRepository>();
            });
        });

        using var client = processingFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateTestToken(testUserId));

        await EnsureUserExistsAsync(processingFactory.Services, testUserId);

        var portfolioRequest = new { Description = "Stock Import Status Query Processing", CurrencyCode = "TWD" };
        var createPortfolioResponse = await client.PostAsJsonAsync("/api/portfolios", portfolioRequest);
        createPortfolioResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var portfolio = await createPortfolioResponse.Content.ReadFromJsonAsync<PortfolioDto>();
        portfolio.Should().NotBeNull();

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio!.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var firstRow = previewJson.RootElement.GetProperty("rows").EnumerateArray().Single();
        var sessionId = previewJson.RootElement.GetProperty("sessionId").GetGuid();

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = firstRow.GetProperty("confirmedTradeSide").GetString(),
                    Exclude = false
                }
            ]
        };

        BlockingStockTransactionRepository.EnableBlocking();

        var executeTask = client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        await BlockingStockTransactionRepository.WaitUntilEnteredAsync();

        // Act
        var statusResponse = await client.GetAsync($"{StatusEndpointPrefix}/{sessionId}");

        // Assert
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusBody = await statusResponse.Content.ReadAsStringAsync();
        var statusDto = JsonSerializer.Deserialize<StockImportExecuteStatusResponseDto>(statusBody, ApiJsonOptions);
        statusDto.Should().NotBeNull();
        statusDto!.SessionId.Should().Be(sessionId);
        statusDto.PortfolioId.Should().Be(portfolio.Id);
        statusDto.ExecutionStatus.Should().Be("processing");
        statusDto.StartedAtUtc.Should().NotBeNull();
        statusDto.CompletedAtUtc.Should().BeNull();
        statusDto.CheckpointCursor.Should().BeNull();
        statusDto.Rows.Should().BeEmpty();
        statusDto.Result.Should().BeNull();

        try
        {
            BlockingStockTransactionRepository.AllowContinue();
            var executeResponse = await executeTask;
            executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            BlockingStockTransactionRepository.DisableBlocking();
        }
    }

    [Fact]
    public async Task GetImportStatus_ShouldReturnNotFound_ForUnknownSession()
    {
        // Arrange
        var unknownSessionId = Guid.NewGuid();

        // Act
        var statusResponse = await Client.GetAsync($"{StatusEndpointPrefix}/{unknownSessionId}");

        // Assert
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusBody = await statusResponse.Content.ReadAsStringAsync();
        var statusDto = JsonSerializer.Deserialize<StockImportExecuteStatusResponseDto>(statusBody, ApiJsonOptions);
        statusDto.Should().NotBeNull();
        statusDto!.SessionId.Should().Be(unknownSessionId);
        statusDto.ExecutionStatus.Should().Be("not_found");
        statusDto.CheckpointCursor.Should().BeNull();
        statusDto.Rows.Should().BeEmpty();
        statusDto.Result.Should().BeNull();
    }

    [Fact]
    public async Task Execute_ReturnsRejectedWithSessionRowMismatch_WhenRowIsNotInPreviewSession()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Session Row Mismatch");
        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var sessionId = previewJson.RootElement.GetProperty("sessionId").GetGuid();

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 999,
                    Ticker = "2330",
                    ConfirmedTradeSide = "buy",
                    Exclude = false
                }
            ]
        };

        // Act
        var response = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        root.GetProperty("status").GetString().Should().Be("rejected");

        var result = root.GetProperty("results").EnumerateArray().Single();
        result.GetProperty("rowNumber").GetInt32().Should().Be(999);
        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("SESSION_ROW_MISMATCH");

        var error = root.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("rowNumber").GetInt32().Should().Be(999);
        error.GetProperty("fieldName").GetString().Should().Be("rowNumber");
        error.GetProperty("errorCode").GetString().Should().Be("SESSION_ROW_MISMATCH");
        error.GetProperty("invalidValue").GetString().Should().Be("999");
    }

    [Fact]
    public async Task Execute_Blocks_WhenSessionBelongsToDifferentPortfolio()
    {
        // Arrange
        var sourcePortfolio = await CreateTestPortfolioAsync(
            "Stock Import Session Source Portfolio",
            currencyCode: "USD");
        var targetPortfolio = await CreateTestPortfolioAsync(
            "Stock Import Session Target Portfolio",
            currencyCode: "EUR");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = sourcePortfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);

        var sessionId = previewJson.RootElement.GetProperty("sessionId").GetGuid();
        var firstRow = previewJson.RootElement.GetProperty("rows").EnumerateArray().First();

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = targetPortfolio.Id,
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = firstRow.GetProperty("confirmedTradeSide").GetString(),
                    Exclude = false
                }
            ]
        };

        // Act
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        executeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var payload = await executeResponse.Content.ReadAsStringAsync();
        payload.Should().Contain("Import session does not match current user or portfolio");
    }

    [Fact]
    public async Task Execute_MismatchedPortfolioAttempt_ShouldNotConsumeSessionForRightfulOwner()
    {
        // Arrange
        var sourcePortfolio = await CreateTestPortfolioAsync(
            "Stock Import Session Replay Guard Source",
            currencyCode: "USD");
        var targetPortfolio = await CreateTestPortfolioAsync(
            "Stock Import Session Replay Guard Target",
            currencyCode: "EUR");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = sourcePortfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);

        var sessionId = previewJson.RootElement.GetProperty("sessionId").GetGuid();
        var firstRow = previewJson.RootElement.GetProperty("rows").EnumerateArray().First();
        var rowNumber = firstRow.GetProperty("rowNumber").GetInt32();
        var ticker = firstRow.GetProperty("ticker").GetString();
        var confirmedTradeSide = firstRow.GetProperty("confirmedTradeSide").GetString();

        var unauthorizedRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = targetPortfolio.Id,
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = rowNumber,
                    Ticker = ticker,
                    ConfirmedTradeSide = confirmedTradeSide,
                    Exclude = true
                }
            ]
        };

        // Act 1: mismatched portfolio attempt
        var unauthorizedResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, unauthorizedRequest);

        // Assert 1: request is forbidden and session remains available
        unauthorizedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using (var verifyScope = Factory.Services.CreateScope())
        {
            var sessionStore = verifyScope.ServiceProvider.GetRequiredService<IStockImportSessionStore>();
            var persistedSession = await sessionStore.GetAsync(sessionId, CancellationToken.None);
            persistedSession.Should().NotBeNull("mismatched portfolio attempt must not consume the preview session");
        }

        var authorizedRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = sourcePortfolio.Id,
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = rowNumber,
                    Ticker = ticker,
                    ConfirmedTradeSide = confirmedTradeSide,
                    Exclude = true
                }
            ]
        };

        // Act 2: rightful owner executes with same session
        var authorizedResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, authorizedRequest);

        // Assert 2: rightful owner can still execute and consume session
        authorizedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var authorizedPayload = await authorizedResponse.Content.ReadAsStringAsync();
        using var authorizedJson = JsonDocument.Parse(authorizedPayload);
        authorizedJson.RootElement.GetProperty("status").GetString().Should().Be("committed");

        using (var verifyScope = Factory.Services.CreateScope())
        {
            var sessionStore = verifyScope.ServiceProvider.GetRequiredService<IStockImportSessionStore>();
            var consumedSession = await sessionStore.GetAsync(sessionId, CancellationToken.None);
            consumedSession.Should().BeNull("session should be consumed by rightful owner execution");
        }
    }

    [Fact]
    public async Task Execute_UsesConfirmedTradeSideInResultRowContract_WhenRejected()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import ConfirmedTradeSide Result Contract");
        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithAmbiguousTradeSide(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);

        var sessionId = previewJson.RootElement.GetProperty("sessionId").GetGuid();
        var previewRows = previewJson.RootElement.GetProperty("rows");
        var firstRow = previewRows.EnumerateArray().Single();

        firstRow.GetProperty("tradeSide").GetString().Should().Be("ambiguous");
        firstRow.GetProperty("confirmedTradeSide").ValueKind.Should().Be(JsonValueKind.Null);

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = null,
                    Exclude = false
                }
            ]
        };

        // Act
        var response = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        root.GetProperty("status").GetString().Should().Be("rejected");

        var summary = root.GetProperty("summary");
        summary.GetProperty("insertedRows").GetInt32().Should().Be(0);
        summary.GetProperty("failedRows").GetInt32().Should().Be(1);

        var result = root.GetProperty("results").EnumerateArray().Single();
        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("TRADE_SIDE_CONFIRMATION_REQUIRED");
        result.TryGetProperty("confirmedTradeSide", out var confirmedTradeSideElement).Should().BeTrue();
        confirmedTradeSideElement.ValueKind.Should().Be(JsonValueKind.Null);

        var error = root.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("fieldName").GetString().Should().Be("confirmedTradeSide");
        error.GetProperty("errorCode").GetString().Should().Be("TRADE_SIDE_CONFIRMATION_REQUIRED");
    }

    [Fact]
    public async Task Execute_ReturnsBadRequest_WhenRowsContainDuplicateRowNumber()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Duplicate RowNumber Validation");
        var request = new ExecuteStockImportRequest
        {
            SessionId = Guid.NewGuid(),
            PortfolioId = portfolio.Id,
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 1,
                    Ticker = "2330",
                    ConfirmedTradeSide = "buy",
                    Exclude = false
                },
                new ExecuteStockImportRowRequest
                {
                    RowNumber = 1,
                    Ticker = "2317",
                    ConfirmedTradeSide = "sell",
                    Exclude = false
                }
            ]
        };

        // Act
        var response = await Client.PostAsJsonAsync(ExecuteEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("Rows.RowNumber contains duplicates");
        payload.Should().Contain("1");
    }

    [Fact]
    public async Task Execute_ReturnsBadRequest_WhenDefaultTopUpUsesExchangeBuy()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import ExchangeBuy Validation", currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);

        var sessionId = previewJson.RootElement.GetProperty("sessionId").GetGuid();
        var firstRow = previewJson.RootElement.GetProperty("rows").EnumerateArray().Single();

        var request = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.ExchangeBuy
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = firstRow.GetProperty("confirmedTradeSide").GetString(),
                    Exclude = false
                }
            ]
        };

        // Act
        var response = await Client.PostAsJsonAsync(ExecuteEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("DefaultBalanceAction.TopUpTransactionType must be one of Deposit, InitialBalance, Interest, or OtherIncome when DefaultBalanceAction.Action is TopUp.");
    }

    [Fact]
    public async Task Execute_PartialPeriodSellWithoutHoldings_WithoutDecision_ShouldAutoCommitWithTraceableSellBeforeBuyDecision()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Sell No Holdings", currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneSellRow(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var root = previewJson.RootElement;

        var firstRow = root.GetProperty("rows").EnumerateArray().Single();
        firstRow.GetProperty("status").GetString().Should().Be("valid");
        firstRow.GetProperty("usesPartialHistoryAssumption").GetBoolean().Should().BeTrue();
        firstRow.GetProperty("actionsRequired").EnumerateArray().Select(x => x.GetString()).Should().Contain("choose_sell_before_buy_handling");

        var previewSellBeforeBuyDecision = firstRow.GetProperty("sellBeforeBuyDecision");
        previewSellBeforeBuyDecision.GetProperty("strategy").GetString().Should().Be("create_adjustment");
        previewSellBeforeBuyDecision.GetProperty("decisionScope").GetString().Should().Be("auto_default");
        previewSellBeforeBuyDecision.GetProperty("reason").GetString().Should().Be("auto_default_for_sell_before_buy");

        var sessionId = root.GetProperty("sessionId").GetGuid();
        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = "sell",
                    Exclude = false
                }
            ]
        };

        // Act
        var response = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        var executeRoot = json.RootElement;

        executeRoot.GetProperty("status").GetString().Should().Be("committed");

        var result = executeRoot.GetProperty("results").EnumerateArray().Single();
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("sellBeforeBuyDecision").GetProperty("strategy").GetString().Should().Be("create_adjustment");
        result.GetProperty("sellBeforeBuyDecision").GetProperty("decisionScope").GetString().Should().Be("auto_default");
        result.GetProperty("sellBeforeBuyDecision").GetProperty("reason").GetString().Should().Be("auto_default_for_sell_before_buy");

        var warning = executeRoot.GetProperty("errors").EnumerateArray().Single();
        warning.GetProperty("errorCode").GetString().Should().Be("PARTIAL_PERIOD_ASSUMPTION");
    }

    [Fact]
    public async Task Execute_PartialPeriodSellWithoutHoldings_WithCreateAdjustment_ShouldCommitWithoutOpeningPosition()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Sell No Holdings CreateAdjustment", currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneSellRow(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var root = previewJson.RootElement;

        var firstRow = root.GetProperty("rows").EnumerateArray().Single();
        var sessionId = root.GetProperty("sessionId").GetGuid();

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = "sell",
                    SellBeforeBuyAction = SellBeforeBuyAction.CreateAdjustment,
                    Exclude = false
                }
            ]
        };

        // Act
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);
        var executeRoot = executeJson.RootElement;

        executeRoot.GetProperty("status").GetString().Should().Be("committed");
        executeRoot.GetProperty("summary").GetProperty("insertedRows").GetInt32().Should().Be(1);
        executeRoot.GetProperty("summary").GetProperty("failedRows").GetInt32().Should().Be(0);

        var result = executeRoot.GetProperty("results").EnumerateArray().Single();
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("errorCode", out var errorCodeElement).Should().BeTrue();
        errorCodeElement.ValueKind.Should().Be(JsonValueKind.Null);
        result.GetProperty("sellBeforeBuyDecision").GetProperty("strategy").GetString().Should().Be("create_adjustment");
        result.GetProperty("sellBeforeBuyDecision").GetProperty("decisionScope").GetString().Should().Be("row_override");
        result.GetProperty("sellBeforeBuyDecision").GetProperty("reason").GetString().Should().Be("row_override");

        var warnings = executeRoot.GetProperty("errors").EnumerateArray().ToList();
        warnings.Should().ContainSingle();
        warnings[0].GetProperty("errorCode").GetString().Should().Be("PARTIAL_PERIOD_ASSUMPTION");

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var importedSell = await verifyDbContext.StockTransactions
            .Where(transaction => transaction.PortfolioId == portfolio.Id && transaction.TransactionType == TransactionType.Sell)
            .OrderByDescending(transaction => transaction.CreatedAt)
            .FirstAsync();

        importedSell.RealizedPnlHome.Should().Be(6544m);

        var openingAdjustment = await verifyDbContext.StockTransactions
            .Where(transaction =>
                transaction.PortfolioId == portfolio.Id
                && transaction.Ticker == "2330"
                && transaction.TransactionType == TransactionType.Adjustment
                && transaction.Notes != null
                && transaction.Notes.Contains("import-execute-adjustment"))
            .SingleAsync();

        openingAdjustment.MarketValueAtImport.Should().Be(10000m);
        openingAdjustment.HistoricalTotalCost.Should().BeNull();

        var linkedToOpeningAdjustment = await verifyDbContext.CurrencyTransactions
            .Where(transaction =>
                transaction.CurrencyLedgerId == portfolio.BoundCurrencyLedgerId
                && transaction.RelatedStockTransactionId == openingAdjustment.Id)
            .ToListAsync();

        var pairedOpeningInitialBalance = linkedToOpeningAdjustment
            .Single(transaction =>
                transaction.TransactionType == CurrencyTransactionType.InitialBalance
                && transaction.Notes == "import-execute-opening-initial-balance");

        pairedOpeningInitialBalance.ForeignAmount.Should().Be(10000m);
        pairedOpeningInitialBalance.TransactionDate.Should().Be(new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc));

        var openingInitialBalanceOffset = linkedToOpeningAdjustment.Should().ContainSingle(transaction =>
            transaction.TransactionType == CurrencyTransactionType.Spend
            && transaction.ForeignAmount == pairedOpeningInitialBalance.ForeignAmount
            && transaction.TransactionDate == pairedOpeningInitialBalance.TransactionDate
            && transaction.Notes == "import-execute-opening-initial-balance-offset").Subject;

        openingInitialBalanceOffset.HomeAmount.Should().NotBeNull();
        openingInitialBalanceOffset.HomeAmount.Should().Be(pairedOpeningInitialBalance.ForeignAmount);
        openingInitialBalanceOffset.ExchangeRate.Should().Be(1.0m);
    }

    [Fact]
    public async Task Execute_PartialPeriodSellWithoutHoldings_WithCreateAdjustmentAndHistoricalTotalCost_ShouldKeepYearAndAggregateReturnsFiniteAndConsistent()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Sell No Holdings Historical Cost Return Guard", currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneSellRow(),
            SelectedFormat = "broker_statement",
            Baseline = new StockImportBaselineRequest
            {
                BaselineDate = new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc),
                OpeningPositions =
                [
                    new StockImportOpeningPositionRequest
                    {
                        Ticker = "2330",
                        Quantity = 100m,
                        TotalCost = 10000m,
                        HistoricalTotalCost = 9800m
                    }
                ]
            }
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var root = previewJson.RootElement;

        var firstRow = root.GetProperty("rows").EnumerateArray().Single();
        var sessionId = root.GetProperty("sessionId").GetGuid();

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = "sell",
                    SellBeforeBuyAction = SellBeforeBuyAction.CreateAdjustment,
                    Exclude = false
                }
            ]
        };

        // Act 1: execute
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert execute
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);
        var executeRoot = executeJson.RootElement;

        executeRoot.GetProperty("status").GetString().Should().Be("committed");

        var warnings = executeRoot.GetProperty("errors").EnumerateArray().ToList();
        warnings.Should().ContainSingle();
        warnings[0].GetProperty("errorCode").GetString().Should().Be("PARTIAL_PERIOD_ASSUMPTION");

        using (var verifyScope = Factory.Services.CreateScope())
        {
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var openingAdjustment = await verifyDbContext.StockTransactions
                .Where(transaction =>
                    transaction.PortfolioId == portfolio.Id
                    && transaction.Ticker == "2330"
                    && transaction.TransactionType == TransactionType.Adjustment
                    && transaction.Notes != null
                    && transaction.Notes.Contains("import-execute-adjustment"))
                .SingleAsync();

            openingAdjustment.MarketValueAtImport.Should().Be(10000m);
            openingAdjustment.HistoricalTotalCost.Should().Be(9800m);

            var linkedToOpeningAdjustment = await verifyDbContext.CurrencyTransactions
                .Where(transaction =>
                    transaction.CurrencyLedgerId == portfolio.BoundCurrencyLedgerId
                    && transaction.RelatedStockTransactionId == openingAdjustment.Id)
                .ToListAsync();

            var pairedOpeningInitialBalance = linkedToOpeningAdjustment
                .Single(transaction =>
                    transaction.TransactionType == CurrencyTransactionType.InitialBalance
                    && transaction.Notes == "import-execute-opening-initial-balance");

            pairedOpeningInitialBalance.ForeignAmount.Should().Be(10000m);

            var openingInitialBalanceOffset = linkedToOpeningAdjustment.Should().ContainSingle(transaction =>
                transaction.TransactionType == CurrencyTransactionType.Spend
                && transaction.ForeignAmount == pairedOpeningInitialBalance.ForeignAmount
                && transaction.TransactionDate == pairedOpeningInitialBalance.TransactionDate
                && transaction.Notes == "import-execute-opening-initial-balance-offset").Subject;

            openingInitialBalanceOffset.HomeAmount.Should().NotBeNull();
            openingInitialBalanceOffset.HomeAmount.Should().Be(pairedOpeningInitialBalance.ForeignAmount);
            openingInitialBalanceOffset.ExchangeRate.Should().Be(1.0m);
        }

        var summaryResponse = await Client.GetAsync($"/api/portfolios/{portfolio.Id}/summary");
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var summaryDto = await summaryResponse.Content.ReadFromJsonAsync<PortfolioSummaryDto>();
        summaryDto.Should().NotBeNull();

        var yearPriceRequest = BuildYearPriceRequestFromSummaryPositions(summaryDto!.Positions);
        yearPriceRequest["2330"] = new YearEndPriceInfo { Price = 165.5m, ExchangeRate = 1m };

        var yearPerformanceResponse = await Client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.Id}/performance/year",
            new CalculateYearPerformanceRequest
            {
                Year = 2026,
                YearStartPrices = yearPriceRequest,
                YearEndPrices = yearPriceRequest
            });
        yearPerformanceResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var yearPayload = await yearPerformanceResponse.Content.ReadAsStringAsync();
        using var yearJson = JsonDocument.Parse(yearPayload);
        AssertYearPerformanceCoverageSignalContract(yearJson.RootElement);

        var yearPerformance = JsonSerializer.Deserialize<YearPerformanceDto>(yearPayload, ApiJsonOptions);
        yearPerformance.Should().NotBeNull();

        AssertFiniteIfHasValue(yearPerformance!.TimeWeightedReturnPercentageSource, nameof(yearPerformance.TimeWeightedReturnPercentageSource));
        AssertFiniteIfHasValue(yearPerformance.ModifiedDietzPercentageSource, nameof(yearPerformance.ModifiedDietzPercentageSource));
        yearPerformance.Xirr.Should().BeNull();
        yearPerformance.XirrPercentage.Should().BeNull();
        yearPerformance.XirrSource.Should().BeNull();
        yearPerformance.XirrPercentageSource.Should().BeNull();


        var aggregateResponse = await Client.PostAsJsonAsync(
            "/api/portfolios/aggregate/performance/year",
            new CalculateYearPerformanceRequest
            {
                Year = 2026,
                YearStartPrices = yearPriceRequest,
                YearEndPrices = yearPriceRequest
            });
        aggregateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var aggregatePerformance = await aggregateResponse.Content.ReadFromJsonAsync<YearPerformanceDto>();
        aggregatePerformance.Should().NotBeNull();

        AssertFiniteIfHasValue(aggregatePerformance!.TimeWeightedReturnPercentage, nameof(aggregatePerformance.TimeWeightedReturnPercentage));
        AssertFiniteIfHasValue(aggregatePerformance.ModifiedDietzPercentage, nameof(aggregatePerformance.ModifiedDietzPercentage));
        aggregatePerformance.SourceCurrency.Should().BeNull();
        aggregatePerformance.StartValueSource.Should().BeNull();
        aggregatePerformance.EndValueSource.Should().BeNull();
        aggregatePerformance.NetContributionsSource.Should().BeNull();
        aggregatePerformance.TotalReturnPercentageSource.Should().BeNull();
        aggregatePerformance.TimeWeightedReturnPercentageSource.Should().BeNull();
        aggregatePerformance.ModifiedDietzPercentageSource.Should().BeNull();
        aggregatePerformance.Xirr.Should().BeNull();
        aggregatePerformance.XirrPercentage.Should().BeNull();
        aggregatePerformance.XirrSource.Should().BeNull();
        aggregatePerformance.XirrPercentageSource.Should().BeNull();

        aggregatePerformance.TimeWeightedReturnPercentage.HasValue.Should().Be(
            yearPerformance.TimeWeightedReturnPercentage.HasValue);
        if (yearPerformance.TimeWeightedReturnPercentage.HasValue)
        {
            aggregatePerformance.TimeWeightedReturnPercentage!.Value.Should().BeApproximately(
                yearPerformance.TimeWeightedReturnPercentage.Value,
                0.0001d);
        }

        aggregatePerformance.ModifiedDietzPercentage.HasValue.Should().Be(
            yearPerformance.ModifiedDietzPercentage.HasValue);
        if (yearPerformance.ModifiedDietzPercentage.HasValue)
        {
            aggregatePerformance.ModifiedDietzPercentage!.Value.Should().BeApproximately(
                yearPerformance.ModifiedDietzPercentage.Value,
                0.0001d);
        }
    }

    [Fact]
    public async Task Execute_PartialPeriodSellThenBuy_WithOpeningDecision_ShouldUseSellIncomeWithoutTopUp()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Partial-Period Sell Then Buy", currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithSellThenBuyRows(),
            SelectedFormat = "broker_statement",
            Baseline = new StockImportBaselineRequest
            {
                BaselineDate = new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc),
                OpeningPositions =
                [
                    new StockImportOpeningPositionRequest
                    {
                        Ticker = "2330",
                        Quantity = 100m,
                        TotalCost = 100000m
                    }
                ]
            }
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var root = previewJson.RootElement;

        var sessionId = root.GetProperty("sessionId").GetGuid();
        var rows = root.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().HaveCount(2);

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            BaselineDecision = new StockImportBaselineExecutionDecisionRequest
            {
                SellBeforeBuyAction = SellBeforeBuyAction.UseOpeningPosition
            },
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = rows[0].GetProperty("rowNumber").GetInt32(),
                    Ticker = rows[0].GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = "sell",
                    Exclude = false
                },
                new ExecuteStockImportRowRequest
                {
                    RowNumber = rows[1].GetProperty("rowNumber").GetInt32(),
                    Ticker = rows[1].GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = "buy",
                    Exclude = false
                }
            ]
        };

        // Act
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);
        var executeRoot = executeJson.RootElement;

        executeRoot.GetProperty("status").GetString().Should().Be("committed");
        executeRoot.GetProperty("summary").GetProperty("insertedRows").GetInt32().Should().Be(2);
        executeRoot.GetProperty("summary").GetProperty("failedRows").GetInt32().Should().Be(0);

        var warnings = executeRoot.GetProperty("errors").EnumerateArray().ToList();
        warnings.Should().ContainSingle();
        warnings[0].GetProperty("errorCode").GetString().Should().Be("PARTIAL_PERIOD_ASSUMPTION");

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var linkedCurrencyTransactions = await verifyDbContext.CurrencyTransactions
            .Where(transaction => transaction.CurrencyLedgerId == portfolio.BoundCurrencyLedgerId)
            .ToListAsync();

        linkedCurrencyTransactions.Should().Contain(transaction => transaction.TransactionType == CurrencyTransactionType.OtherIncome);
        linkedCurrencyTransactions.Should().Contain(transaction => transaction.TransactionType == CurrencyTransactionType.Spend);

        linkedCurrencyTransactions.Should().NotContain(
            transaction => transaction.TransactionType == CurrencyTransactionType.Deposit &&
                transaction.Notes != null && transaction.Notes.Contains("補足買入", StringComparison.Ordinal),
            "先賣出已產生可用餘額時，下一筆買入不應再補足");
    }

    [Fact]
    public async Task Execute_SameDayBuyFirstSellLater_WithBuyRowNumberSmaller_ShouldNotCreateTopUpDeposit()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Same-Day Buy First Sell Later", currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithBuyThenSellRows(),
            SelectedFormat = "broker_statement",
            Baseline = new StockImportBaselineRequest
            {
                BaselineDate = new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc),
                OpeningPositions =
                [
                    new StockImportOpeningPositionRequest
                    {
                        Ticker = "2330",
                        Quantity = 100m,
                        TotalCost = 100000m
                    }
                ]
            }
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        var previewDto = JsonSerializer.Deserialize<StockImportPreviewResponseDto>(previewPayload, ApiJsonOptions);
        previewDto.Should().NotBeNull();

        var previewRows = previewDto!.Rows
            .OrderBy(row => row.RowNumber)
            .ToList();
        previewRows.Should().HaveCount(2);
        previewRows[0].TradeDate!.Value.Date.Should().Be(new DateTime(2026, 1, 22));
        previewRows[1].TradeDate!.Value.Date.Should().Be(new DateTime(2026, 1, 22));

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = previewDto.SessionId,
            PortfolioId = portfolio.Id,
            BaselineDecision = new StockImportBaselineExecutionDecisionRequest
            {
                SellBeforeBuyAction = SellBeforeBuyAction.UseOpeningPosition
            },
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = previewRows[0].RowNumber,
                    Ticker = previewRows[0].Ticker,
                    ConfirmedTradeSide = "buy",
                    Exclude = false
                },
                new ExecuteStockImportRowRequest
                {
                    RowNumber = previewRows[1].RowNumber,
                    Ticker = previewRows[1].Ticker,
                    ConfirmedTradeSide = "sell",
                    Exclude = false
                }
            ]
        };

        // Act
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        var executeDto = JsonSerializer.Deserialize<StockImportExecuteResponseDto>(executePayload, ApiJsonOptions);
        executeDto.Should().NotBeNull();

        executeDto!.Status.Should().Be("committed");
        executeDto.Summary.InsertedRows.Should().Be(2);
        executeDto.Summary.FailedRows.Should().Be(0);
        executeDto.Results.Should().ContainSingle(row => row.RowNumber == previewRows[0].RowNumber && row.Success);
        executeDto.Results.Should().ContainSingle(row => row.RowNumber == previewRows[1].RowNumber && row.Success);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var linkedCurrencyTransactions = await verifyDbContext.CurrencyTransactions
            .Where(transaction => transaction.CurrencyLedgerId == portfolio.BoundCurrencyLedgerId)
            .ToListAsync();

        linkedCurrencyTransactions.Should().Contain(transaction => transaction.TransactionType == CurrencyTransactionType.OtherIncome);
        linkedCurrencyTransactions.Should().Contain(transaction => transaction.TransactionType == CurrencyTransactionType.Spend);
        linkedCurrencyTransactions.Should().NotContain(transaction =>
            transaction.TransactionType == CurrencyTransactionType.Deposit
            && transaction.Notes != null
            && transaction.Notes.Contains("補足買入", StringComparison.Ordinal),
            "同日 buy-first / sell-later（rowNumber buy < sell）執行後不應有多餘 TopUp Deposit");
    }

    [Fact]
    public async Task Execute_ReverseChronologicalSellThenBuyPairs_ShouldCommitWithoutTopUpForKeyRows()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Reverse Chronological Sell/Buy No TopUp", currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithReverseChronologicalSellThenBuyPairs(),
            SelectedFormat = "broker_statement",
            Baseline = new StockImportBaselineRequest
            {
                BaselineDate = new DateTime(2025, 12, 29, 0, 0, 0, DateTimeKind.Utc),
                OpeningPositions =
                [
                    new StockImportOpeningPositionRequest
                    {
                        Ticker = "2330",
                        Quantity = 200m,
                        TotalCost = 120000m
                    }
                ]
            }
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var previewRoot = previewJson.RootElement;

        var sessionId = previewRoot.GetProperty("sessionId").GetGuid();
        var previewRows = previewRoot.GetProperty("rows")
            .EnumerateArray()
            .OrderBy(row => row.GetProperty("rowNumber").GetInt32())
            .ToList();

        previewRows.Should().HaveCount(4);

        previewRows[0].GetProperty("tradeDate").GetDateTime().Date.Should().Be(new DateTime(2026, 1, 7));
        previewRows[0].GetProperty("tradeSide").GetString().Should().Be("sell");

        previewRows[1].GetProperty("tradeDate").GetDateTime().Date.Should().Be(new DateTime(2026, 1, 7));
        previewRows[1].GetProperty("tradeSide").GetString().Should().Be("buy");

        previewRows[2].GetProperty("tradeDate").GetDateTime().Date.Should().Be(new DateTime(2025, 12, 30));
        previewRows[2].GetProperty("tradeSide").GetString().Should().Be("sell");

        previewRows[3].GetProperty("tradeDate").GetDateTime().Date.Should().Be(new DateTime(2025, 12, 30));
        previewRows[3].GetProperty("tradeSide").GetString().Should().Be("buy");

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            BaselineDecision = new StockImportBaselineExecutionDecisionRequest
            {
                SellBeforeBuyAction = SellBeforeBuyAction.UseOpeningPosition
            },
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                .. previewRows.Select(row => new ExecuteStockImportRowRequest
                {
                    RowNumber = row.GetProperty("rowNumber").GetInt32(),
                    Ticker = row.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = row.GetProperty("confirmedTradeSide").GetString(),
                    Exclude = false
                })
            ]
        };

        // Act
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);
        var executeRoot = executeJson.RootElement;

        executeRoot.GetProperty("status").GetString().Should().Be("committed");
        executeRoot.GetProperty("summary").GetProperty("insertedRows").GetInt32().Should().Be(4);
        executeRoot.GetProperty("summary").GetProperty("failedRows").GetInt32().Should().Be(0);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var importedStockTransactions = await verifyDbContext.StockTransactions
            .Where(transaction => transaction.PortfolioId == portfolio.Id && transaction.Ticker == "2330")
            .OrderBy(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.CreatedAt)
            .ToListAsync();

        var importedTradeTransactions = importedStockTransactions
            .Where(transaction => transaction.TransactionType is TransactionType.Buy or TransactionType.Sell)
            .ToList();

        importedTradeTransactions.Should().HaveCount(4);

        var importedTradeIds = importedTradeTransactions
            .Select(transaction => transaction.Id)
            .ToList();

        var linkedCurrencyTransactions = await verifyDbContext.CurrencyTransactions
            .Where(transaction =>
                transaction.CurrencyLedgerId == portfolio.BoundCurrencyLedgerId
                && transaction.RelatedStockTransactionId.HasValue
                && importedTradeIds.Contains(transaction.RelatedStockTransactionId.Value))
            .ToListAsync();

        var keyDates = new[]
        {
            new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 30, 0, 0, 0, DateTimeKind.Utc)
        };

        foreach (var keyDate in keyDates)
        {
            var sellTransaction = importedTradeTransactions.Single(transaction =>
                transaction.TransactionDate == keyDate
                && transaction.TransactionType == TransactionType.Sell);

            var buyTransaction = importedTradeTransactions.Single(transaction =>
                transaction.TransactionDate == keyDate
                && transaction.TransactionType == TransactionType.Buy);

            linkedCurrencyTransactions.Should().Contain(transaction =>
                transaction.RelatedStockTransactionId == sellTransaction.Id
                && transaction.TransactionType == CurrencyTransactionType.OtherIncome);

            linkedCurrencyTransactions.Should().Contain(transaction =>
                transaction.RelatedStockTransactionId == buyTransaction.Id
                && transaction.TransactionType == CurrencyTransactionType.Spend);

            linkedCurrencyTransactions.Should().NotContain(transaction =>
                transaction.RelatedStockTransactionId == buyTransaction.Id
                && transaction.TransactionType == CurrencyTransactionType.Deposit
                && transaction.Notes != null
                && transaction.Notes.Contains("補足買入", StringComparison.Ordinal),
                $"{keyDate:yyyy/MM/dd} 賣出後同日買入應優先使用賣出現金，不應補足買入");
        }

        linkedCurrencyTransactions.Should().NotContain(transaction =>
            transaction.TransactionType == CurrencyTransactionType.Deposit
            && transaction.Notes != null
            && transaction.Notes.Contains("補足買入", StringComparison.Ordinal),
            "新到舊列序下賣後買情境不應 over-topup");
    }

    [Fact]
    public async Task Execute_PartialPeriodSell_WithBaselineCost_ShouldPersistRealizedPnlFromOpeningCostBasis()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Partial-Period Sell Baseline Cost", currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneSellRow(),
            SelectedFormat = "broker_statement",
            Baseline = new StockImportBaselineRequest
            {
                BaselineDate = new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc),
                OpeningPositions =
                [
                    new StockImportOpeningPositionRequest
                    {
                        Ticker = "2330",
                        Quantity = 100m,
                        TotalCost = 6000m
                    }
                ]
            }
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var root = previewJson.RootElement;

        var firstRow = root.GetProperty("rows").EnumerateArray().Single();
        var sessionId = root.GetProperty("sessionId").GetGuid();

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            BaselineDecision = new StockImportBaselineExecutionDecisionRequest
            {
                SellBeforeBuyAction = SellBeforeBuyAction.UseOpeningPosition
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = "sell",
                    Exclude = false
                }
            ]
        };

        // Act
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);
        var executeRoot = executeJson.RootElement;

        executeRoot.GetProperty("status").GetString().Should().Be("committed");
        executeRoot.GetProperty("summary").GetProperty("insertedRows").GetInt32().Should().Be(1);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var importedSell = await verifyDbContext.StockTransactions
            .Where(transaction => transaction.PortfolioId == portfolio.Id && transaction.TransactionType == TransactionType.Sell)
            .OrderByDescending(transaction => transaction.CreatedAt)
            .FirstAsync();

        importedSell.RealizedPnlHome.Should().Be(10544m, "opening baseline totalCost should be used as sell cost basis");
    }

    [Fact]
    public async Task Execute_WithOpeningLedgerBalance_ShouldSeedInitialBalanceAndAvoidTopUpDeposit()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Opening Ledger Balance", currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "broker_statement",
            Baseline = new StockImportBaselineRequest
            {
                BaselineDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                OpeningCashBalance = 626425m,
                OpeningLedgerBalance = null
            }
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var root = previewJson.RootElement;

        var firstRow = root.GetProperty("rows").EnumerateArray().Single();
        var sessionId = root.GetProperty("sessionId").GetGuid();

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = "buy",
                    Exclude = false
                }
            ]
        };

        // Act
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);
        var executeRoot = executeJson.RootElement;

        executeRoot.GetProperty("status").GetString().Should().Be("committed");
        executeRoot.GetProperty("summary").GetProperty("insertedRows").GetInt32().Should().Be(1);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var linkedCurrencyTransactions = await verifyDbContext.CurrencyTransactions
            .Where(transaction => transaction.CurrencyLedgerId == portfolio.BoundCurrencyLedgerId)
            .ToListAsync();

        var expectedAnchorDate = new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc);

        linkedCurrencyTransactions.Should().Contain(transaction =>
            transaction.TransactionType == CurrencyTransactionType.InitialBalance
            && transaction.ForeignAmount == 626425m
            && transaction.TransactionDate == expectedAnchorDate
            && string.Equals(transaction.Notes, "import-execute-opening-ledger-baseline", StringComparison.Ordinal));

        linkedCurrencyTransactions.Should().Contain(transaction => transaction.TransactionType == CurrencyTransactionType.Spend);
        linkedCurrencyTransactions.Should().NotContain(transaction =>
            transaction.TransactionType == CurrencyTransactionType.Deposit
            && transaction.Notes != null
            && transaction.Notes.Contains("補足買入", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Execute_WithCrossYearRows_BrokerStatementShouldUseEarliestIncludedTradeMinusOneForOpeningLedgerAndOpeningAdjustment()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Cross-Year Baseline Anchor Consistency", currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithCrossYearBuyAndSellRows(),
            SelectedFormat = "broker_statement",
            Baseline = new StockImportBaselineRequest
            {
                BaselineDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                OpeningCashBalance = 200000m,
                OpeningLedgerBalance = 200000m
            }
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewDto = await previewResponse.Content.ReadFromJsonAsync<StockImportPreviewResponseDto>();
        previewDto.Should().NotBeNull();

        var rows = previewDto!.Rows
            .OrderBy(row => row.RowNumber)
            .ToList();
        rows.Should().HaveCount(2);

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = previewDto.SessionId,
            PortfolioId = portfolio.Id,
            BaselineDecision = new StockImportBaselineExecutionDecisionRequest
            {
                SellBeforeBuyAction = SellBeforeBuyAction.CreateAdjustment
            },
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = rows[0].RowNumber,
                    Ticker = rows[0].Ticker,
                    ConfirmedTradeSide = rows[0].ConfirmedTradeSide,
                    Exclude = false
                },
                new ExecuteStockImportRowRequest
                {
                    RowNumber = rows[1].RowNumber,
                    Ticker = rows[1].Ticker,
                    ConfirmedTradeSide = rows[1].ConfirmedTradeSide,
                    Exclude = false
                }
            ]
        };

        // Act
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executeDto = await executeResponse.Content.ReadFromJsonAsync<StockImportExecuteResponseDto>();
        executeDto.Should().NotBeNull();
        executeDto!.Status.Should().Be("committed");
        executeDto.Summary.InsertedRows.Should().Be(2);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var openingLedgerBaseline = await verifyDbContext.CurrencyTransactions
            .Where(transaction => transaction.CurrencyLedgerId == portfolio.BoundCurrencyLedgerId
                && transaction.TransactionType == CurrencyTransactionType.InitialBalance
                && transaction.RelatedStockTransactionId == null
                && transaction.Notes == "import-execute-opening-ledger-baseline")
            .SingleAsync();

        var seededAdjustment = await verifyDbContext.StockTransactions
            .Where(transaction => transaction.PortfolioId == portfolio.Id
                && transaction.TransactionType == TransactionType.Adjustment
                && transaction.Notes != null
                && transaction.Notes.Contains("import-execute-adjustment"))
            .SingleAsync();

        var seededOpeningInitialBalance = await verifyDbContext.CurrencyTransactions
            .Where(transaction => transaction.CurrencyLedgerId == portfolio.BoundCurrencyLedgerId
                && transaction.TransactionType == CurrencyTransactionType.InitialBalance
                && transaction.RelatedStockTransactionId == seededAdjustment.Id
                && transaction.Notes == "import-execute-opening-initial-balance")
            .SingleAsync();

        var expectedAnchorDate = new DateTime(2025, 12, 29, 0, 0, 0, DateTimeKind.Utc);

        openingLedgerBaseline.TransactionDate.Should().Be(
            expectedAnchorDate,
            "broker_statement anchor should be earliest included trade date minus one day");

        seededAdjustment.TransactionDate.Should().Be(
            expectedAnchorDate,
            "seeded opening adjustment should reuse the same resolved baseline anchor date");

        seededOpeningInitialBalance.TransactionDate.Should().Be(
            expectedAnchorDate,
            "seeded opening initial balance should reuse the same resolved baseline anchor date");

        openingLedgerBaseline.TransactionDate.Should().Be(
            seededAdjustment.TransactionDate,
            "opening ledger baseline 與 seeded opening adjustment 應使用同一 anchor date");

        seededOpeningInitialBalance.TransactionDate.Should().Be(
            openingLedgerBaseline.TransactionDate,
            "opening ledger baseline 與 seeded opening initial balance 應使用同一 anchor date");
    }

    [Fact]
    public async Task Preview_PartialPeriodSellWithAsOfDate_BrokerStatementShouldIgnoreUserBaselineDateForAnchorSemantics()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Broker Anchor Preview Ignores BaselineDate", currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithReverseDateTwoSells(),
            SelectedFormat = "broker_statement",
            Baseline = new StockImportBaselineRequest
            {
                AsOfDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                BaselineDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                OpeningPositions =
                [
                    new StockImportOpeningPositionRequest
                    {
                        Ticker = "2330",
                        Quantity = 100m,
                        TotalCost = 10000m
                    }
                ]
            }
        };

        // Act
        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);

        // Assert
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var rows = previewJson.RootElement
            .GetProperty("rows")
            .EnumerateArray()
            .OrderBy(row => row.GetProperty("rowNumber").GetInt32())
            .ToList();

        rows.Should().HaveCount(2);

        rows[0].GetProperty("usesPartialHistoryAssumption").GetBoolean().Should().BeFalse(
            "first sell can consume the seeded opening position");

        rows[1].GetProperty("usesPartialHistoryAssumption").GetBoolean().Should().BeTrue(
            "broker_statement preview should evaluate sell-before-buy using earliest trade minus one anchor, not user baseline date");
        rows[1].GetProperty("actionsRequired").EnumerateArray().Select(x => x.GetString()).Should().Contain("choose_sell_before_buy_handling");
    }

    [Fact]
    public async Task Execute_BrokerStatementEarliestIncludedTradeJan2_ShouldAnchorToJan1InsteadOfDec31()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Broker Anchor Jan2 Should Resolve To Jan1", currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithSingleSellOnJan2(),
            SelectedFormat = "broker_statement",
            Baseline = new StockImportBaselineRequest
            {
                AsOfDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                BaselineDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                OpeningLedgerBalance = 200000m,
                OpeningPositions =
                [
                    new StockImportOpeningPositionRequest
                    {
                        Ticker = "2330",
                        Quantity = 100m,
                        TotalCost = 10000m
                    }
                ]
            }
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewDto = await previewResponse.Content.ReadFromJsonAsync<StockImportPreviewResponseDto>();
        previewDto.Should().NotBeNull();
        previewDto!.Rows.Should().ContainSingle();

        var previewRow = previewDto.Rows.Single();

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = previewDto.SessionId,
            PortfolioId = portfolio.Id,
            BaselineDecision = new StockImportBaselineExecutionDecisionRequest
            {
                SellBeforeBuyAction = SellBeforeBuyAction.UseOpeningPosition
            },
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = previewRow.RowNumber,
                    Ticker = previewRow.Ticker,
                    ConfirmedTradeSide = previewRow.ConfirmedTradeSide,
                    Exclude = false
                }
            ]
        };

        // Act
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executeDto = await executeResponse.Content.ReadFromJsonAsync<StockImportExecuteResponseDto>();
        executeDto.Should().NotBeNull();
        executeDto!.Status.Should().Be("committed");
        executeDto.Summary.InsertedRows.Should().Be(1);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var openingLedgerBaseline = await verifyDbContext.CurrencyTransactions
            .Where(transaction => transaction.CurrencyLedgerId == portfolio.BoundCurrencyLedgerId
                && transaction.TransactionType == CurrencyTransactionType.InitialBalance
                && transaction.RelatedStockTransactionId == null
                && transaction.Notes == "import-execute-opening-ledger-baseline")
            .SingleAsync();

        var seededOpeningAdjustment = await verifyDbContext.StockTransactions
            .Where(transaction => transaction.PortfolioId == portfolio.Id
                && transaction.TransactionType == TransactionType.Adjustment
                && transaction.Notes == "import-execute-opening-baseline")
            .SingleAsync();

        var expectedAnchorDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var guardedDate = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        openingLedgerBaseline.TransactionDate.Should().Be(expectedAnchorDate,
            "broker_statement execute anchor should be earliest included trade date minus one day");
        openingLedgerBaseline.TransactionDate.Should().NotBe(guardedDate,
            "broker_statement execute anchor should not apply Jan-1 guard");

        seededOpeningAdjustment.TransactionDate.Should().Be(expectedAnchorDate,
            "seeded opening adjustment should reuse broker_statement strict anchor date");
    }

    [Fact]
    public async Task RegisterPreviewExecuteAndPerformance_UsingSampleCsv_ShouldReturnCalculableDashboardAndPortfolioXirrAfterImport()
    {
        // Arrange
        var registerRequest = new RegisterRequest
        {
            Email = $"import-xirr-calculable-{Guid.NewGuid():N}@example.com",
            Password = "Password123!",
            DisplayName = "Import XIRR Calculable"
        };

        var registerResponse = await Client.PostAsJsonAsync("/api/auth/register", registerRequest);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        auth.Should().NotBeNull();
        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        Factory.TestUserId = auth.User.Id;

        var portfoliosResponse = await Client.GetAsync("/api/portfolios");
        portfoliosResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var portfolios = await portfoliosResponse.Content.ReadFromJsonAsync<List<PortfolioDto>>();
        portfolios.Should().NotBeNullOrEmpty();

        var twdPortfolio = portfolios!
            .SingleOrDefault(p => string.Equals(p.BaseCurrency, "TWD", StringComparison.OrdinalIgnoreCase));
        twdPortfolio.Should().NotBeNull();

        var sampleFixture = await LoadSampleBrokerStatementFixtureAsync();

        var previewResponse = await Client.PostAsJsonAsync(
            PreviewEndpoint,
            new PreviewStockImportRequest
            {
                PortfolioId = twdPortfolio!.Id,
                CsvContent = sampleFixture.CsvContent,
                SelectedFormat = "broker_statement"
            });

        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewDto = await previewResponse.Content.ReadFromJsonAsync<StockImportPreviewResponseDto>();
        previewDto.Should().NotBeNull();
        previewDto!.Rows.Should().HaveCount(sampleFixture.DataRowCount);

        var executeRows = previewDto.Rows
            .Where(row => !string.Equals(row.Status, "invalid", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(row.ConfirmedTradeSide))
            .Select(row =>
            {
                var resolvedTicker = ResolveTickerForExecute(row);
                return new ExecuteStockImportRowRequest
                {
                    RowNumber = row.RowNumber,
                    Ticker = resolvedTicker,
                    ConfirmedTradeSide = row.ConfirmedTradeSide,
                    SellBeforeBuyAction = row.UsesPartialHistoryAssumption
                        ? SellBeforeBuyAction.CreateAdjustment
                        : null,
                    Exclude = string.IsNullOrWhiteSpace(resolvedTicker)
                };
            })
            .ToList();

        executeRows.Should().Contain(row => !row.Exclude);

        var executeResponse = await Client.PostAsJsonAsync(
            ExecuteEndpoint,
            new ExecuteStockImportRequest
            {
                SessionId = previewDto.SessionId,
                PortfolioId = twdPortfolio.Id,
                BaselineDecision = new StockImportBaselineExecutionDecisionRequest
                {
                    SellBeforeBuyAction = SellBeforeBuyAction.CreateAdjustment
                },
                DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
                {
                    Action = BalanceAction.Margin
                },
                Rows = executeRows
            });

        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executeDto = await executeResponse.Content.ReadFromJsonAsync<StockImportExecuteResponseDto>();
        executeDto.Should().NotBeNull();
        executeDto!.Status.Should().Be("committed");
        executeDto.Summary.InsertedRows.Should().BeGreaterThan(0);

        var summaryResponse = await Client.GetAsync($"/api/portfolios/{twdPortfolio.Id}/summary");
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await summaryResponse.Content.ReadFromJsonAsync<PortfolioSummaryDto>();
        summary.Should().NotBeNull();
        summary!.Positions.Should().NotBeEmpty();

        var currentPrices = BuildCurrentPriceRequestFromSummaryPositions(summary.Positions);
        currentPrices.Should().NotBeEmpty();

        // Act: Portfolio page XIRR API path
        var portfolioXirrResponse = await Client.PostAsJsonAsync(
            $"/api/portfolios/{twdPortfolio.Id}/xirr",
            new CalculateXirrRequest
            {
                CurrentPrices = currentPrices,
                AsOfDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc)
            });

        portfolioXirrResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var portfolioXirr = await portfolioXirrResponse.Content.ReadFromJsonAsync<XirrResultDto>();
        portfolioXirr.Should().NotBeNull();

        // Act: Dashboard aggregate XIRR API path
        var aggregateXirrResponse = await Client.PostAsJsonAsync(
            "/api/portfolios/aggregate/xirr",
            new CalculateXirrRequest
            {
                CurrentPrices = currentPrices,
                AsOfDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc)
            });

        aggregateXirrResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var aggregateXirr = await aggregateXirrResponse.Content.ReadFromJsonAsync<XirrResultDto>();
        aggregateXirr.Should().NotBeNull();

        portfolioXirr!.CashFlowCount.Should().BeGreaterThanOrEqualTo(2,
            "匯入後應包含買賣/初始化調整與期末市值現金流，才可計算 XIRR");
        aggregateXirr!.CashFlowCount.Should().BeGreaterThanOrEqualTo(2,
            "Dashboard aggregate XIRR 應與 Portfolio XIRR 一樣具備足夠現金流");

        portfolioXirr.Xirr.Should().NotBeNull(
            $"Portfolio XIRR should be calculable after import. cashFlowCount={portfolioXirr.CashFlowCount}");
        aggregateXirr.Xirr.Should().NotBeNull(
            $"Aggregate XIRR should be calculable after import. cashFlowCount={aggregateXirr.CashFlowCount}");

        double.IsNaN(portfolioXirr.Xirr!.Value).Should().BeFalse();
        double.IsInfinity(portfolioXirr.Xirr.Value).Should().BeFalse();
        double.IsNaN(aggregateXirr.Xirr!.Value).Should().BeFalse();
        double.IsInfinity(aggregateXirr.Xirr.Value).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_SellWithExistingHoldings_DoesNotCreateTopUpDeposit()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Stock Import Sell With Holdings", currencyCode: "TWD");

        using (var scope = Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var buyTransaction = new StockTransaction(
                portfolioId: portfolio.Id,
                transactionDate: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                ticker: "2330",
                transactionType: TransactionType.Buy,
                shares: 1000m,
                pricePerShare: 100m,
                exchangeRate: 1.0m,
                fees: 0m,
                currencyLedgerId: portfolio.BoundCurrencyLedgerId,
                notes: null,
                market: StockMarket.TW,
                currency: Currency.TWD);

            dbContext.StockTransactions.Add(buyTransaction);
            await dbContext.SaveChangesAsync();
        }

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneSellRow(),
            SelectedFormat = "broker_statement"
        };

        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var root = previewJson.RootElement;

        var sessionId = root.GetProperty("sessionId").GetGuid();
        var firstRow = root.GetProperty("rows").EnumerateArray().Single();

        var executeRequest = new ExecuteStockImportRequest
        {
            SessionId = sessionId,
            PortfolioId = portfolio.Id,
            DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
            {
                Action = BalanceAction.TopUp,
                TopUpTransactionType = CurrencyTransactionType.Deposit
            },
            Rows =
            [
                new ExecuteStockImportRowRequest
                {
                    RowNumber = firstRow.GetProperty("rowNumber").GetInt32(),
                    Ticker = firstRow.GetProperty("ticker").GetString(),
                    ConfirmedTradeSide = "sell",
                    Exclude = false
                }
            ]
        };

        // Act
        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);
        var executeRoot = executeJson.RootElement;

        executeRoot.GetProperty("status").GetString().Should().Be("committed");
        executeRoot.GetProperty("summary").GetProperty("insertedRows").GetInt32().Should().Be(1);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var importedSell = await verifyDbContext.StockTransactions
            .Where(transaction => transaction.PortfolioId == portfolio.Id && transaction.TransactionType == TransactionType.Sell)
            .OrderByDescending(transaction => transaction.CreatedAt)
            .FirstAsync();

        var linkedCurrencyTransactions = await verifyDbContext.CurrencyTransactions
            .Where(transaction => transaction.RelatedStockTransactionId == importedSell.Id)
            .ToListAsync();

        linkedCurrencyTransactions.Should().ContainSingle();
        linkedCurrencyTransactions.Single().TransactionType.Should().Be(CurrencyTransactionType.OtherIncome);

        var depositLinkedToSell = linkedCurrencyTransactions.Any(transaction => transaction.TransactionType == CurrencyTransactionType.Deposit);
        depositLinkedToSell.Should().BeFalse("賣出列不應觸發補足 deposit");
    }

    private static async Task EnsureUserExistsAsync(IServiceProvider services, Guid userId)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existingUser = await context.Users.FindAsync(userId);
        if (existingUser is not null)
        {
            return;
        }

        var user = new User("test@example.com", "password", "Test User");
        typeof(User).GetProperty("Id")!.SetValue(user, userId);
        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    private sealed class StaticCurrentUserService(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public string? Email => "test@example.com";
        public bool IsAuthenticated => true;
    }

    private sealed class ThrowingCurrencyTransactionRepository(string errorMessage) : ICurrencyTransactionRepository
    {
        public Task<CurrencyTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<CurrencyTransaction?> GetByStockTransactionIdAsync(Guid stockTransactionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<CurrencyTransaction>> GetByStockTransactionIdAllAsync(Guid stockTransactionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<CurrencyTransaction>> GetByLedgerIdAsync(Guid ledgerId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<CurrencyTransaction>> GetByLedgerIdOrderedAsync(Guid ledgerId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(errorMessage);

        public Task<CurrencyTransaction> AddAsync(CurrencyTransaction transaction, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task UpdateAsync(CurrencyTransaction transaction, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class BlockingStockTransactionRepository : IStockTransactionRepository
    {
        private static TaskCompletionSource<bool> _enteredGate =
            CreateCompletionSource();

        private static TaskCompletionSource<bool> _continueGate =
            CreateCompletionSource();

        private static volatile bool _blockingEnabled;

        public static Task WaitUntilEnteredAsync()
            => _enteredGate.Task;

        public static void EnableBlocking()
        {
            _blockingEnabled = true;
            _enteredGate = CreateCompletionSource();
            _continueGate = CreateCompletionSource();
        }

        public static void DisableBlocking()
        {
            _blockingEnabled = false;
            _continueGate.TrySetResult(true);
        }

        public static void AllowContinue()
            => _continueGate.TrySetResult(true);

        private static TaskCompletionSource<bool> CreateCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<StockTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<StockTransaction?>(null);

        public async Task<IReadOnlyList<StockTransaction>> GetByPortfolioIdAsync(Guid portfolioId, CancellationToken cancellationToken = default)
        {
            if (!_blockingEnabled)
            {
                return [];
            }

            _enteredGate.TrySetResult(true);
            await _continueGate.Task.WaitAsync(cancellationToken);
            return [];
        }

        public Task<IReadOnlyList<StockTransaction>> GetByTickerAsync(Guid portfolioId, string ticker, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StockTransaction>>([]);

        public Task<StockTransaction> AddAsync(StockTransaction transaction, CancellationToken cancellationToken = default)
            => Task.FromResult(transaction);

        public Task UpdateAsync(StockTransaction transaction, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private static async Task<SampleBrokerStatementFixture> LoadSampleBrokerStatementFixtureAsync()
    {
        var fixturePath = ResolveSampleBrokerStatementFixturePath();
        var csvContent = await File.ReadAllTextAsync(fixturePath, CancellationToken.None);
        var dataRowCount = CountCsvDataRows(csvContent);

        dataRowCount.Should().BeGreaterThan(0, "sample fixture should include at least one data row");

        return new SampleBrokerStatementFixture(csvContent, dataRowCount);
    }

    private static string ResolveSampleBrokerStatementFixturePath()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, SampleBrokerStatementFixtureFileName);

        // Prefer repository fixture copy for deterministic test behavior across environments.
        if (File.Exists(fixturePath))
        {
            return fixturePath;
        }

        if (File.Exists(SourceBrokerStatementFixtureAbsolutePath))
        {
            return SourceBrokerStatementFixtureAbsolutePath;
        }

        throw new FileNotFoundException(
            $"Sample broker statement fixture not found at '{fixturePath}' nor '{SourceBrokerStatementFixtureAbsolutePath}'.");
    }

    private static int CountCsvDataRows(string csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return 0;
        }

        using var reader = new StringReader(csvContent);
        _ = reader.ReadLine(); // header row

        var dataRowCount = 0;
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                dataRowCount++;
            }
        }

        return dataRowCount;
    }

    private static string? ResolveTickerForExecute(StockImportPreviewRowDto row)
    {
        if (!string.IsNullOrWhiteSpace(row.Ticker))
        {
            return row.Ticker;
        }

        if (!string.IsNullOrWhiteSpace(row.RawSecurityName)
            && TwSecurityNameMappings.TryGetValue(row.RawSecurityName.Trim(), out var mappedTicker))
        {
            return mappedTicker;
        }

        return null;
    }

    private static Dictionary<string, YearEndPriceInfo> BuildYearPriceRequestFromSummaryPositions(
        IReadOnlyList<StockPositionDto> positions)
    {
        Dictionary<string, YearEndPriceInfo> prices = new(StringComparer.OrdinalIgnoreCase);

        foreach (var position in positions)
        {
            if (string.IsNullOrWhiteSpace(position.Ticker))
            {
                continue;
            }

            var normalizedTicker = position.Ticker.Trim().ToUpperInvariant();
            var resolvedPrice = position.CurrentPrice.HasValue && position.CurrentPrice.Value > 0m
                ? position.CurrentPrice.Value
                : Math.Max(position.AverageCostPerShareSource, 1m);

            var resolvedExchangeRate = position.CurrentExchangeRate.HasValue && position.CurrentExchangeRate.Value > 0m
                ? position.CurrentExchangeRate.Value
                : 1m;

            prices[normalizedTicker] = new YearEndPriceInfo
            {
                Price = resolvedPrice,
                ExchangeRate = resolvedExchangeRate
            };
        }

        return prices;
    }

    private static Dictionary<string, CurrentPriceInfo> BuildCurrentPriceRequestFromSummaryPositions(
        IReadOnlyList<StockPositionDto> positions)
    {
        Dictionary<string, CurrentPriceInfo> prices = new(StringComparer.OrdinalIgnoreCase);

        foreach (var position in positions)
        {
            if (string.IsNullOrWhiteSpace(position.Ticker))
            {
                continue;
            }

            var normalizedTicker = position.Ticker.Trim().ToUpperInvariant();
            var resolvedPrice = position.CurrentPrice.HasValue && position.CurrentPrice.Value > 0m
                ? position.CurrentPrice.Value
                : Math.Max(position.AverageCostPerShareSource, 1m);

            var resolvedExchangeRate = position.CurrentExchangeRate.HasValue && position.CurrentExchangeRate.Value > 0m
                ? position.CurrentExchangeRate.Value
                : 1m;

            prices[normalizedTicker] = new CurrentPriceInfo
            {
                Price = resolvedPrice,
                ExchangeRate = resolvedExchangeRate
            };
        }

        return prices;
    }

    private static string BuildBrokerStatementCsvWithOneLateYearBuyRow()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2025/12/30","1,000","-100,000","100","100,000","0","0","0","MD0001","台幣",""
""";
    }

    private static void AssertYearPerformanceCoverageSignalContract(JsonElement yearPerformanceRoot)
    {
        yearPerformanceRoot.TryGetProperty("coverageStartDate", out var coverageStartDate).Should().BeTrue();
        (coverageStartDate.ValueKind == JsonValueKind.Null || coverageStartDate.ValueKind == JsonValueKind.String)
            .Should().BeTrue();

        yearPerformanceRoot.TryGetProperty("coverageDays", out var coverageDays).Should().BeTrue();
        (coverageDays.ValueKind == JsonValueKind.Null || coverageDays.ValueKind == JsonValueKind.Number)
            .Should().BeTrue();

        yearPerformanceRoot.TryGetProperty("hasOpeningBaseline", out var hasOpeningBaseline).Should().BeTrue();
        (hasOpeningBaseline.ValueKind == JsonValueKind.Null
            || hasOpeningBaseline.ValueKind == JsonValueKind.True
            || hasOpeningBaseline.ValueKind == JsonValueKind.False)
            .Should().BeTrue();

        yearPerformanceRoot.TryGetProperty("usesPartialHistoryAssumption", out var usesPartialHistoryAssumption).Should().BeTrue();
        (usesPartialHistoryAssumption.ValueKind == JsonValueKind.Null
            || usesPartialHistoryAssumption.ValueKind == JsonValueKind.True
            || usesPartialHistoryAssumption.ValueKind == JsonValueKind.False)
            .Should().BeTrue();

        yearPerformanceRoot.TryGetProperty("xirrReliability", out var xirrReliability).Should().BeTrue();
        (xirrReliability.ValueKind == JsonValueKind.Null || xirrReliability.ValueKind == JsonValueKind.String)
            .Should().BeTrue();

        yearPerformanceRoot.TryGetProperty("shouldDegradeReturnDisplay", out var shouldDegradeReturnDisplay).Should().BeTrue();
        (shouldDegradeReturnDisplay.ValueKind == JsonValueKind.True
            || shouldDegradeReturnDisplay.ValueKind == JsonValueKind.False)
            .Should().BeTrue();

        yearPerformanceRoot.TryGetProperty("returnDisplayDegradeReasonCode", out var degradeReasonCode).Should().BeTrue();
        (degradeReasonCode.ValueKind == JsonValueKind.Null || degradeReasonCode.ValueKind == JsonValueKind.String)
            .Should().BeTrue();

        yearPerformanceRoot.TryGetProperty("returnDisplayDegradeReasonMessage", out var degradeReasonMessage).Should().BeTrue();
        (degradeReasonMessage.ValueKind == JsonValueKind.Null || degradeReasonMessage.ValueKind == JsonValueKind.String)
            .Should().BeTrue();

        yearPerformanceRoot.TryGetProperty("hasRecentLargeInflowWarning", out var hasRecentLargeInflowWarning).Should().BeTrue();
        (hasRecentLargeInflowWarning.ValueKind == JsonValueKind.True
            || hasRecentLargeInflowWarning.ValueKind == JsonValueKind.False)
            .Should().BeTrue();

        yearPerformanceRoot.TryGetProperty("recentLargeInflowWarningMessage", out var recentLargeInflowWarningMessage).Should().BeTrue();
        (recentLargeInflowWarningMessage.ValueKind == JsonValueKind.Null || recentLargeInflowWarningMessage.ValueKind == JsonValueKind.String)
            .Should().BeTrue();

        if (coverageDays.ValueKind == JsonValueKind.Number)
        {
            coverageDays.GetInt32().Should().BeGreaterThanOrEqualTo(0);
        }

        if (xirrReliability.ValueKind == JsonValueKind.String)
        {
            xirrReliability.GetString().Should().NotBeNullOrWhiteSpace();
        }

        if (degradeReasonCode.ValueKind == JsonValueKind.String)
        {
            degradeReasonCode.GetString().Should().NotBeNullOrWhiteSpace();
        }

        if (degradeReasonMessage.ValueKind == JsonValueKind.String)
        {
            degradeReasonMessage.GetString().Should().NotBeNullOrWhiteSpace();
        }

        if (hasRecentLargeInflowWarning.ValueKind == JsonValueKind.True)
        {
            recentLargeInflowWarningMessage.GetString().Should().Be("近期大額資金異動可能導致資金加權報酬率短期波動。");
        }
    }

    private static void AssertFiniteIfHasValue(double? value, string fieldName)
    {
        if (!value.HasValue)
        {
            return;
        }

        double.IsNaN(value.Value).Should().BeFalse($"{fieldName} should not be NaN");
        double.IsInfinity(value.Value).Should().BeFalse($"{fieldName} should not be Infinity");
    }

    private static string BuildBrokerStatementCsvWithOneRow()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2026/01/22","1,000","-626,425","625","625,000","1,425","0","0","A0001","台幣",""
""";
    }

    private static string BuildBrokerStatementCsvWithOneSellRow()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2026/01/22","100","16,544","165.5","16,550","6","0","0","A0001","台幣",""
""";
    }

    private static string BuildBrokerStatementCsvWithSingleSellOnJan2()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2026/01/02","100","16,544","165.5","16,550","6","0","0","JN0201","台幣",""
""";
    }

    private static string BuildBrokerStatementCsvWithCrossYearBuyAndSellRows()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"鴻海","2317","2025/12/30","1","-10","10","10","0","0","0","CY0001","台幣",""
"台積電","2330","2026/01/07","100","16,544","165.5","16,550","6","0","0","CY0002","台幣",""
""";
    }

    private static string BuildBrokerStatementCsvWithReverseDateTwoSells()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2026/01/22","100","16,544","165.5","16,550","6","0","0","RV0001","台幣",""
"台積電","2330","2026/01/21","100","16,544","165.5","16,550","6","0","0","RV0002","台幣",""
""";
    }

    private static string BuildCathayBrokerStatementCsvWithoutTickerHeader()
    {
        return """
股名,日期,成交股數,淨收付額,成交均價,成交價金,手續費,證交稅,稅款,委託書號,幣別,備註
"融程電","2026/01/22","1,000","155,969","156.5","156,500","62","469","0","a7776","台幣",""
"玉山金","2026/01/12","1,000","-32,562","32.55","32,550","12","0","0","a9969","台幣",""
""";
    }

    private static string BuildBrokerStatementCsvWithBuyAndSellTaxes()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2026/01/20","1","-1,005","1,000","1,000","2","3","0","B0001","台幣",""
"台積電","2330","2026/01/21","1","1,650","1,660","660","4","6","0","S0001","台幣",""
""";
    }

    private static string BuildBrokerStatementCsvWithTwoTaxColumns()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,證交稅,委託書號,幣別,備註
"台積電","2330","2026/01/22","1,000","155,972","156","156,000","12","7","0","9","A0002","台幣",""
""";
    }

    private static string BuildBrokerStatementCsvWithAmbiguousTradeSide()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2026/01/22","1,000","0","625","625,000","1,425","0","0","A0001","台幣",""
""";
    }

    private static string BuildBrokerStatementCsvWithBuyThenSellRows()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2026/01/22","100","-50,000","500","50,000","0","0","0","B0001","台幣",""
"台積電","2330","2026/01/22","100","100,000","1,000","100,000","0","0","0","S0001","台幣",""
""";
    }

    private static string BuildBrokerStatementCsvWithSellThenBuyRows()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2026/01/22","100","100,000","1,000","100,000","0","0","0","S0001","台幣",""
"台積電","2330","2026/01/22","100","-50,000","500","50,000","0","0","0","B0001","台幣",""
""";
    }

    private static string BuildBrokerStatementCsvWithReverseChronologicalSellThenBuyPairs()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2026/1/7","100","66,000","660","66,000","0","0","0","S0107","台幣",""
"台積電","2330","2026/1/7","100","-55,000","550","55,000","0","0","0","B0107","台幣",""
"台積電","2330","2025/12/30","100","61,000","610","61,000","0","0","0","S1230","台幣",""
"台積電","2330","2025/12/30","100","-52,000","520","52,000","0","0","0","B1230","台幣",""
""";
    }
}
