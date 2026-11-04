namespace LiteGame
{
    /// <summary>
    /// 本地传输工厂的登记口（M0 接缝 5 的跨程序集装订点）：开发装配（`LiteGame.Dev`）在
    /// `RuntimeInitializeOnLoadMethod` 里 <see cref="Bind"/>，App 侧只读。
    ///
    /// **为什么是静态槽**：App 不得引用 Dev 程序集（单向依赖），而组合根在 App——跨程序集装订只能经
    /// 一个薄登记口。本类刻意只做"一进一出"（Bind/Unbind/TryGet，无业务语义），M1 建 `LiteGame.Dev`
    /// 装配层后由 DevBootstrap 直接注入，本类随之退役。
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
