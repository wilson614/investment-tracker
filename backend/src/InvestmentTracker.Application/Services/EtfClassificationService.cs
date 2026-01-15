using InvestmentTracker.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Application.Services;

/// <summary>
/// ETF type classification for YTD performance calculation.
/// </summary>
public enum EtfType
{
    Unknown = 0,
    Accumulating = 1,
    Distributing = 2,
}

/// <summary>
/// ETF classification result.
/// </summary>
public record EtfClassificationResult(
    string Ticker,
    EtfType Type,
    bool IsConfirmed,
    string? Source
);

/// <summary>
/// Service for classifying ETF types (accumulating vs distributing).
/// Used to determine if dividend adjustment is needed for YTD calculations.
/// </summary>
public class EtfClassificationService
{
    private readonly ILogger<EtfClassificationService> _logger;

    // Known accumulating ETFs (no dividend adjustment needed)
    private static readonly HashSet<string> KnownAccumulatingEtfs = new(StringComparer.OrdinalIgnoreCase)
    {
        // Vanguard UK accumulating ETFs
        "VWRA", "VUAA", "VHVE", "VFEM", "VEUA", "VJPA",
        // iShares accumulating ETFs
        "WSML", "HCHA",
        // Xtrackers
        "XRSU", "EXUS",
        // Euronext USD ETFs
        "AGAC",
    };

    // Known Taiwan stocks/ETFs (dividend adjustment needed)
    private static readonly HashSet<string> KnownTaiwanDistributing = new(StringComparer.OrdinalIgnoreCase)
    {
        "0050", "0056", "006208", "00878", "00919",
    };

    public EtfClassificationService(ILogger<EtfClassificationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Classify an ETF by ticker symbol.
    /// </summary>
    public EtfClassificationResult ClassifyEtf(string ticker)
    {
        // Check known accumulating ETFs
        if (KnownAccumulatingEtfs.Contains(ticker))
        {
            return new EtfClassificationResult(ticker, EtfType.Accumulating, true, "Known List");
        }

        // Check Taiwan distributing ETFs
        if (KnownTaiwanDistributing.Contains(ticker))
        {
            return new EtfClassificationResult(ticker, EtfType.Distributing, true, "Known List");
        }

        // Taiwan stock pattern (numeric with optional suffix)
        if (System.Text.RegularExpressions.Regex.IsMatch(ticker, @"^\d+[A-Za-z]*$"))
        {
            _logger.LogDebug("Ticker {Ticker} appears to be Taiwan stock, assuming distributing", ticker);
            return new EtfClassificationResult(ticker, EtfType.Distributing, false, "Pattern Match");
        }

        // LSE-listed ETFs (ending with .L) - likely accumulating if ACC in name
        if (ticker.EndsWith(".L", StringComparison.OrdinalIgnoreCase))
        {
            return new EtfClassificationResult(ticker, EtfType.Accumulating, false, "LSE Pattern");
        }

        // Default to unknown
        _logger.LogDebug("Unable to classify ticker {Ticker}, returning unknown", ticker);
        return new EtfClassificationResult(ticker, EtfType.Unknown, false, null);
    }

    /// <summary>
    /// Check if dividend adjustment is needed for a ticker.
    /// </summary>
    public bool NeedsDividendAdjustment(string ticker)
    {
        var classification = ClassifyEtf(ticker);
        return classification.Type == EtfType.Distributing;
    }

    /// <summary>
    /// Get all known ETF classifications.
    /// </summary>
    public IReadOnlyList<EtfClassificationResult> GetKnownClassifications()
    {
        var results = new List<EtfClassificationResult>();

        foreach (var ticker in KnownAccumulatingEtfs)
        {
            results.Add(new EtfClassificationResult(ticker, EtfType.Accumulating, true, "Known List"));
        }

        foreach (var ticker in KnownTaiwanDistributing)
        {
            results.Add(new EtfClassificationResult(ticker, EtfType.Distributing, true, "Known List"));
        }

        return results;
    }
}
