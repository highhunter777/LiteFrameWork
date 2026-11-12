using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGameNext.Platform.Localization;
using UnityEngine;

namespace LiteGameNext.Platform.Ui
{
    /// <summary>UI 运行时：页面栈/层/模态/Back/缓存/输入阻断/逐帧驱动；页面由构造注入的工厂创建（P0 测试工厂，P1 prefab loader）。</summary>
    public sealed class UiRuntime : IDisposable
    {
        /// <summary>缓存预算默认值：关闭后保留在缓存中的页面实例上限（Resident 不受淘汰）。</summary>
        public const int DefaultCacheBudget = 16;

        private const float DefaultTransitionSeconds = 0.15f;

        private readonly Func<PageSpec, CancellationToken, UniTask<PageBase>> _pageFactory;
        private readonly Dictionary<string, PageEntry> _cache = new Dictionary<string, PageEntry>(StringComparer.Ordinal);
        private readonly List<PageEntry> _stack = new List<PageEntry>();
        private readonly int _cacheBudget;
        private LocalizationService _localization;
        private int _inputBlockCount;
        private int _lruClock;
        private float _transitionRemaining;

        /// <summary>无工厂构造：只供 P0 启动链（Initialize/Tick/占位页）；打开页面需要工厂。</summary>
        public UiRuntime() : this(null, DefaultCacheBudget)
        {
        }

        /// <summary>构造：工厂按 Spec.Address 产出页面实例（测试用代码建对象，P1 接 prefab loader）。</summary>
        public UiRuntime(Func<PageSpec, CancellationToken, UniTask<PageBase>> pageFactory, int cacheBudget = DefaultCacheBudget)
        {
            if (cacheBudget < 1) throw new ArgumentOutOfRangeException(nameof(cacheBudget), cacheBudget, "缓存预算必须 ≥ 1");
            _pageFactory = pageFactory;
            _cacheBudget = cacheBudget;
        }

        /// <summary>是否已初始化（Tick/打开页面要求已初始化）。</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>注入本地化：此后打开的页面可经 <c>PageBase.Text(key)</c> 取表文案。</summary>
        public void AttachLocalization(LocalizationService localization) => _localization = localization;

        /// <summary>累计帧时间（秒），供转场/动效按需推进。</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>输入是否被 UI 阻断（由模态页/全屏页计数决定）。</summary>
        public bool IsInputBlocked => _inputBlockCount > 0;

        /// <summary>输入阻断计数（模态页与全屏页各计 1）。</summary>
        public int InputBlockCount => _inputBlockCount;

        /// <summary>当前打开页面数（栈深）。</summary>
        public int OpenPageCount => _stack.Count;

        /// <summary>缓存中的页面实例数（不含已打开页面）。</summary>
        public int CachedPageCount => CountIdleCached();

        /// <summary>缓存预算（构造期固定）。</summary>
        public int CacheBudget => _cacheBudget;

        /// <summary>是否处于转场窗口（由 Tick 推进，超时自动结束）。</summary>
        public bool IsTransitioning => _transitionRemaining > 0f;

        /// <summary>栈顶页面句柄；无打开页面时为 null。</summary>
        public PageHandle Top => _stack.Count == 0 ? null : _stack[_stack.Count - 1].Handle;

        /// <summary>初始化运行时；重复调用幂等。</summary>
        public void Initialize()
        {
            if (IsInitialized) return;
            IsInitialized = true;
            ElapsedSeconds = 0f;
            _transitionRemaining = 0f;
        }

        /// <summary>逐帧驱动：累计时间、推进转场窗口、转发打开页面的 OnTick；未初始化或非正增量时为空操作。</summary>
        public void Tick(float deltaTime)
        {
            if (!IsInitialized || deltaTime <= 0f) return;
            ElapsedSeconds += deltaTime;
            if (_transitionRemaining > 0f) _transitionRemaining = Mathf.Max(0f, _transitionRemaining - deltaTime);

            PageEntry[] snapshot = _stack.ToArray();
            for (int index = 0; index < snapshot.Length; index++)
                if (snapshot[index].IsOpen) snapshot[index].Page.Tick(deltaTime);
        }

