using LiteFramework;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 日志关联上下文（T1）：LogEntry 记录时的 session/match/frame/player 快照。
    /// 钉住三件事——条目带得上、清上下文只清自己那局、未开局日志保持空态。
    /// </summary>
    public sealed class LogContextEditModeTests
    {
        [SetUp]
        public void SetUp() => Log.ResetForTesting();

        [TearDown]
        public void TearDown() => Log.ResetForTesting();

        [Test]
        public void 日志条目_带对局帧与席位()
        {
            Log.SetMatchContext("match-abcdef", 3);
            Log.SetFrame(42);
            Log.Info("hello", LogTag.Battle);

            LogEntry entry = Log.Recent[Log.Recent.Count - 1];
            Assert.AreEqual("match-abcdef", entry.MatchId);
            Assert.AreEqual(42, entry.Frame);
            Assert.AreEqual(3, entry.PlayerId);
            Assert.IsFalse(string.IsNullOrEmpty(entry.SessionId), "会话 id 进程内生成一次，不应为空");
        }

        [Test]
        public void 清上下文_只清自己那局()
        {
            Log.SetMatchContext("m1", 1);
            Log.ClearMatchContext("m2");
            Assert.AreEqual("m1", Log.CurrentMatchId, "旧局晚拆不能清掉新局");

            Log.ClearMatchContext("m1");
            Assert.AreEqual(string.Empty, Log.CurrentMatchId);
            Assert.AreEqual(-1, Log.CurrentPlayerId);
            Assert.AreEqual(0, Log.CurrentFrame);
        }

        [Test]
        public void 未开局日志_上下文为空()
        {
            Log.Info("boot");
            LogEntry entry = Log.Recent[0];
            Assert.AreEqual(string.Empty, entry.MatchId);
            Assert.AreEqual(-1, entry.PlayerId);
        }
    }
}
