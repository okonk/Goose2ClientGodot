using System;
using System.Threading;
using System.Threading.Tasks;
using MapEditor.GameData.Connectivity;

namespace MapEditor.GameData.Sync;

public static class RetryPolicy
{
    public const int MaxAttempts = 3;

    // Delays must have exactly MaxAttempts - 1 entries; the caller sleeps Delays[attempt-1] before attempt attempt+1.
    public static readonly TimeSpan[] Delays =
    {
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(1)
    };

    public static bool IsReadRetriable(GatewayFailureKind kind)
        => kind is GatewayFailureKind.RateLimited
            or GatewayFailureKind.Temporary
            or GatewayFailureKind.Transport;

    public static bool IsWriteRetriable(GatewayFailureKind kind, bool requestWasRejected)
        => kind == GatewayFailureKind.RateLimited && requestWasRejected;

    public static async Task<T> RunReadAsync<T>(
        Func<CancellationToken, Task<T>> attempt,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        for (var i = 1; i <= MaxAttempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await attempt(cancellationToken).ConfigureAwait(false);
            }
            catch (GameDataGatewayException ex) when (i < MaxAttempts && IsReadRetriable(ex.Kind))
            {
                await delay(Delays[i - 1], cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Unreachable.");
    }
}