        /// <summary>占位页入口（P0 启动可观察点）：无页面工厂时只记日志；真实页面走 OpenAsync。</summary>
        public void OpenPlaceholder()
        {
            if (!IsInitialized) return;
            Log.Info("UiRuntime 就绪：打开页面 " + _stack.Count + " 个，页面工厂=" + (_pageFactory != null));
        }

        /// <summary>打开页面：创建（非激活）→ 绑定依赖 → 激活 → 入栈 → OnShow；返回本展示代次的句柄。</summary>
        public async UniTask<PageHandle> OpenAsync<TPage>(PageSpec spec, Action<TPage> bind = null,
            CancellationToken cancellationToken = default) where TPage : PageBase
        {
            PageEntry entry = await AcquireEntryAsync(spec, cancellationToken);
            if (!(entry.Page is TPage page))
            {
                string actual = entry.Page == null ? "null" : entry.Page.GetType().Name;
                DropEntry(entry);
                throw new InvalidOperationException("页面地址 " + spec.Address + " 的根组件不是 "
                    + typeof(TPage).Name + "（实际：" + actual + "）");
            }

            try
            {
                SetInactive(entry.Page);
                entry.Page.AttachLocalization(_localization);
                entry.Page.Prepare();
                bind?.Invoke(page);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                DropEntry(entry);
                throw;
            }

            return Activate(entry);
        }

        /// <summary>关闭栈顶页面后打开新页（替换语义）；栈空时等价于 OpenAsync。</summary>
        public async UniTask<PageHandle> ReplaceAsync<TPage>(PageSpec spec, Action<TPage> bind = null,
            CancellationToken cancellationToken = default) where TPage : PageBase
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (_stack.Count > 0) CloseEntry(_stack[_stack.Count - 1]);
            return await OpenAsync(spec, bind, cancellationToken);
        }

