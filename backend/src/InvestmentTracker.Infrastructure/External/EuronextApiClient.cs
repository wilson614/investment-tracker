using System.Text.RegularExpressions;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.External;

/// <summary>
/// HTTP client for fetching quotes from Euronext exchange.
/// Uses the live.euronext.com API endpoint which returns HTML content.
/// </summary>
public class EuronextApiClient : IEuronextApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EuronextApiClient> _logger;

    // Regex patterns to extract data from HTML response
    private static readonly Regex PriceRegex = new(
        @"id=""header-instrument-price""[^>]*>([0-9.,]+)</span>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CurrencyRegex = new(
        @"id=""header-instrument-currency""[^>]*>\s*([€$£]|\w{3})\s*</span>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex NameRegex = new(
        @"id=""header-instrument-name""[^>]*>\s*<strong>([^<]+)</strong>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DateTimeRegex = new(
        @"last-price-date-time[^>]*>([0-9]{2}/[0-9]{2}/[0-9]{4})\s*-\s*([0-9]{2}:[0-9]{2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public EuronextApiClient(HttpClient httpClient, ILogger<EuronextApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<EuronextQuoteResult?> GetQuoteAsync(string isin, string mic, CancellationToken cancellationToken = default)
    {
        try
        {
            // Euronext API format: /en/ajax/getDetailedQuote/{ISIN}-{MIC}
            var url = $"https://live.euronext.com/en/ajax/getDetailedQuote/{isin}-{mic}";

            _logger.LogDebug("Fetching Euronext quote from {Url}", url);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "text/html");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Euronext API returned {StatusCode} for {Isin}-{Mic}",
                    response.StatusCode, isin, mic);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(html))
            {
                _logger.LogWarning("Euronext API returned empty content for {Isin}-{Mic}", isin, mic);
                return null;
            }

            // Check for "unknown instrument" error
            if (html.Contains("This instrument is unknown", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Euronext API: instrument unknown for {Isin}-{Mic}", isin, mic);
                return null;
            }

            // Extract price
            var priceMatch = PriceRegex.Match(html);
            if (!priceMatch.Success)
            {
                _logger.LogWarning("Could not find price in Euronext HTML for {Isin}-{Mic}", isin, mic);
                return null;
            }

            var priceStr = priceMatch.Groups[1].Value.Replace(",", ".");
            if (!decimal.TryParse(priceStr, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var price))
            {
                _logger.LogWarning("Could not parse price '{Price}' for {Isin}-{Mic}", priceStr, isin, mic);
                return null;
            }

            // Extract currency
            var currency = "EUR"; // Default
            var currencyMatch = CurrencyRegex.Match(html);
            if (currencyMatch.Success)
            {
                currency = MapCurrencySymbol(currencyMatch.Groups[1].Value.Trim());
            }

            // Extract name
            string? name = null;
            var nameMatch = NameRegex.Match(html);
            if (nameMatch.Success)
            {
                name = nameMatch.Groups[1].Value.Trim();
            }

            // Extract market time
            DateTime? marketTime = null;
            var dateTimeMatch = DateTimeRegex.Match(html);
            if (dateTimeMatch.Success)
            {
                var dateStr = dateTimeMatch.Groups[1].Value; // DD/MM/YYYY
                var timeStr = dateTimeMatch.Groups[2].Value; // HH:mm

                if (DateTime.TryParseExact($"{dateStr} {timeStr}", "dd/MM/yyyy HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeLocal, out var parsed))
                {
                    marketTime = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                }
            }

            _logger.LogDebug("Parsed Euronext quote: {Name} {Price} {Currency}", name, price, currency);

            return new EuronextQuoteResult
            {
                Price = price,
                Currency = currency,
                MarketTime = marketTime,
                Name = name
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching Euronext quote for {Isin}-{Mic}", isin, mic);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching Euronext quote for {Isin}-{Mic}", isin, mic);
            return null;
        }
    }

    private static string MapCurrencySymbol(string symbol) => symbol switch
    {
        "$" => "USD",
        "€" => "EUR",
        "£" => "GBP",
        _ => symbol.ToUpperInvariant()
    };
}
