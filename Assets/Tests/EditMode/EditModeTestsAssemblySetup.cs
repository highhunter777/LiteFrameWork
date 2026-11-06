using NUnit.Framework;
using LiteGame.Modules.Composition;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// EditMode 程序集的**每轮起始复位**。
    /// </summary>
    [SetUpFixture]
    public sealed class EditModeTestsAssemblySetup
    {
        [OneTimeSetUp]
        public void ResetSharedStaticsBeforeSuite()
        {
            LiteFramework.Log.ResetForTesting();
        }
    }
}
