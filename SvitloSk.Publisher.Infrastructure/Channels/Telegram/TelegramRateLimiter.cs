using System;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Orchestration;

namespace SvitloSk.Publisher.Infrastructure.Channels.Telegram;

/// <summary>
/// Controls Telegram API rate limiting and throttling per TELEGRAM_RATE_LIMIT_SPECIFICATION.
/// Enforces mandatory 1000ms delay between consecutive operations and exponential backoff on HTTP 429/5xx.
/// </summary>
public class TelegramRateLimiter
{
    private readonly IDelayProvider _delayProvider;

    public TelegramRateLimiter(IDelayProvider? delayProvider = null)
    {
        _delayProvider = delayProvider ?? new SystemDelayProvider();
    }

    /// <summary>
    /// Throttles between consecutive message transmissions (mandatory 1000ms per specification).
    /// </summary>
    public async Task ThrottleAsync(CancellationToken cancellationToken = default)
    {
        await _delayProvider.DelayAsync(1000, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calculates backoff delay in milliseconds based on attempt count or 429 retry-after header.
    /// </summary>
    public int CalculateBackoffDelayMs(int attempt, int? retryAfterSeconds = null)
    {
        if (retryAfterSeconds.HasValue)
        {
            int delaySec = Math.Min(retryAfterSeconds.Value, 60);
            return delaySec * 1000;
        }

        return attempt switch
        {
            1 => 1000,
            2 => 2000,
            _ => 4000
        };
    }

    /// <summary>
    /// Delays execution according to the calculated backoff.
    /// </summary>
    public async Task DelayBackoffAsync(int attempt, int? retryAfterSeconds = null, CancellationToken cancellationToken = default)
    {
        int delayMs = CalculateBackoffDelayMs(attempt, retryAfterSeconds);
        await _delayProvider.DelayAsync(delayMs, cancellationToken).ConfigureAwait(false);
    }
}
