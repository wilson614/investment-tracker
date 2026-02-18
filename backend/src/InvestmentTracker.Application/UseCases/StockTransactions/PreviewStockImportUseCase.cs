using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

public interface IPreviewStockImportUseCase
{
    Task<StockImportPreviewResponseDto> ExecuteAsync(
        PreviewStockImportRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PreviewStockImportUseCase(
    IPortfolioRepository portfolioRepository,
    ICurrentUserService currentUserService,
    IStockImportParser stockImportParser,
    IStockImportSymbolResolver stockImportSymbolResolver,
    IStockImportSessionStore stockImportSessionStore,
    ILogger<PreviewStockImportUseCase> logger) : IPreviewStockImportUseCase
{
    private const string StatusValid = "valid";
    private const string StatusRequiresUserAction = "requires_user_action";
    private const string StatusInvalid = "invalid";

    public async Task<StockImportPreviewResponseDto> ExecuteAsync(
        PreviewStockImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolio = await portfolioRepository.GetByIdAsync(request.PortfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", request.PortfolioId);

        if (portfolio.UserId != userId)
        {
            throw new AccessDeniedException();
        }

        var parseResult = stockImportParser.Parse(request.CsvContent, request.SelectedFormat);
        logger.LogInformation(
            "Stock import preview parsed. PortfolioId={PortfolioId}, UserId={UserId}, SelectedFormat={SelectedFormat}, DetectedFormat={DetectedFormat}, ParsedRows={ParsedRows}, ParseDiagnostics={ParseDiagnosticsCount}",
            request.PortfolioId,
            userId,
            parseResult.SelectedFormat,
            parseResult.DetectedFormat,
            parseResult.Rows.Count,
            parseResult.Diagnostics.Count);

        var symbolResolution = await stockImportSymbolResolver.ResolveAsync(parseResult.Rows, cancellationToken);

        var diagnostics = parseResult.Diagnostics
            .Concat(symbolResolution.Diagnostics)
            .OrderBy(d => d.RowNumber)
            .ThenBy(d => d.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = symbolResolution.Rows
            .OrderBy(row => row.RowNumber)
            .Select(MapPreviewRow)
            .ToList();

        var summary = BuildSummary(rows);
        var sessionId = Guid.NewGuid();

        logger.LogInformation(
            "Stock import preview resolved. PortfolioId={PortfolioId}, SessionId={SessionId}, TotalRows={TotalRows}, ValidRows={ValidRows}, RequiresActionRows={RequiresActionRows}, InvalidRows={InvalidRows}, DiagnosticCount={DiagnosticCount}",
            request.PortfolioId,
            sessionId,
            summary.TotalRows,
            summary.ValidRows,
            summary.RequiresActionRows,
            summary.InvalidRows,
            diagnostics.Count);

        await stockImportSessionStore.SaveAsync(new StockImportSessionSnapshotDto
        {
            SessionId = sessionId,
            UserId = userId,
            PortfolioId = request.PortfolioId,
            SelectedFormat = parseResult.SelectedFormat,
            DetectedFormat = parseResult.DetectedFormat,
            Baseline = MapBaseline(request.Baseline),
            Rows = rows.Select(row => new StockImportSessionRowSnapshotDto
            {
                RowNumber = row.RowNumber,
                TradeDate = row.TradeDate,
                Ticker = row.Ticker,
                TradeSide = row.TradeSide,
                ConfirmedTradeSide = row.ConfirmedTradeSide,
                Quantity = row.Quantity,
                UnitPrice = row.UnitPrice,
                Fees = row.Fees,
                Taxes = row.Taxes,
                NetSettlement = row.NetSettlement,
                Currency = row.Currency,
                Status = row.Status,
                ActionsRequired = row.ActionsRequired,
                IsInvalid = string.Equals(row.Status, StatusInvalid, StringComparison.Ordinal)
            }).ToList(),
        }, cancellationToken);

        return new StockImportPreviewResponseDto
        {
            SessionId = sessionId,
            DetectedFormat = parseResult.DetectedFormat,
            SelectedFormat = parseResult.SelectedFormat,
            Summary = summary,
            Rows = rows,
            Errors = diagnostics
        };
    }

    private static StockImportPreviewRowDto MapPreviewRow(StockImportParsedRow row)
    {
        var status = ResolveRowStatus(row);

        return new StockImportPreviewRowDto
        {
            RowNumber = row.RowNumber,
            TradeDate = row.TradeDate,
            RawSecurityName = row.RawSecurityName,
            Ticker = row.Ticker,
            TradeSide = row.TradeSide,
            ConfirmedTradeSide = row.TradeSide is "buy" or "sell" ? row.TradeSide : null,
            Quantity = row.Quantity,
            UnitPrice = row.UnitPrice,
            Fees = row.Fees,
            Taxes = row.Taxes,
            NetSettlement = row.NetSettlement,
            Currency = row.Currency,
            Status = status,
            ActionsRequired = row.ActionsRequired
        };
    }

    private static string ResolveRowStatus(StockImportParsedRow row)
    {
        if (row.IsInvalid)
        {
            return StatusInvalid;
        }

        if (row.ActionsRequired.Count > 0)
        {
            return StatusRequiresUserAction;
        }

        return StatusValid;
    }

    private static StockImportPreviewSummaryDto BuildSummary(IReadOnlyList<StockImportPreviewRowDto> rows)
    {
        var validRows = rows.Count(row => string.Equals(row.Status, StatusValid, StringComparison.Ordinal));
        var requiresActionRows = rows.Count(row => string.Equals(row.Status, StatusRequiresUserAction, StringComparison.Ordinal));
        var invalidRows = rows.Count(row => string.Equals(row.Status, StatusInvalid, StringComparison.Ordinal));

        return new StockImportPreviewSummaryDto
        {
            TotalRows = rows.Count,
            ValidRows = validRows,
            RequiresActionRows = requiresActionRows,
            InvalidRows = invalidRows
        };
    }

    private static StockImportSessionBaselineSnapshotDto MapBaseline(StockImportBaselineRequest? baseline)
    {
        if (baseline is null)
        {
            return new StockImportSessionBaselineSnapshotDto();
        }

        var openingPositions = baseline.OpeningPositions ?? [];

        var normalizedPositions = openingPositions
            .Select(position => position is null
                ? null
                : new StockImportSessionOpeningPositionSnapshotDto
                {
                    Ticker = string.IsNullOrWhiteSpace(position.Ticker)
                        ? null
                        : position.Ticker.Trim().ToUpperInvariant(),
                    Quantity = position.Quantity,
                    TotalCost = position.TotalCost
                })
            .OfType<StockImportSessionOpeningPositionSnapshotDto>()
            .ToList();

        var openingCashBalance = baseline.OpeningCashBalance;
        var openingLedgerBalance = baseline.OpeningLedgerBalance ?? openingCashBalance;

        return new StockImportSessionBaselineSnapshotDto
        {
            BaselineDate = baseline.BaselineDate,
            OpeningPositions = normalizedPositions,
            OpeningCashBalance = openingCashBalance,
            OpeningLedgerBalance = openingLedgerBalance
        };
    }
}
