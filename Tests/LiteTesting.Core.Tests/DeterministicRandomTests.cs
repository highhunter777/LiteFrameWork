using LiteTesting;
using Xunit;

namespace LiteTestingTests
{
    public sealed class DeterministicRandomTests
    {
        [Fact]
        [Trait(TestTrait.Category, TestCategory.Unit)]
        public void SameSeedProducesSameSequence()
        {
            var first = new DeterministicRandom(42);
            var second = new DeterministicRandom(42);

            for (int i = 0; i < 100; i++)
            {
                Assert.Equal(first.NextUInt64(), second.NextUInt64());
            }
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Unit)]
        public void NextIntHonorsBounds()
        {
            var random = new DeterministicRandom(7);
            for (int i = 0; i < 1000; i++)
            {
                int value = random.NextInt(-3, 5);
                Assert.InRange(value, -3, 4);
            }
        }
    }
}
