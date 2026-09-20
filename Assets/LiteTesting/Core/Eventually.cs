using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LiteTesting
{
    public static class Eventually
    {
        public static void Until(
            Func<bool> condition,
            TimeSpan timeout,
            TimeSpan pollInterval,
            Func<string> describeState = null)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            ValidateTiming(timeout, pollInterval);

            var watch = Stopwatch.StartNew();
            int attempts = 0;
            while (true)
            {
                attempts++;
                if (condition()) return;
                if (watch.Elapsed >= timeout)
                {
                    throw Timeout(timeout, attempts, describeState);
                }

                TimeSpan remaining = timeout - watch.Elapsed;
                TimeSpan delay = remaining < pollInterval ? remaining : pollInterval;
                if (delay > TimeSpan.Zero) Thread.Sleep(delay);
            }
        }

        public static async Task UntilAsync(
            Func<CancellationToken, Task<bool>> condition,
            TimeSpan timeout,
            TimeSpan pollInterval,
            CancellationToken cancellationToken = default,
            Func<string> describeState = null)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            ValidateTiming(timeout, pollInterval);

            var watch = Stopwatch.StartNew();
            int attempts = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempts++;
                if (await condition(cancellationToken).ConfigureAwait(false)) return;
                if (watch.Elapsed >= timeout)
                {
                    throw Timeout(timeout, attempts, describeState);
                }

                TimeSpan remaining = timeout - watch.Elapsed;
                TimeSpan delay = remaining < pollInterval ? remaining : pollInterval;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static void ValidateTiming(TimeSpan timeout, TimeSpan pollInterval)
        {
            if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        private static TimeoutException Timeout(TimeSpan timeout, int attempts, Func<string> describeState)
        {
            string state = describeState == null ? string.Empty : " Last state: " + describeState();
            return new TimeoutException($"Condition was not met within {timeout} after {attempts} attempts.{state}");
        }
    }
}
