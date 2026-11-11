#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using LiteNet.Transport;

using LiteGame.Adapters.Dev.Debug.WeakNet;
using LiteGame.Adapters.Dev.Debug.WeakNet.Profile;
namespace LiteGame.Adapters.Dev.Debug.WeakNet.Transport
{
    /// <summary>传输工厂包装：只包真 KCP 形态，本地测试房（进程内服）不包。</summary>
    public static class WeakNetWiring
    {
        /// <summary>包装传输（规则持有件由会话域自己持有，本层不碰）：本地测试房不注入弱网，
        /// 真 KCP 形态按运行时状态包装；无运行时 = 原样返回。</summary>
        public static IClientTransport Wrap(bool testRoom, IClientTransport transport, WeakNetRuntime runtime)
        {
            if (transport == null) return null;
            if (runtime == null) return transport;
            if (testRoom)
            {
                runtime.Detach();
                return transport;
            }
            return runtime.Wrap(transport);
        }
    }
}
#endif