        /// <summary>关闭指定页面；已关闭句柄或未知句柄为空操作。</summary>
        public UniTask CloseAsync(PageHandle handle)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            return handle.IsClosed ? UniTask.CompletedTask : CloseHandleAsync(handle);
        }

        /// <summary>平台返回：系统页 → 模态页 → 普通页；仅剩根页面时返回 false（不关空屏）。</summary>
        public UniTask<bool> BackAsync()
        {
            if (!IsInitialized || _stack.Count == 0) return UniTask.FromResult(false);

            PageEntry target = TopOfLayer(PageLayer.System) ?? TopOfLayer(PageLayer.Modal);
            if (target == null)
            {
                if (_stack.Count <= 1) return UniTask.FromResult(false);
                target = _stack[_stack.Count - 1];
            }

            CloseEntry(target);
            return UniTask.FromResult(true);
        }

        /// <summary>关闭全部页面并释放缓存；重复调用为空操作。</summary>
        public void Shutdown()
        {
            if (!IsInitialized) return;

            PageEntry[] open = _stack.ToArray();
            for (int index = open.Length - 1; index >= 0; index--) CloseEntry(open[index]);

            var idle = new List<PageEntry>(_cache.Values);
            _cache.Clear();
            _stack.Clear();
            for (int index = 0; index < idle.Count; index++) ReleasePage(idle[index]);

            _inputBlockCount = 0;
            _transitionRemaining = 0f;
            IsInitialized = false;
        }

        /// <summary>为 Lifetime 逆序释放提供入口。</summary>
        public void Dispose() => Shutdown();

        private async UniTask<PageEntry> AcquireEntryAsync(PageSpec spec, CancellationToken cancellationToken)
        {
            EnsureOpenable(spec);

            if (_cache.TryGetValue(spec.Address, out PageEntry cached))
            {
                if (cached.IsOpen)
                    throw new InvalidOperationException("页面已打开，不能重复打开（地址 " + spec.Address + "）：先 CloseAsync 或复用句柄");
                cached.Spec = spec;
                cached.LastTouch = ++_lruClock;
                return cached;
            }

            PageBase page = await _pageFactory(spec, cancellationToken);
            if (page == null) throw new InvalidOperationException("页面工厂返回 null（地址 " + spec.Address + "）");

            var entry = new PageEntry(spec, page) { LastTouch = ++_lruClock };
            _cache.Add(spec.Address, entry);
            return entry;
        }

        private PageHandle Activate(PageEntry entry)
        {
            int generation = ++entry.DisplayCount;
            var handle = new PageHandle(entry.Page, entry.Spec, generation, CloseHandleAsync);
            entry.Page.Attach(handle);
            entry.Handle = handle;
            entry.IsOpen = true;
            entry.LastTouch = ++_lruClock;

            entry.Page.gameObject.SetActive(true);
            _stack.Add(entry);
            if (BlocksInput(entry.Spec)) _inputBlockCount++;
            _transitionRemaining = DefaultTransitionSeconds;
            entry.Page.Show();
            return handle;
        }

        private UniTask CloseHandleAsync(PageHandle handle)
        {
            PageEntry entry = FindEntry(handle);
            if (entry != null) CloseEntry(entry);
            return UniTask.CompletedTask;
        }

        private void CloseEntry(PageEntry entry)
        {
            if (entry == null || !entry.IsOpen) return;

            entry.IsOpen = false;
            _stack.Remove(entry);
            if (BlocksInput(entry.Spec) && _inputBlockCount > 0) _inputBlockCount--;
            entry.Page.Hide();
            // 缓存页关闭后必须离开屏幕：仅 Hide 不隐藏 GameObject，Lru/Resident 会留在画面上。
            entry.Page.gameObject.SetActive(false);

            PageHandle handle = entry.Handle;
            entry.Handle = null;
            handle?.MarkClosed();
            _transitionRemaining = DefaultTransitionSeconds;

            if (entry.Spec.CachePolicy == PageCachePolicy.DestroyOnClose)
            {
                _cache.Remove(entry.Spec.Address);
                ReleasePage(entry);
                return;
            }

            entry.LastTouch = ++_lruClock;
            EvictOverBudget();
        }

        private void EvictOverBudget()
        {
            while (CountIdleCached() > _cacheBudget)
            {
                PageEntry victim = null;
                foreach (PageEntry entry in _cache.Values)
                {
                    if (entry.IsOpen || entry.Spec.CachePolicy != PageCachePolicy.Lru) continue;
                    if (victim == null || entry.LastTouch < victim.LastTouch) victim = entry;
                }

                if (victim == null) return;
                _cache.Remove(victim.Spec.Address);
                ReleasePage(victim);
            }
        }

        private void DropEntry(PageEntry entry)
        {
            if (entry == null) return;
            _cache.Remove(entry.Spec.Address);
            _stack.Remove(entry);
            entry.IsOpen = false;
            entry.Handle = null;
            ReleasePage(entry);
        }

        private int CountIdleCached()
        {
            int count = 0;
            foreach (PageEntry entry in _cache.Values)
                if (!entry.IsOpen) count++;
            return count;
        }

        private PageEntry TopOfLayer(PageLayer layer)
        {
            for (int index = _stack.Count - 1; index >= 0; index--)
                if (_stack[index].Spec.Layer == layer) return _stack[index];
            return null;
        }

        private PageEntry FindEntry(PageHandle handle)
        {
            for (int index = 0; index < _stack.Count; index++)
                if (ReferenceEquals(_stack[index].Handle, handle)) return _stack[index];
            foreach (PageEntry entry in _cache.Values)
                if (ReferenceEquals(entry.Handle, handle)) return entry;
            return null;
        }

        private void EnsureOpenable(PageSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (!IsInitialized) throw new InvalidOperationException("UiRuntime 未初始化：先调用 Initialize()");
            if (_pageFactory == null)
                throw new InvalidOperationException("UiRuntime 未配置页面工厂：构造时传入 Func<PageSpec, CancellationToken, UniTask<PageBase>>");
        }

        private static bool BlocksInput(PageSpec spec) => spec.Layer == PageLayer.Modal || spec.FullScreen;

        private static void SetInactive(PageBase page)
        {
            if (page.gameObject.activeSelf) page.gameObject.SetActive(false);
        }

        private static void ReleasePage(PageEntry entry)
        {
            PageBase page = entry?.Page;
            if (page == null) return;
            entry.Page = null;

            page.DisposePage();
            GameObject root = page.gameObject;
            if (root == null) return;
            root.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }

        private sealed class PageEntry
        {
            public PageEntry(PageSpec spec, PageBase page)
            {
                Spec = spec;
                Page = page;
            }

            public PageSpec Spec { get; set; }

            public PageBase Page { get; set; }

            public PageHandle Handle { get; set; }

            public bool IsOpen { get; set; }

            public int DisplayCount { get; set; }

            public int LastTouch { get; set; }
        }
    }
}
