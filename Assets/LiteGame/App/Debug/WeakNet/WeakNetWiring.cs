#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using LiteNet.Transport;

namespace LiteGame
{
    /// <summary>传输工厂包装：只包真 KCP 形态，本地测试房（进程内服）不包。</summary>
    public static class WeakNetWiring
    {
        /// <summary>包装传输工厂（规则持有件原样透传）：本地测试房不注入弱网，真 KCP 形态按运行时状态包装。</summary>
        public static Func<bool, MatchTransport> WrapFactory(
            Func<bool, MatchTransport> factory, WeakNetRuntime runtime)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (runtime == null) return factory;
            return testRoom =>
            {
                MatchTransport match = factory(testRoom);
                if (match.Transport == null) return match;
                if (testRoom)
                {
                    runtime.Detach();
                    return match;
                }
                return new MatchTransport(runtime.Wrap(match.Transport), match.Rules);
            };
        }
    }
}
#endif
