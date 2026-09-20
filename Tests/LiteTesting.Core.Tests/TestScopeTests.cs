using System;
using System.Collections.Generic;
using System.IO;
using LiteTesting;
using Xunit;

namespace LiteTestingTests
{
    public sealed class TestScopeTests
    {
        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void CleanupRunsInReverseRegistrationOrder()
        {
            var order = new List<int>();
            using (var scope = CreateScope(nameof(CleanupRunsInReverseRegistrationOrder)))
            {
                scope.OnCleanup(() => order.Add(1));
                scope.OnCleanup(() => order.Add(2));
            }

            Assert.Equal(new[] { 2, 1 }, order);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void TempDirectoryIsRemovedWhenScopeEnds()
        {
            string path;
            using (var scope = CreateScope(nameof(TempDirectoryIsRemovedWhenScopeEnds)))
            {
                path = scope.CreateTempDirectory();
                File.WriteAllText(Path.Combine(path, "probe.txt"), "probe");
                Assert.True(Directory.Exists(path));
            }

            Assert.False(Directory.Exists(path));
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void ArtifactPathCannotEscapeConfiguredRoot()
        {
            using var scope = CreateScope(nameof(ArtifactPathCannotEscapeConfiguredRoot));
            Assert.Throws<ArgumentException>(() => scope.CreateArtifactPath("../outside.txt"));
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void CleanupStillRunsWhenCancellationCallbackFails()
        {
            bool cleaned = false;
            var scope = CreateScope(nameof(CleanupStillRunsWhenCancellationCallbackFails));
            scope.CancellationToken.Register(() => throw new InvalidOperationException("cancel failed"));
            scope.OnCleanup(() => cleaned = true);

            Assert.Throws<AggregateException>(scope.Dispose);
            Assert.True(cleaned);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public void EventuallyReportsLastStateOnTimeout()
        {
            var error = Assert.Throws<TimeoutException>(() =>
                Eventually.Until(() => false, TimeSpan.Zero, TimeSpan.FromMilliseconds(1), () => "state=waiting"));

            Assert.Contains("state=waiting", error.Message);
        }

        private static TestScope CreateScope(string testId)
        {
            string artifacts = Path.Combine(Path.GetTempPath(), "LiteTestingTests", "artifacts");
            return new TestScope(testId, new TestRunSettings(123, 1.0, artifacts, "self-test"));
        }
    }
}
