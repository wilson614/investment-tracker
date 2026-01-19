using InvestmentTracker.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Application.Services;

/// <summary>
/// 用於 YTD 計算的 ETF 類型分類。
/// </summary>
public enum EtfType
{
    Unknown = 0,
    Accumulating = 1,
    Distributing = 2,
}

/// <summary>
/// ETF 分類結果。
/// </summary>
public record EtfClassificationResult(
    string Ticker,
    EtfType Type,
    bool IsConfirmed,
    string? Source
);

/// <summary>
/// ETF 類型分類服務（accumulating vs distributing）。
/// 用於判斷 YTD 計算是否需要做股息調整。
/// </summary>
public class EtfClassificationService
{
    private readonly ILogger<EtfClassificationService> _logger;

    // 已知的 accumulating ETF（不需要股息調整）
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

    // 已知的台股/台灣 ETF（需要股息調整）
    private static readonly HashSet<string> KnownTaiwanDistributing = new(StringComparer.OrdinalIgnoreCase)
    {
        "0050", "0056", "006208", "00878", "00919",
    };

    public EtfClassificationService(ILogger<EtfClassificationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 依 ticker 對 ETF 做分類。
    /// </summary>
    public EtfClassificationResult ClassifyEtf(string ticker)
    {
        // 檢查已知的 accumulating ETF
        if (KnownAccumulatingEtfs.Contains(ticker))
        {
            return new EtfClassificationResult(ticker, EtfType.Accumulating, true, "Known List");
        }

        // 檢查台灣 distributing ETF
        if (KnownTaiwanDistributing.Contains(ticker))
        {
            return new EtfClassificationResult(ticker, EtfType.Distributing, true, "Known List");
        }

        // 台股代號樣式（數字，可能帶字母後綴）
        if (System.Text.RegularExpressions.Regex.IsMatch(ticker, @"^\d+[A-Za-z]*$"))
        {
            _logger.LogDebug("Ticker {Ticker} appears to be Taiwan stock, assuming distributing", ticker);
            return new EtfClassificationResult(ticker, EtfType.Distributing, false, "Pattern Match");
        }

        // LSE 上市 ETF（.L 結尾）：通常視為 accumulating（若命名含 ACC）
        if (ticker.EndsWith(".L", StringComparison.OrdinalIgnoreCase))
        {
            return new EtfClassificationResult(ticker, EtfType.Accumulating, false, "LSE Pattern");
        }

        // 預設回傳 unknown
        _logger.LogDebug("Unable to classify ticker {Ticker}, returning unknown", ticker);
        return new EtfClassificationResult(ticker, EtfType.Unknown, false, null);
    }

    /// <summary>
    /// 判斷此 ticker 是否需要做股息調整。
    /// </summary>
    public bool NeedsDividendAdjustment(string ticker)
    {
        var classification = ClassifyEtf(ticker);
        return classification.Type == EtfType.Distributing;
    }

    /// <summary>
    /// 取得所有已知的 ETF 分類結果。
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
