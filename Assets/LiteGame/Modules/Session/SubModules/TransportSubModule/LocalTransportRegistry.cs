namespace LiteGame
{
    /// <summary>
    /// 本地传输工厂的登记口（M0 接缝 5 的跨程序集装订点）：开发装配（`LiteGame.Dev`）在 `RuntimeInitializeOnLoadMethod` 里 Bind，App 侧只读。
    /// </summary>
    public static class LocalTransportRegistry
    {
        private static ILocalTransportFactory s_factory;

        public static void Bind(ILocalTransportFactory factory)
        {
            if (factory == null) return;
            s_factory = factory;
        }

        public static void Unbind(ILocalTransportFactory factory)
        {
            if (ReferenceEquals(s_factory, factory)) s_factory = null;
        }

        public static bool TryGet(out ILocalTransportFactory factory)
        {
            factory = s_factory;
            return factory != null;
        }
    }
}
