using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
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
    IStockTransactionRepository stockTransactionRepository,
    PortfolioCalculator portfolioCalculator,
    ILogger<PreviewStockImportUseCase> logger) : IPreviewStockImportUseCase
{
    private const string StatusValid = "valid";
    private const string StatusRequiresUserAction = "requires_user_action";
    private const string StatusInvalid = "invalid";

    private const string ActionChooseSellBeforeBuyHandling = "choose_sell_before_buy_handling";
    private const string TradeSideBuy = "buy";
    private const string TradeSideSell = "sell";

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

        var previewBaseline = MapBaseline(request.Baseline);
        var rows = await BuildPreviewRowsAsync(
            symbolResolution.Rows,
            request.PortfolioId,
            previewBaseline,
            cancellationToken);

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
            Baseline = previewBaseline,
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

    private async Task<IReadOnlyList<StockImportPreviewRowDto>> BuildPreviewRowsAsync(
        IReadOnlyList<StockImportParsedRow> parsedRows,
        Guid portfolioId,
        StockImportSessionBaselineSnapshotDto baseline,
        CancellationToken cancellationToken)
    {
        var orderedRows = parsedRows
            .OrderBy(row => row.RowNumber)
            .ToList();

        if (orderedRows.Count == 0)
        {
            return [];
        }

        var allPortfolioTransactions = await stockTransactionRepository.GetByPortfolioIdAsync(
            portfolioId,
            cancellationToken);

        var baselinePositions = BuildBaselinePositionsByTickerAndMarket(baseline);
        var runningSyntheticTransactionsByTicker = new Dictionary<string, List<StockTransaction>>(StringComparer.OrdinalIgnoreCase);

        var result = new List<StockImportPreviewRowDto>(orderedRows.Count);

        foreach (var row in orderedRows)
        {
            var previewRow = MapPreviewRow(row);

            if (previewRow.TradeDate is not null
                && previewRow.Quantity.HasValue
                && previewRow.Quantity.Value > 0m
                && string.Equals(previewRow.ConfirmedTradeSide, TradeSideSell, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(previewRow.Ticker))
            {
                var usesPartialHistoryAssumption = ShouldRequireSellBeforeBuyAction(
                    previewRow,
                    allPortfolioTransactions,
                    runningSyntheticTransactionsByTicker,
                    baselinePositions,
                    out var resolvedMarket,
                    out _);

                if (usesPartialHistoryAssumption)
                {
                    previewRow = previewRow with
                    {
                        UsesPartialHistoryAssumption = true,
                        ActionsRequired = AppendAction(
                            previewRow.ActionsRequired,
                            ActionChooseSellBeforeBuyHandling),
                        Status = StatusRequiresUserAction
                    };
                }

                if (resolvedMarket is not null)
                {
                    RegisterSyntheticSellForPreview(
                        previewRow,
                        resolvedMarket.Value,
                        runningSyntheticTransactionsByTicker);
                }
            }

            result.Add(previewRow);
        }

        return result;
    }

    private static StockImportPreviewRowDto MapPreviewRow(StockImportParsedRow row)
    {
        var status = ResolveRowStatus(row.IsInvalid, row.ActionsRequired);

        return new StockImportPreviewRowDto
        {
            RowNumber = row.RowNumber,
            TradeDate = row.TradeDate,
            RawSecurityName = row.RawSecurityName,
            Ticker = row.Ticker,
            TradeSide = row.TradeSide,
            ConfirmedTradeSide = row.TradeSide is TradeSideBuy or TradeSideSell ? row.TradeSide : null,
            Quantity = row.Quantity,
            UnitPrice = row.UnitPrice,
            Fees = row.Fees,
            Taxes = row.Taxes,
            NetSettlement = row.NetSettlement,
            Currency = row.Currency,
            Status = status,
            UsesPartialHistoryAssumption = false,
            ActionsRequired = row.ActionsRequired
        };
    }

    private bool ShouldRequireSellBeforeBuyAction(
        StockImportPreviewRowDto previewRow,
        IReadOnlyList<StockTransaction> persistedTransactions,
        IReadOnlyDictionary<string, List<StockTransaction>> runningSyntheticTransactionsByTicker,
        IReadOnlyDictionary<(string Ticker, StockMarket Market), decimal> baselinePositions,
        out StockMarket? resolvedMarket,
        out decimal availableShares)
    {
        resolvedMarket = ResolvePreviewMarket(previewRow, persistedTransactions);
        availableShares = 0m;

        if (!resolvedMarket.HasValue)
        {
            return false;
        }

        var ticker = previewRow.Ticker!.Trim().ToUpperInvariant();
        var market = resolvedMarket.Value;

        var seededShares = 0m;
        if (baselinePositions.TryGetValue((ticker, market), out var baselineShares))
        {
            seededShares = baselineShares;
        }

        runningSyntheticTransactionsByTicker.TryGetValue(ticker, out var syntheticTransactionsByTicker);

        var transactionsForPosition = persistedTransactions
            .Concat(syntheticTransactionsByTicker ?? [])
            .Where(transaction => string.Equals(transaction.Ticker, ticker, StringComparison.OrdinalIgnoreCase)
                && transaction.Market == market)
            .OrderBy(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.CreatedAt)
            .ToList();

        if (seededShares > 0m)
        {
            var syntheticOpening = BuildSyntheticOpeningAdjustmentTransaction(
                previewRow,
                ticker,
                market,
                seededShares);
            transactionsForPosition.Insert(0, syntheticOpening);
        }

        var currentPosition = portfolioCalculator.CalculatePositionByMarket(
            ticker,
            market,
            transactionsForPosition);

        availableShares = currentPosition.TotalShares;

        if (availableShares >= previewRow.Quantity!.Value)
        {
            return false;
        }

        return availableShares <= 0m;
    }

    private static void RegisterSyntheticSellForPreview(
        StockImportPreviewRowDto previewRow,
        StockMarket market,
        IDictionary<string, List<StockTransaction>> runningSyntheticTransactionsByTicker)
    {
        if (previewRow.TradeDate is null
            || !previewRow.Quantity.HasValue
            || previewRow.Quantity.Value <= 0m
            || string.IsNullOrWhiteSpace(previewRow.Ticker)
            || !string.Equals(previewRow.ConfirmedTradeSide, TradeSideSell, StringComparison.Ordinal))
        {
            return;
        }

        var ticker = previewRow.Ticker.Trim().ToUpperInvariant();
        var syntheticSell = BuildSyntheticPreviewTransaction(
            previewRow,
            ticker,
            market,
            TransactionType.Sell);

        if (!runningSyntheticTransactionsByTicker.TryGetValue(ticker, out var list))
        {
            list = [];
            runningSyntheticTransactionsByTicker[ticker] = list;
        }

        list.Add(syntheticSell);
    }

    private static StockTransaction BuildSyntheticPreviewTransaction(
        StockImportPreviewRowDto previewRow,
        string ticker,
        StockMarket market,
        TransactionType transactionType)
    {
        var transactionDate = DateTime.SpecifyKind(previewRow.TradeDate!.Value.Date, DateTimeKind.Utc);
        var currency = ParseCurrency(previewRow.Currency) ?? StockTransaction.GuessCurrencyFromMarket(market);

        return new StockTransaction(
            portfolioId: Guid.NewGuid(),
            transactionDate: transactionDate,
            ticker: ticker,
            transactionType: transactionType,
            shares: previewRow.Quantity!.Value,
            pricePerShare: previewRow.UnitPrice ?? 0m,
            exchangeRate: 1m,
            fees: previewRow.Fees + previewRow.Taxes,
            currencyLedgerId: null,
            notes: "import-preview-synthetic",
            market: market,
            currency: currency);
    }

    private static StockTransaction BuildSyntheticOpeningAdjustmentTransaction(
        StockImportPreviewRowDto previewRow,
        string ticker,
        StockMarket market,
        decimal openingShares)
    {
        var baselineDate = DateTime.SpecifyKind(previewRow.TradeDate!.Value.Date.AddDays(-1), DateTimeKind.Utc);
        var currency = ParseCurrency(previewRow.Currency) ?? StockTransaction.GuessCurrencyFromMarket(market);

        return new StockTransaction(
            portfolioId: Guid.NewGuid(),
            transactionDate: baselineDate,
            ticker: ticker,
            transactionType: TransactionType.Adjustment,
            shares: openingShares,
            pricePerShare: 0m,
            exchangeRate: 1m,
            fees: 0m,
            currencyLedgerId: null,
            notes: "import-preview-opening-baseline",
            market: market,
            currency: currency);
    }

    private static StockMarket? ResolvePreviewMarket(
        StockImportPreviewRowDto previewRow,
        IReadOnlyList<StockTransaction> persistedTransactions)
    {
        if (string.IsNullOrWhiteSpace(previewRow.Ticker))
        {
            return null;
        }

        var ticker = previewRow.Ticker.Trim().ToUpperInvariant();

        var candidateMarkets = persistedTransactions
            .Where(transaction => string.Equals(transaction.Ticker, ticker, StringComparison.OrdinalIgnoreCase))
            .Select(transaction => transaction.Market)
            .Distinct()
            .ToList();

        if (candidateMarkets.Count == 1)
        {
            return candidateMarkets[0];
        }

        var parsedCurrency = ParseCurrency(previewRow.Currency);
        var inferredFromCurrency = TryInferMarketFromCurrency(parsedCurrency);

        if (inferredFromCurrency.HasValue)
        {
            return inferredFromCurrency.Value;
        }

        if (candidateMarkets.Count > 1)
        {
            return null;
        }

        return StockTransaction.GuessMarketFromTicker(ticker);
    }

    private static Currency? ParseCurrency(string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return null;
        }

        return Enum.TryParse<Currency>(currencyCode.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }

    private static StockMarket? TryInferMarketFromCurrency(Currency? currency)
        => currency switch
        {
            Currency.TWD => StockMarket.TW,
            Currency.GBP => StockMarket.UK,
            Currency.EUR => StockMarket.EU,
            _ => null
        };

    private static IReadOnlyDictionary<(string Ticker, StockMarket Market), decimal> BuildBaselinePositionsByTickerAndMarket(
        StockImportSessionBaselineSnapshotDto baseline)
    {
        if (baseline.OpeningPositions.Count == 0)
        {
            return new Dictionary<(string Ticker, StockMarket Market), decimal>();
        }

        var result = new Dictionary<(string Ticker, StockMarket Market), decimal>();

        foreach (var openingPosition in baseline.OpeningPositions)
        {
            if (openingPosition is null
                || string.IsNullOrWhiteSpace(openingPosition.Ticker)
                || !openingPosition.Quantity.HasValue
                || openingPosition.Quantity.Value <= 0m)
            {
                continue;
            }

            var normalizedTicker = openingPosition.Ticker.Trim().ToUpperInvariant();
            var market = StockTransaction.GuessMarketFromTicker(normalizedTicker);
            var key = (normalizedTicker, market);

            if (result.TryGetValue(key, out var existingShares))
            {
                result[key] = existingShares + openingPosition.Quantity.Value;
            }
            else
            {
                result[key] = openingPosition.Quantity.Value;
            }
        }

        return result;
    }

    private static IReadOnlyList<string> AppendAction(
        IReadOnlyList<string> actions,
        string action)
    {
        if (actions.Contains(action, StringComparer.Ordinal))
        {
            return actions;
        }

        var updatedActions = actions.ToList();
        updatedActions.Add(action);
        return updatedActions;
    }

    private static string ResolveRowStatus(
        bool isInvalid,
        IReadOnlyList<string> actionsRequired)
    {
        if (isInvalid)
        {
            return StatusInvalid;
        }

        if (actionsRequired.Count > 0)
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
