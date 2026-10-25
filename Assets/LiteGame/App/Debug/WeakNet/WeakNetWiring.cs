#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using LiteNet.Transport;

namespace LiteGame
{
    /// <summary>传输工厂包装：只包真 KCP 形态，本地测试房（进程内服）不包。</summary>
    public static class WeakNetWiring
    {
        public static Func<bool, IClientTransport> WrapFactory(
            Func<bool, IClientTransport> factory, WeakNetRuntime runtime)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (runtime == null) return factory;
            return testRoom =>
            {
                IClientTransport transport = factory(testRoom);
                if (transport == null) return null;
                if (testRoom)
                {
                    runtime.Detach();
                    return transport;
                }
                return runtime.Wrap(transport);
            };
        }
    }
}
#endif
