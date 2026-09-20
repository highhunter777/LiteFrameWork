using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LiteTesting
{
    /// <summary>
    /// Owns per-test deterministic state, cancellation and LIFO cleanup.
    /// </summary>
    public sealed class TestScope : IDisposable
    {
        private readonly List<Action> _cleanup = new List<Action>();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private bool _disposed;

        public TestScope(string testId, TestRunSettings settings = null)
        {
            if (string.IsNullOrWhiteSpace(testId)) throw new ArgumentException("A test id is required.", nameof(testId));

            TestId = testId;
            Settings = settings ?? TestRunSettings.FromEnvironment();
            Seed = Settings.DeriveSeed(testId);
            Random = new DeterministicRandom(Seed);
            _cancellation.CancelAfter(Settings.ScaleTimeout(TimeSpan.FromSeconds(30)));
        }

        public string TestId { get; }
        public TestRunSettings Settings { get; }
        public ulong Seed { get; }
        public DeterministicRandom Random { get; }
        public CancellationToken CancellationToken => _cancellation.Token;

        public TimeSpan Timeout(TimeSpan baseline)
        {
            return Settings.ScaleTimeout(baseline);
        }

        public void OnCleanup(Action cleanup)
        {
            ThrowIfDisposed();
            if (cleanup == null) throw new ArgumentNullException(nameof(cleanup));
            _cleanup.Add(cleanup);
        }

        public T Track<T>(T disposable) where T : IDisposable
        {
            if (disposable == null) throw new ArgumentNullException(nameof(disposable));
            OnCleanup(disposable.Dispose);
            return disposable;
        }

        public string CreateTempDirectory(string label = "case")
        {
            ThrowIfDisposed();
            string safeLabel = SanitizeSegment(label);
            string root = Path.Combine(Path.GetTempPath(), "LiteTesting", SanitizeSegment(Settings.RunId));
            string path = Path.Combine(root, safeLabel + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            OnCleanup(() => DeleteDirectory(path));
            return path;
        }

        public string CreateArtifactPath(string relativePath)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A relative path is required.", nameof(relativePath));
            if (Path.IsPathRooted(relativePath)) throw new ArgumentException("Artifact paths must be relative.", nameof(relativePath));

            string root = Path.GetFullPath(Path.Combine(Settings.ArtifactsDirectory, SanitizeSegment(Settings.RunId)));
            string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Artifact path escapes the configured artifact directory.", nameof(relativePath));
            }

            string parent = Path.GetDirectoryName(candidate);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            return candidate;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception> failures = null;
            try
            {
                _cancellation.Cancel();
            }
            catch (Exception exception)
            {
                failures = new List<Exception> { exception };
            }

            for (int i = _cleanup.Count - 1; i >= 0; i--)
            {
                try
                {
                    _cleanup[i]();
                }
                catch (Exception exception)
                {
                    if (failures == null) failures = new List<Exception>();
                    failures.Add(exception);
                }
            }

            _cleanup.Clear();
            _cancellation.Dispose();
            if (failures != null) throw new AggregateException("One or more test cleanup actions failed.", failures);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TestScope));
        }

        private static string SanitizeSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unnamed";
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (chars[i] != invalid[j]) continue;
                    chars[i] = '_';
                    break;
                }
            }

            return new string(chars);
        }

        private static void DeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;
            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.Delete(path, true);
                    return;
                }
                catch (Exception exception)
                {
                    last = exception;
                    Thread.Sleep(25 * (attempt + 1));
                }
            }

            throw new IOException("Failed to delete test directory: " + path, last);
        }
    }
}
