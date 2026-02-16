using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using InvestmentTracker.API.Tests.Integration;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.API.Tests.Controllers;

/// <summary>
/// Regression tests for legacy CSV import preview/execute behavior.
/// </summary>
public class StockTransactionsLegacyImportRegressionTests(CustomWebApplicationFactory factory)
    : IntegrationTestBase(factory)
{
    private const string PreviewEndpoint = "/api/stocktransactions/import/preview";
    private const string ExecuteEndpoint = "/api/stocktransactions/import/execute";

    [Fact]
    public async Task LegacyPreviewThenExecute_PreservesKeyValues_AndCommitsSuccessfully()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync(
            "Legacy CSV Preview->Execute Regression",
            currencyCode: "TWD");

        var previewRequest = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildLegacyCsvWithOneBuyRow(),
            SelectedFormat = "legacy_csv"
        };

        // Act 1: preview
        var previewResponse = await Client.PostAsJsonAsync(PreviewEndpoint, previewRequest);

        // Assert preview shape + key values
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewPayload = await previewResponse.Content.ReadAsStringAsync();
        using var previewJson = JsonDocument.Parse(previewPayload);
        var previewRoot = previewJson.RootElement;

        previewRoot.GetProperty("detectedFormat").GetString().Should().Be("legacy_csv");
        previewRoot.GetProperty("selectedFormat").GetString().Should().Be("legacy_csv");

        var summary = previewRoot.GetProperty("summary");
        summary.GetProperty("totalRows").GetInt32().Should().Be(1);
        summary.GetProperty("validRows").GetInt32().Should().Be(1);
        summary.GetProperty("requiresActionRows").GetInt32().Should().Be(0);
        summary.GetProperty("invalidRows").GetInt32().Should().Be(0);

        var sessionId = previewRoot.GetProperty("sessionId").GetGuid();
        var firstRow = previewRoot.GetProperty("rows").EnumerateArray().Single();

        var rowNumber = firstRow.GetProperty("rowNumber").GetInt32();
        var tradeDate = firstRow.GetProperty("tradeDate").GetDateTime();
        var quantity = firstRow.GetProperty("quantity").GetDecimal();
        var unitPrice = firstRow.GetProperty("unitPrice").GetDecimal();
        var fees = firstRow.GetProperty("fees").GetDecimal();
        var taxes = firstRow.GetProperty("taxes").GetDecimal();
        var netSettlement = firstRow.GetProperty("netSettlement").GetDecimal();
        var ticker = firstRow.GetProperty("ticker").GetString();
        var tradeSide = firstRow.GetProperty("tradeSide").GetString();
        var confirmedTradeSide = firstRow.GetProperty("confirmedTradeSide").GetString();

        tradeDate.Date.Should().Be(new DateTime(2026, 1, 22));
        quantity.Should().Be(1000m);
        unitPrice.Should().Be(625m);
        fees.Should().Be(1425m);
        taxes.Should().Be(0m);
        netSettlement.Should().Be(-626425m);
        ticker.Should().Be("2330");
        tradeSide.Should().Be("buy");
        confirmedTradeSide.Should().Be("buy");

        // Act 2: execute
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
                    RowNumber = rowNumber,
                    Ticker = ticker,
                    ConfirmedTradeSide = confirmedTradeSide,
                    Exclude = false
                }
            ]
        };

        var executeResponse = await Client.PostAsJsonAsync(ExecuteEndpoint, executeRequest);

        // Assert execute
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executePayload = await executeResponse.Content.ReadAsStringAsync();
        using var executeJson = JsonDocument.Parse(executePayload);
        var executeRoot = executeJson.RootElement;

        executeRoot.GetProperty("status").GetString().Should().Be("committed");

        var executeSummary = executeRoot.GetProperty("summary");
        executeSummary.GetProperty("insertedRows").GetInt32().Should().Be(1);
        executeSummary.GetProperty("failedRows").GetInt32().Should().Be(0);

        var resultRow = executeRoot.GetProperty("results").EnumerateArray().Single();
        resultRow.GetProperty("rowNumber").GetInt32().Should().Be(rowNumber);
        resultRow.GetProperty("success").GetBoolean().Should().BeTrue();
        resultRow.GetProperty("confirmedTradeSide").GetString().Should().Be("buy");
        resultRow.GetProperty("transactionId").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Preview_UsesManualBrokerOverride_OverDetectedLegacyFormat()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Legacy CSV Manual Broker Override");
        var request = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildLegacyCsvWithOneBuyRow(),
            SelectedFormat = "broker_statement"
        };

        // Act
        var response = await Client.PostAsJsonAsync(PreviewEndpoint, request);

        // Assert: selected format override takes precedence for routing
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        root.GetProperty("detectedFormat").GetString().Should().Be("legacy_csv");
        root.GetProperty("selectedFormat").GetString().Should().Be("broker_statement");
        root.GetProperty("rows").GetArrayLength().Should().Be(0);

        var errors = root.GetProperty("errors").EnumerateArray().ToList();
        errors.Should().NotBeEmpty();
        errors.Should().Contain(e =>
            e.GetProperty("errorCode").GetString() == "CSV_HEADER_MISSING" &&
            e.GetProperty("fieldName").GetString() == "netSettlement");
    }

    [Fact]
    public async Task Preview_UsesManualLegacyOverride_OverDetectedBrokerFormat()
    {
        // Arrange
        var portfolio = await CreateTestPortfolioAsync("Broker CSV Manual Legacy Override");
        var request = new PreviewStockImportRequest
        {
            PortfolioId = portfolio.Id,
            CsvContent = BuildBrokerStatementCsvWithOneRow(),
            SelectedFormat = "legacy_csv"
        };

        // Act
        var response = await Client.PostAsJsonAsync(PreviewEndpoint, request);

        // Assert: selected format override takes precedence for routing
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        root.GetProperty("detectedFormat").GetString().Should().Be("broker_statement");
        root.GetProperty("selectedFormat").GetString().Should().Be("legacy_csv");
        root.GetProperty("rows").GetArrayLength().Should().Be(0);

        var errors = root.GetProperty("errors").EnumerateArray().ToList();
        errors.Should().NotBeEmpty();
        errors.Should().Contain(e =>
            e.GetProperty("errorCode").GetString() == "CSV_HEADER_MISSING" &&
            e.GetProperty("fieldName").GetString() == "tradeType");
    }

    private static string BuildLegacyCsvWithOneBuyRow()
    {
        return """
transactionDate,ticker,transactionType,shares,pricePerShare,fees,currency
2026-01-22,2330,buy,1000,625,1425,TWD
""";
    }

    private static string BuildBrokerStatementCsvWithOneRow()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2026/01/22","1,000","-626,425","625","625,000","1,425","0","0","A0001","台幣",""
""";
    }
}
