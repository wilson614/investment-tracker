namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// ETF 配息型態分類，用於 YTD 計算。
/// </summary>
public enum EtfType
{
    /// <summary>尚未判定；計算時視為 Accumulating</summary>
    Unknown = 0,

    /// <summary>累積型 ETF：配息再投入，不需要做股利調整</summary>
    Accumulating = 1,

    /// <summary>配息型 ETF：會發放股利，可能需要做股利調整</summary>
    Distributing = 2
}
