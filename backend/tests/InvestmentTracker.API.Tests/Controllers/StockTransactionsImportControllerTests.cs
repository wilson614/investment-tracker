using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.API.Tests.Integration;

namespace InvestmentTracker.API.Tests.Controllers;

/// <summary>
/// Contract tests for stock import preview/execute endpoints.
/// </summary>
public class StockTransactionsImportControllerTests(CustomWebApplicationFactory factory)
    : IntegrationTestBase(factory)
{
    private const string PreviewEndpoint = "/api/stocktransactions/import/preview";
    private const string ExecuteEndpoint = "/api/stocktransactions/import/execute";

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
                }
            ]
        };

        // Act
        var response = await Client.PostAsJsonAsync(ExecuteEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("Import session not found or expired");
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

    private static string BuildBrokerStatementCsvWithOneRow()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2026/01/22","1,000","-626,425","625","625,000","1,425","0","0","A0001","台幣",""
""";
    }

    private static string BuildBrokerStatementCsvWithAmbiguousTradeSide()
    {
        return """
股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註
"台積電","2330","2026/01/22","1,000","0","625","625,000","1,425","0","0","A0001","台幣",""
""";
    }
}
