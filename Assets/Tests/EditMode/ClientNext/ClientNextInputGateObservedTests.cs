using LiteGame.Adapters.Dev.LocalServer;
using LiteSim;
using RoomServer.Runtime;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 输入门可观测面（S3-3）：服务端 InputGate 的丢弃计数与步进数在客户端/测试侧可直接读，
    /// 这是判定 C3/C4/C5 是否在实际对局中发生的唯一证据来源。
    /// </summary>
    public sealed class ClientNextInputGateObservedTests
    {
        private static SimMapData FlatMap()
        {
            var map = new SimMapData { GroundY = 0f, HalfWidth = 10f, HalfDepth = 10f, SpawnPointCount = 2 };
            map.SpawnPoints[0] = new SimVector3(0f, 0f, -3f);
            map.SpawnPoints[1] = new SimVector3(0f, 0f, 3f);
            return map;
        }

        [Test]
        public void 门计数族_全部可读且初值为零()
        {
            using LocalServerTransport transport = LocalServerTransport.ForRoom("gate-probe", 2, null, 0u, FlatMap());
            InputGate gate = transport.Runtime.Gate;
            Assert.AreEqual(0L, gate.AcceptedCount, "AcceptedCount");
            Assert.AreEqual(0L, gate.DroppedStaleFrame, "DroppedStaleFrame");
            Assert.AreEqual(0L, gate.DroppedOutOfRange, "DroppedOutOfRange");
            Assert.AreEqual(0L, gate.DroppedIllegalVector, "DroppedIllegalVector");
            Assert.AreEqual(0L, gate.DroppedDuplicateFrame, "DroppedDuplicateFrame");
            Assert.AreEqual(0L, gate.DroppedAckSnapshot, "DroppedAckSnapshot");
            Assert.AreEqual(0L, gate.DroppedIllegalButtons, "DroppedIllegalButtons");
            Assert.AreEqual(0L, gate.DroppedNonFinite, "DroppedNonFinite");
        }

        [Test]
        public void 运行时诊断计数_可读()
        {
            using LocalServerTransport transport = LocalServerTransport.ForRoom("gate-run", 2, null, 0u, FlatMap());
            RoomRuntime runtime = transport.Runtime;
            Assert.GreaterOrEqual(runtime.StepsCount, 0L, "StepsCount");
            Assert.GreaterOrEqual(runtime.FireInputsProcessed, 0L, "FireInputsProcessed");
            Assert.IsNotNull(runtime.Gate, "Gate 应可访问");
        }
    }
}
