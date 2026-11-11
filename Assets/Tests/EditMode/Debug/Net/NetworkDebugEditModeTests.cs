using LiteNet;
using NUnit.Framework;

using LiteSync.Client;
using LiteGame.Adapters.Dev.Debug.NetDebug;
namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 网络调试组件（<see cref="NetworkDebug"/>）的契约：无对局空态 / 动作拒绝原因稳定 / 相位判定（断线仅 Connected·Restoring。）。
    /// </summary>
    public sealed class NetworkDebugEditModeTests
    {
        [Test]
        public void 无对局_断线与重连给稳定拒绝原因()
        {
            var network = new NetworkDebug(null, null);

            Assert.IsFalse(network.HasBattle, "无对局上下文");
            Assert.IsFalse(network.TryForceDisconnect(out string disconnectError));
            Assert.AreEqual("无对局（未进房）", disconnectError);
            Assert.IsFalse(network.TryBeginReconnect(out string reconnectError));
            Assert.AreEqual("无对局（未进房）", reconnectError);
        }

        [Test]
        public void 相位判定_断线仅在连相位_重连仅在疑似丢失相位()
        {
            Assert.IsTrue(NetworkDebug.CanForceDisconnect(ClientSessionPhase.Connected));
            Assert.IsTrue(NetworkDebug.CanForceDisconnect(ClientSessionPhase.Restoring));
            Assert.IsFalse(NetworkDebug.CanForceDisconnect(ClientSessionPhase.SuspectedLost));
            Assert.IsFalse(NetworkDebug.CanForceDisconnect(ClientSessionPhase.Reconnecting));
            Assert.IsFalse(NetworkDebug.CanForceDisconnect(ClientSessionPhase.Idle));

            Assert.IsTrue(NetworkDebug.CanBeginReconnect(ClientSessionPhase.SuspectedLost));
            Assert.IsFalse(NetworkDebug.CanBeginReconnect(ClientSessionPhase.Connected));
            Assert.IsFalse(NetworkDebug.CanBeginReconnect(ClientSessionPhase.Reconnecting));
            Assert.IsFalse(NetworkDebug.CanBeginReconnect(ClientSessionPhase.Failed));
        }

        [Test]
        public void 无对局_读数空态且弱网未启用()
        {
            var network = new NetworkDebug(null, null);

            Assert.AreEqual(ClientSessionPhase.Idle, network.Phase);
            Assert.IsFalse(network.Quality.HasSample, "无对局无 RTT/丢包样本");
            Assert.IsFalse(network.WeakNet.Enabled, "弱网未装配 = 未启用");
            Assert.AreEqual(string.Empty, network.MatchId);
        }

        [Test]
        public void 无对局_自动重连抑制开关给稳定拒绝原因()
        {
            var network = new NetworkDebug(null, null);

            Assert.IsFalse(network.SuppressAutoReconnect, "无对局 = 未抑制");
            Assert.IsFalse(network.TrySetSuppressAutoReconnect(true, out string error));
            Assert.AreEqual("无对局（未进房）", error);
            Assert.IsFalse(network.SuppressAutoReconnect, "拒绝后状态不变");
        }
    }
}
