using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;


namespace LiteFramework
{
    /// <summary>
    /// 单例服务的构造装配器(composition root 工具,不是 IoC 框架)。
    /// 纪律:构造函数注入(唯一最长公共构造,歧义抛);注册期查重;
    /// Seal 封注册面(Resolve 不受限);注册即发现(注册顺序 = 驱动顺序)。
    /// 诊断口径:错误在 Resolve 时点当场抛(未注册/循环依赖含链/构造抛真因),无 Validate 全量预检。
    /// 不做:属性注入/自动扫描/AOP/线程安全/Transient-Scoped。主线程 only。
    /// AOT:显式泛型注册零扫描;构造反射可用,实现类写进 link.xml。
    /// </summary>
    public sealed class ServiceContainer : IServiceContainer, IModuleStats
    {
        private sealed class Entry
        {
            public Type Interface;
            public Type Impl;                                   // Register 型
            public Func<IServiceContainer, object> Factory;     // RegisterFactory 型(须纯构造:无 IO 无副作用无时序)
            public object Instance;                             // 单例缓存(含 RegisterInstance)
            public ConstructorInfo Ctor;                        // 惰性选择并缓存
            public bool Constructed;
        }

        /// <summary>注册打点开关——GameEntry 按 Editor/Development 置位,发布恒 false。</summary>
        public static bool EnableRegistrationTrace;

        private readonly Dictionary<Type, Entry> _entries = new();
        private readonly List<Entry> _order = new();            // 注册顺序的真相源(装配清单)
        private readonly List<ITickable> _tickables = new();
        private readonly List<IModuleStats> _stats = new();
        private readonly HashSet<Type> _building = new();       // 解析中的类型(循环检测)
        private readonly List<Type> _chain = new();             // 依赖链(报错可读:A → B → C → A)
        private bool _sealed;
        public bool IsSealed { get { return _sealed; } }
        public IReadOnlyList<ITickable> Tickables => _tickables;
        public IReadOnlyList<IModuleStats> Stats => _stats;

        // ---- 注册(装配期 only;Seal 后全抛) ----

        public void Register<TInterface, TImpl>() where TImpl : TInterface
        {
            ThrowIfSealed();
            var iface = typeof(TInterface);
            if (_entries.ContainsKey(iface))
                throw new InvalidOperationException($"重复注册:{iface.Name}——注册期当场抛,不静默覆盖");
            var e = new Entry { Interface = iface, Impl = typeof(TImpl) };
            _entries.Add(iface, e); _order.Add(e);
            Trace(iface, e.Impl);
        }

        public void RegisterInstance<TInterface>(TInterface instance)
        {
            ThrowIfSealed();
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            var iface = typeof(TInterface);
            if (_entries.ContainsKey(iface))
                throw new InvalidOperationException($"重复注册:{iface.Name}——注册期当场抛,不静默覆盖");
            var e = new Entry { Interface = iface, Instance = instance, Constructed = true };
            _entries.Add(iface, e); _order.Add(e);
            Discover(e);
            Trace(iface, instance.GetType());
        }

        public void RegisterFactory<TInterface>(Func<IServiceContainer, TInterface> factory)
        {
            ThrowIfSealed();
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var iface = typeof(TInterface);
            if (_entries.ContainsKey(iface))
                throw new InvalidOperationException($"重复注册:{iface.Name}——注册期当场抛,不静默覆盖");
            var e = new Entry { Interface = iface, Factory = c => (object)factory(c) };
            _entries.Add(iface, e); _order.Add(e);
            Trace(iface, null);
        }

        // ---- 解析(Seal 后不受限) ----

        public T Resolve<T>() => (T)Resolve(typeof(T));

        private object Resolve(Type iface)
        {
            if (!_entries.TryGetValue(iface, out var e))
                throw new InvalidOperationException(
                    $"未注册的服务:{iface.Name}——一切服务在 ProcedureLaunch 显式注册(无自动扫描)");
            if (e.Constructed) return e.Instance;
            if (!_building.Add(iface))
                throw new InvalidOperationException(
                    $"循环依赖: {FormatChain(iface)}——不做自动解(工厂/属性注入是逃避,拆依赖才是修)");
            _chain.Add(iface);
            try
            {
                object instance = e.Factory != null ? e.Factory(this) : Construct(e);
                e.Instance = instance;
                e.Constructed = true;
                Discover(e);
                return instance;
            }
            finally
            {
                _chain.RemoveAt(_chain.Count - 1);
                _building.Remove(iface);
            }
        }

        private object Construct(Entry e)
        {
            if (e.Ctor == null) e.Ctor = SelectCtor(e.Impl);
            var pars = e.Ctor.GetParameters();
            var args = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
            {
                Type dep = pars[i].ParameterType;
                if (!_entries.ContainsKey(dep))
                    throw new InvalidOperationException(
                        $"构造依赖未注册:{e.Impl.Name} 需要 {dep.Name}——裸值/配置经 RegisterInstance 或工厂提供");
                args[i] = Resolve(dep);                         // 递归
            }
            try { return e.Ctor.Invoke(args); }
            catch (TargetInvocationException tie)               // 解包,报真因
            {
                var inner = tie.InnerException ?? tie;
                throw new InvalidOperationException($"构造抛异常:{e.Impl.Name}({inner.Message})", inner);
            }
        }

        private static ConstructorInfo SelectCtor(Type impl)
        {
            var ctors = impl.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
            if (ctors.Length == 0)
                throw new InvalidOperationException($"无公共构造函数:{impl.Name}");
            int max = -1; ConstructorInfo best = null; bool ambiguous = false;
            foreach (var c in ctors)
            {
                int n = c.GetParameters().Length;
                if (n > max) { max = n; best = c; ambiguous = false; }
                else if (n == max) ambiguous = true;
            }
            if (ambiguous)
                throw new InvalidOperationException(
                    $"构造函数歧义:{impl.Name} 有多个 {max} 参公共构造——保留唯一最长构造(诊断与 AOT 都要求确定)");
            return best;
        }

        public void Seal()
        {
            if (_sealed) throw new InvalidOperationException("重复 Seal");
            _sealed = true;
        }

        // ---- 内部 ----

        private void ThrowIfSealed()
        {
            if (_sealed)
                throw new InvalidOperationException("容器已密封——装配只发生在 ProcedureLaunch(Seal 后一切 Register* 抛)");
        }

        private void Discover(Entry e)
        {
            // 同一实例可注册在多个接口下——防双重 Tick(Contains 守卫,池化实体教训同款)
            if (e.Instance is ITickable t && !_tickables.Contains(t)) _tickables.Add(t);
            if (e.Instance is IModuleStats s && !_stats.Contains(s)) _stats.Add(s);
        }

        private string FormatChain(Type requested)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _chain.Count; i++) sb.Append(_chain[i].Name).Append(" → ");
            sb.Append(requested.Name);
            return sb.ToString();
        }

        private static void Trace(Type iface, Type impl)
        {
            if (EnableRegistrationTrace)
                Log.Info($"{iface.Name} → {(impl != null ? impl.Name : "(工厂)")}", "DI");
        }

        // ---- 自身即 stats 源(自注册后 HUD 可见) ----

        public string StatsName => "DI";
        public void Snapshot(Dictionary<string, string> into)
        {
            int built = 0;
            foreach (var e in _order) if (e.Constructed) built++;
            into["注册数"] = _entries.Count.ToString();
            into["已构造"] = built.ToString();
            into["Tickable"] = _tickables.Count.ToString();
            into["已密封"] = _sealed ? "是" : "否";
        }
    }
}
