using System.Collections.Concurrent;

namespace InvestmentTracker.Infrastructure.StockPrices;

/// <summary>
/// Rate limiter for TWSE API requests
/// TWSE blocks IPs that exceed 3 requests per 5 seconds
/// </summary>
public class TwseRateLimiter : ITwseRateLimiter
{
    private readonly ConcurrentQueue<DateTime> _requestTimes = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private const int MaxRequests = 3;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    public async Task WaitForSlotAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Remove expired timestamps
                var cutoff = DateTime.UtcNow - Window;
                while (_requestTimes.TryPeek(out var oldest) && oldest < cutoff)
                {
                    _requestTimes.TryDequeue(out _);
                }

                // Check if we have a slot
                if (_requestTimes.Count < MaxRequests)
                {
                    _requestTimes.Enqueue(DateTime.UtcNow);
                    return;
                }

                // Wait until the oldest request expires
                if (_requestTimes.TryPeek(out var oldestTime))
                {
                    var waitTime = oldestTime + Window - DateTime.UtcNow;
                    if (waitTime > TimeSpan.Zero)
                    {
                        await Task.Delay(waitTime + TimeSpan.FromMilliseconds(100), cancellationToken);
                    }
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

public interface ITwseRateLimiter
{
    Task WaitForSlotAsync(CancellationToken cancellationToken = default);
}
