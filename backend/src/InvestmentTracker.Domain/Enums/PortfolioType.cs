namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// 投資組合類型，用於不同的幣別處理模式。
/// </summary>
public enum PortfolioType
{
    /// <summary>
    /// 主投資組合：以本位幣（TWD）追蹤成本，並透過匯率換算。
    /// </summary>
    Primary = 0,

    /// <summary>
    /// 外幣投資組合：所有指標以單一來源幣別計算，不進行匯率換算。
    /// 適用於投資組合內的標的皆為同一外幣（例如 USD）。
    /// </summary>
    ForeignCurrency = 1
}
