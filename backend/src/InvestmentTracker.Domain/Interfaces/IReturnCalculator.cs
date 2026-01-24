namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// 報酬率計算器：提供 Modified Dietz 與 Time-Weighted Return (TWR) 的核心演算法。
/// </summary>
public interface IReturnCalculator
{
    /// <summary>
    /// 計算 Modified Dietz 報酬率（非百分比）。
    /// </summary>
    /// <param name="startValue">期間起始資產價值。</param>
    /// <param name="endValue">期間結束資產價值。</param>
    /// <param name="periodStart">期間起始日期。</param>
    /// <param name="periodEnd">期間結束日期。</param>
    /// <param name="cashFlows">
    /// 期間內的外部現金流事件。
    /// 金額正負號約定：投入/入金為正；提領/出金為負。
    /// </param>
    /// <returns>報酬率（例如 0.125 代表 12.5%）；若無法計算則回傳 null。</returns>
    /// <remarks>
    /// 計算公式：
    /// (End - Start - ΣCF) / (Start + Σ(CF × W))
    ///
    /// 其中：
    /// W = (TotalDays - DaysSinceStart) / TotalDays
    /// </remarks>
    decimal? CalculateModifiedDietz(
        decimal startValue,
        decimal endValue,
        DateTime periodStart,
        DateTime periodEnd,
        IReadOnlyList<ReturnCashFlow> cashFlows);

    /// <summary>
    /// 計算 Time-Weighted Return (TWR)（非百分比）。
    /// </summary>
    /// <param name="startValue">期間起始資產價值。</param>
    /// <param name="endValue">期間結束資產價值。</param>
    /// <param name="cashFlowSnapshots">每個外部現金流事件對應的「事件前/事件後」投資組合估值快照。</param>
    /// <returns>報酬率（例如 0.125 代表 12.5%）；若無法計算則回傳 null。</returns>
    /// <remarks>
    /// 子期間串接規則（before/after model）：
    /// - 子期間起點：前一個現金流事件的 ValueAfter（首段使用 startValue）
    /// - 子期間終點：下一個現金流事件的 ValueBefore
    /// - 最後一段：用 endValue 作為終點
    ///
    /// TWR = Π(1 + R_i) - 1
    /// </remarks>
    decimal? CalculateTimeWeightedReturn(
        decimal startValue,
        decimal endValue,
        IReadOnlyList<ReturnValuationSnapshot> cashFlowSnapshots);
}

/// <summary>
/// 外部現金流事件（供 Modified Dietz 計算使用）。
/// </summary>
/// <param name="Date">事件日期（以日期為主；時間會被忽略）。</param>
/// <param name="Amount">金額：投入/入金為正；提領/出金為負。</param>
public record ReturnCashFlow(DateTime Date, decimal Amount);

/// <summary>
/// 外部現金流事件當下的投資組合估值快照（供 TWR 計算使用）。
/// </summary>
/// <param name="Date">事件日期（以日期為主）。</param>
/// <param name="ValueBefore">事件發生前的投資組合價值。</param>
/// <param name="ValueAfter">事件發生後的投資組合價值。</param>
public record ReturnValuationSnapshot(DateTime Date, decimal ValueBefore, decimal ValueAfter);
