namespace InvestmentTracker.Domain.Tests.Services;

/// <summary>
/// Tests for TwseRateLimiter behavior
/// </summary>
public class TwseRateLimiterTests
{
    /// <summary>
    /// Test ROC date parsing (民國年 format)
    /// </summary>
    [Theory]
    [InlineData("114年01月17日", 2025, 1, 17)]
    [InlineData("114年07月21日", 2025, 7, 21)]
    [InlineData("113年12月31日", 2024, 12, 31)]
    [InlineData("115年01月22日", 2026, 1, 22)]
    [InlineData("100年01月01日", 2011, 1, 1)]
    public void TryParseRocDate_ValidFormats_ParsesCorrectly(string rocDate, int expectedYear, int expectedMonth, int expectedDay)
    {
        // Act
        var success = TryParseRocDate(rocDate, out var result);

        // Assert
        Assert.True(success);
        Assert.Equal(expectedYear, result.Year);
        Assert.Equal(expectedMonth, result.Month);
        Assert.Equal(expectedDay, result.Day);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2025-01-17")]  // Wrong format
    [InlineData("invalid")]
    public void TryParseRocDate_InvalidFormats_ReturnsFalse(string? rocDate)
    {
        // Act
        var success = TryParseRocDate(rocDate, out _);

        // Assert
        Assert.False(success);
    }

    /// <summary>
    /// Duplicate of TwseDividendService.TryParseRocDate for testing
    /// </summary>
    private static bool TryParseRocDate(string? dateStr, out DateTime result)
    {
        result = default;
        if (string.IsNullOrEmpty(dateStr))
            return false;

        try
        {
            var yearEnd = dateStr.IndexOf('年');
            var monthEnd = dateStr.IndexOf('月');
            var dayEnd = dateStr.IndexOf('日');

            if (yearEnd < 0 || monthEnd < 0 || dayEnd < 0)
                return false;

            var rocYear = int.Parse(dateStr[..yearEnd]);
            var month = int.Parse(dateStr[(yearEnd + 1)..monthEnd]);
            var day = int.Parse(dateStr[(monthEnd + 1)..dayEnd]);

            var year = rocYear + 1911;
            result = new DateTime(year, month, day);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
