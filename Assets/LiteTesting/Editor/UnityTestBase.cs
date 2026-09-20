using NUnit.Framework;

namespace LiteTesting.Unity
{
    public abstract class UnityTestBase
    {
        protected UnityTestScope Scope { get; private set; }

        [SetUp]
        protected void LiteTestingSetUp()
        {
            Scope = new UnityTestScope(TestContext.CurrentContext.Test.FullName);
        }

        [TearDown]
        protected void LiteTestingTearDown()
        {
            Scope?.Dispose();
            Scope = null;
        }
    }
}
