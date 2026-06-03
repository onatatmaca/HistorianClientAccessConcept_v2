using System;
using System.Threading;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// Retry with backoff. Pure logic — no Proficy or UI dependencies so it can be unit
    /// tested with a synthetic action and an injected sleep delegate.
    /// </summary>
    public static class RetryHelper
    {
        public static readonly int[] DefaultDelaysMs = { 500, 1000, 2000 };

        public static T Retry<T>(
            Func<T> action,
            int maxAttempts,
            int[] delaysMs = null,
            Action<int> sleep = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

            delaysMs = delaysMs ?? DefaultDelaysMs;
            sleep = sleep ?? Thread.Sleep;

            Exception last = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try { return action(); }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt < maxAttempts - 1 && attempt < delaysMs.Length)
                        sleep(delaysMs[attempt]);
                }
            }
            throw new InvalidOperationException(
                $"Operation failed after {maxAttempts} attempts: {last?.Message}", last);
        }
    }
}
