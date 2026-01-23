using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Infrastructure.MarketData;

public static class YahooSymbolHelper
{
    /// <summary>
    /// 將系統內的 ticker 轉為 Yahoo Finance 查價用的 symbol。
    /// 注意：若 ticker 已包含 suffix（例如 SWRD.L / AGAC.AS），則原樣回傳。
    /// </summary>
    public static string ConvertToYahooSymbol(string ticker, StockMarket? market)
    {
        if (ticker.Contains('.'))
        {
            return ticker;
        }

        return market switch
        {
            StockMarket.UK => $"{ticker}.L",
            StockMarket.EU => $"{ticker}.AS",
            _ => ticker
        };
    }
}
