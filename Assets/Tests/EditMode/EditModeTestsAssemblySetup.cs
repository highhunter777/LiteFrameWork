using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// EditMode 程序集的**每轮起始复位**。
    ///
    /// <b>为什么需要</b>：<c>LiteFramework.Log</c> 是静态类，注入的 helper **跨测试运行存活**，
    /// 而 PlayMode 套件会跑完整引导链（<c>PlatformInfrastructureModule</c> 调
    /// <c>Log.SetHelper(new UnityLogHelper())</c>）。一旦 helper 在，Unity 的 <c>LogAssert</c>
    /// 就开始看见本被静默丢弃的错误日志——本套件里**故意触发错误日志**的用例随即被判
    /// <c>Unhandled log message</c>。静态 helper 让同一套用例在不同运行次序下得出相反结论，必须复位。
    ///
    /// <b>复位点选在程序集开始</b>（<see cref="OneTimeSetUpAttribute"/>）而非每个用例：
    /// 用例内可能依赖"本次运行至今"的累计值（如 UiU0 记录 <c>errBefore</c> 再断言增量），
    /// 每用例清 <c>ErrorCount</c> 会打断这类断言。程序集起始复位足以阻断上一次运行的泄漏。
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
