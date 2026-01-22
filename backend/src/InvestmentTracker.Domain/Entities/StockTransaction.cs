using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 股票交易記錄實體，記錄股票/ETF 的買賣活動
/// </summary>
public class StockTransaction : BaseEntity
{
    public Guid PortfolioId { get; private set; }
    public DateTime TransactionDate { get; private set; }
    public string Ticker { get; private set; } = string.Empty;
    public TransactionType TransactionType { get; private set; }
    public decimal Shares { get; private set; }
    public decimal PricePerShare { get; private set; }
    public decimal? ExchangeRate { get; private set; }
    public decimal Fees { get; private set; }
    public FundSource FundSource { get; private set; } = FundSource.None;
    public Guid? CurrencyLedgerId { get; private set; }
    public string? Notes { get; private set; }
    public bool IsDeleted { get; private set; }
    public decimal? RealizedPnlHome { get; private set; }
    public StockMarket Market { get; private set; }
    public Currency Currency { get; private set; }

    // 導覽屬性
    public Portfolio Portfolio { get; private set; } = null!;
    public CurrencyLedger? CurrencyLedger { get; private set; }

    // 計算屬性（不儲存於資料庫）
    /// <summary>
    /// 判斷是否為台股。台股代號以數字開頭（如 0050、2330、6547R）
    /// </summary>
    public bool IsTaiwanStock => !string.IsNullOrEmpty(Ticker) && char.IsDigit(Ticker[0]);

    /// <summary>
    /// 原幣總成本：(股數 × 單價) + 手續費
    /// 台股依市場慣例使用無條件捨去計算
    /// </summary>
    public decimal TotalCostSource
    {
        get
        {
            var subtotal = Shares * PricePerShare;
            // 台股交易小計採無條件捨去
            if (IsTaiwanStock)
                subtotal = Math.Floor(subtotal);
            return subtotal + Fees;
        }
    }

    /// <summary>
    /// 本國幣總成本：原幣總成本 × 匯率
    /// 若未設定匯率則回傳 null
    /// </summary>
    public decimal? TotalCostHome => ExchangeRate.HasValue ? TotalCostSource * ExchangeRate.Value : null;

    /// <summary>
    /// 是否有設定本國幣換算匯率
    /// </summary>
    public bool HasExchangeRate => ExchangeRate.HasValue;

    // EF Core 必要的無參數建構子
    private StockTransaction() { }

    public StockTransaction(
        Guid portfolioId,
        DateTime transactionDate,
        string ticker,
        TransactionType transactionType,
        decimal shares,
        decimal pricePerShare,
        decimal? exchangeRate,
        decimal fees = 0m,
        FundSource fundSource = FundSource.None,
        Guid? currencyLedgerId = null,
        string? notes = null,
        StockMarket? market = null,
        Currency? currency = null)
    {
        if (portfolioId == Guid.Empty)
            throw new ArgumentException("Portfolio ID is required", nameof(portfolioId));

        PortfolioId = portfolioId;
        SetTransactionDate(transactionDate);
        SetTicker(ticker);
        TransactionType = transactionType;
        SetShares(shares);
        SetPricePerShare(pricePerShare);
        SetExchangeRate(exchangeRate);
        SetFees(fees);
        SetFundSource(fundSource, currencyLedgerId);
        SetNotes(notes);
        var resolvedMarket = market ?? GuessMarketFromTicker(ticker);
        SetMarket(resolvedMarket);
        SetCurrency(currency ?? GuessCurrencyFromMarket(resolvedMarket));
    }

    public void SetTransactionDate(DateTime date)
    {
        if (date > DateTime.UtcNow.AddDays(1))
            throw new ArgumentException("Transaction date cannot be in the future", nameof(date));

        // 確保 UTC Kind 以相容 PostgreSQL
        TransactionDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
    }

    public void SetTicker(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException("Ticker is required", nameof(ticker));

        if (ticker.Length > 20)
            throw new ArgumentException("Ticker cannot exceed 20 characters", nameof(ticker));

        Ticker = ticker.Trim().ToUpperInvariant();
    }

    public void SetShares(decimal shares)
    {
        if (shares <= 0)
            throw new ArgumentException("Shares must be positive", nameof(shares));

        // 驗證 4 位小數
        Shares = Math.Round(shares, 4);
    }

    public void SetPricePerShare(decimal price)
    {
        if (price < 0)
            throw new ArgumentException("Price cannot be negative", nameof(price));

        PricePerShare = Math.Round(price, 4);
    }

    public void SetExchangeRate(decimal? rate)
    {
        if (rate is <= 0)
            throw new ArgumentException("Exchange rate must be positive", nameof(rate));

        ExchangeRate = rate.HasValue ? Math.Round(rate.Value, 6) : null;
    }

    public void SetFees(decimal fees)
    {
        if (fees < 0)
            throw new ArgumentException("Fees cannot be negative", nameof(fees));

        Fees = Math.Round(fees, 2);
    }

    public void SetFundSource(FundSource fundSource, Guid? currencyLedgerId = null)
    {
        if (fundSource == FundSource.CurrencyLedger && !currencyLedgerId.HasValue)
            throw new ArgumentException("Currency ledger ID is required when fund source is CurrencyLedger");

        if (fundSource != FundSource.CurrencyLedger && currencyLedgerId.HasValue)
            throw new ArgumentException("Currency ledger ID should only be set when fund source is CurrencyLedger");

        FundSource = fundSource;
        CurrencyLedgerId = currencyLedgerId;
    }

    public void SetNotes(string? notes)
    {
        if (notes?.Length > 500)
            throw new ArgumentException("Notes cannot exceed 500 characters", nameof(notes));

        Notes = notes?.Trim();
    }

    public void MarkAsDeleted() => IsDeleted = true;
    public void Restore() => IsDeleted = false;

    public void SetRealizedPnl(decimal? realizedPnlHome)
    {
        RealizedPnlHome = realizedPnlHome.HasValue ? Math.Round(realizedPnlHome.Value, 2) : null;
    }

    public void SetTransactionType(TransactionType transactionType)
    {
        TransactionType = transactionType;
    }

    public void SetMarket(StockMarket market)
    {
        if (!Enum.IsDefined(typeof(StockMarket), market))
            throw new ArgumentException("Invalid market", nameof(market));

        Market = market;
    }

    public void SetCurrency(Currency currency)
    {
        if (!Enum.IsDefined(typeof(Currency), currency))
            throw new ArgumentException("Invalid currency", nameof(currency));

        Currency = currency;
    }

    /// <summary>
    /// Auto-detect currency based on market.
    /// Taiwan stocks use TWD, all others default to USD.
    /// User can override to GBP/EUR manually.
    /// </summary>
    public static Currency GuessCurrencyFromMarket(StockMarket market)
    {
        return market == StockMarket.TW ? Currency.TWD : Currency.USD;
    }

    /// <summary>
    /// 根據股票代號推測市場
    /// - 數字開頭：台股 (TW)
    /// - .L 結尾：英股 (UK)
    /// - 其他：美股 (US)
    /// </summary>
    public static StockMarket GuessMarketFromTicker(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return StockMarket.US;

        ticker = ticker.Trim().ToUpperInvariant();

        // 台股：數字開頭（如 0050、2330、6547R）
        if (char.IsDigit(ticker[0]))
            return StockMarket.TW;

        // 英股：.L 結尾
        if (ticker.EndsWith(".L", StringComparison.OrdinalIgnoreCase))
            return StockMarket.UK;

        // 預設美股
        return StockMarket.US;
    }
}
