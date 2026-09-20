using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace LiteTesting
{
    public sealed class TestRunSettings
    {
        public const string SeedEnvironmentVariable = "LITETEST_SEED";
        public const string TimeoutScaleEnvironmentVariable = "LITETEST_TIMEOUT_SCALE";
        public const string ArtifactsEnvironmentVariable = "LITETEST_ARTIFACTS";
        public const string RunIdEnvironmentVariable = "LITETEST_RUN_ID";

        public const ulong DefaultSeed = 0x4C49544554455354UL;

        public TestRunSettings(ulong seed, double timeoutScale, string artifactsDirectory, string runId)
        {
            if (timeoutScale <= 0 || double.IsNaN(timeoutScale) || double.IsInfinity(timeoutScale))
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutScale));
            }

            if (string.IsNullOrWhiteSpace(artifactsDirectory))
            {
                throw new ArgumentException("An artifacts directory is required.", nameof(artifactsDirectory));
            }

            if (string.IsNullOrWhiteSpace(runId))
            {
                throw new ArgumentException("A run id is required.", nameof(runId));
            }

            Seed = seed;
            TimeoutScale = timeoutScale;
            ArtifactsDirectory = Path.GetFullPath(artifactsDirectory);
            RunId = runId.Trim();
        }

        public ulong Seed { get; }
        public double TimeoutScale { get; }
        public string ArtifactsDirectory { get; }
        public string RunId { get; }

        public static TestRunSettings FromEnvironment()
        {
            string seedText = Environment.GetEnvironmentVariable(SeedEnvironmentVariable);
            string scaleText = Environment.GetEnvironmentVariable(TimeoutScaleEnvironmentVariable);
            string artifacts = Environment.GetEnvironmentVariable(ArtifactsEnvironmentVariable);
            string runId = Environment.GetEnvironmentVariable(RunIdEnvironmentVariable);

            ulong seed = string.IsNullOrWhiteSpace(seedText) ? DefaultSeed : ParseSeed(seedText);
            double scale = string.IsNullOrWhiteSpace(scaleText)
                ? 1.0
                : double.Parse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture);

            if (string.IsNullOrWhiteSpace(artifacts))
            {
                artifacts = Path.Combine(Directory.GetCurrentDirectory(), "TestResults", "artifacts");
            }

            if (string.IsNullOrWhiteSpace(runId)) runId = "local";
            return new TestRunSettings(seed, scale, artifacts, runId);
        }

        public ulong DeriveSeed(string testId)
        {
            if (string.IsNullOrWhiteSpace(testId)) throw new ArgumentException("A test id is required.", nameof(testId));

            ulong hash = 14695981039346656037UL ^ Seed;
            byte[] bytes = Encoding.UTF8.GetBytes(testId);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }

            return hash;
        }

        public TimeSpan ScaleTimeout(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            double ticks = timeout.Ticks * TimeoutScale;
            if (ticks > TimeSpan.MaxValue.Ticks) return TimeSpan.MaxValue;
            return TimeSpan.FromTicks((long)ticks);
        }

        private static ulong ParseSeed(string value)
        {
            string text = value.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return ulong.Parse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            }

            return ulong.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
    }
}
