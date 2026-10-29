using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteFramework.UI
{
    /// <summary>
    /// 页面逻辑绑定与换表（从 <see cref="UIService"/> 外提的"逻辑生命周期"域，M3-6 §4.5）：
    /// 解析（prefab 自带 C# 逻辑优先 → LuaPath → 降级 NullLogic）、注入缝（语言/主题）、
    /// 运行期换表（标记 / 池中立即换 / 打开中关闭后换 / env 重建落空与补解析）。
    ///
    /// **边界**：只动 <see cref="UIForm"/> 的逻辑与注入字段 + 待换表标记；不碰实例注册、转场、
    /// 遮盖与开关时序（那些归 <see cref="UIService"/>）。换表时机仍由服务在关闭/复用事务序里驱动——
    /// 顺序不能反：OnHide 跑完前换表会让界面"一半旧一半新"。
    /// </summary>
    internal sealed class UIFormLogicBinder
    {
        private readonly UIFormStore _store;
        private readonly Func<PageDefinition, IUIFormLogic> _resolver;
        private readonly Func<ILocalizationService> _localeProvider;   // LText 装配缝（null = 不注入）
        private readonly Func<UiThemeAsset> _themeProvider;            // 主题装配缝（null = 页面保持烘焙值）
        private readonly HashSet<int> _stale = new HashSet<int>();      // 逻辑待更新（运行期增量重填，§2.3）

        public UIFormLogicBinder(UIFormStore store,
            Func<PageDefinition, IUIFormLogic> resolver = null,
            Func<ILocalizationService> localeProvider = null,
            Func<UiThemeAsset> themeProvider = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _resolver = resolver;
            _localeProvider = localeProvider;
            _themeProvider = themeProvider;
        }

        /// <summary>仍带"待更新"标记的界面数（HUD 读数：&gt;0 = 新逻辑尚未在该界面生效）。</summary>
        public int StaleCount => _stale.Count;

        /// <summary>新实例绑定：解析逻辑 + 注入语言/主题缝（打开管线在 OnInit 之前调用）。</summary>
        public void BindNew(UIForm form)
        {
            form.Logic = Resolve(form.Info, form.Root);
            form.LocaleProvider = _localeProvider;
            form.ThemeProvider = _themeProvider;
        }

        /// <summary>
        /// 全量打标"逻辑已过期"（幂等）。由重填服务在**清注册表之前**调用——先标后清，窗口内界面仍能用旧表跑完。
        /// Active 界面**不立刻换表**（§2.3：已打开的界面换代码会"一半旧一半新"），等它关闭后再换。
        /// </summary>
        public void MarkAllStale()
        {
            foreach (var form in _store.All) _stale.Add(form.Id);
        }

        /// <summary>单界面打标（调试口用）。</summary>
        public void MarkStale(int formId) => _stale.Add(formId);

        /// <summary>重填完成后调用：池中（Recycled）界面立刻换表；Active/Covered/Paused 保留标记，等 Close 时换。</summary>
        public void ApplyToRecycled()
        {
            foreach (var form in _store.All)
                if (form.State == UIFormState.Recycled) SwapIfStale(form);
        }

        /// <summary>
        /// 换表（幂等）：旧适配器解绑并释放 Lua 引用 → 用当前注册表重新解析 → 池中界面标记需补跑 OnInit。
        /// 只在"已无人在用旧逻辑"时调用（Reuse 之前 / EnterClosing 之后 / ApplyToRecycled 的池中件）。
        /// </summary>
        public void SwapIfStale(UIForm form)
        {
            if (!_stale.Remove(form.Id)) return;

            form.Logic?.Release();   // 通用钩子——UI 服务不认识脚本适配器（§5.1）
            form.Logic = Resolve(form.Info, form.Root);
            if (form.State == UIFormState.Recycled) form.NeedsReinit = true;
        }

        /// <summary>
        /// env 重建后的逻辑补解析（§10.2）：只有"逻辑已被 <see cref="DropAll"/> 落空且标记待重解析"
        /// 才动它——C# 逻辑与非空逻辑一律不越权替换。
        /// </summary>
        public void EnsureAfterReload(UIForm form)
        {
            if (!form.NeedsReinit) return;
            if (!ReferenceEquals(form.Logic, NullUIFormLogic.Instance)) return;
            form.Logic = Resolve(form.Info, form.Root);
        }

        /// <summary>
        /// 逻辑全体落空（§10.2 env 重建前置）：逐个释放适配器持有的旧 Lua 引用、逻辑置空并把界面标记为
        /// "复用前需重新解析 + 补跑 OnInit"。**必须在 Lua env Dispose 之前调用**（释放引用要趁 env 还活着）。
        /// </summary>
        public void DropAll()
        {
            foreach (var form in _store.All)
            {
                form.DropLogic();
                form.NeedsReinit = true;
            }
            _stale.Clear();                              // 待更新标记已被"落空 + 重解析"承接
        }

        /// <summary>实例销毁时摘除待换表标记（幂等）。</summary>
        public void Forget(int formId) => _stale.Remove(formId);

        /// <summary>
        /// 逻辑解析（§3.4 降级口径）：**prefab 自带 C# 逻辑组件优先**（UIBindBase 派生页挂 prefab 根，
        /// 生成器登记控件——显式挂件优先于表行 Lua 逻辑）；空 LuaPath = 纯展示面（正常降级**不打日志**，
        /// 否则纯展示面会凭空制造"Console 新增 Error"的 UI Lane 红灯）；resolver 失败 → 降级 NullLogic。
        /// </summary>
        private IUIFormLogic Resolve(PageDefinition info, UnityEngine.GameObject root)
        {
            if (root != null)
            {
                var carried = root.GetComponentInChildren<IUIFormLogic>(true);
                if (carried != null) return carried;
            }

            if (string.IsNullOrEmpty(info.LuaPath)) return NullUIFormLogic.Instance;

            if (_resolver == null) return NullUIFormLogic.Instance;
            try
            {
                var logic = _resolver(info);
                return logic ?? NullUIFormLogic.Instance;
            }
            catch (Exception ex)
            {
                Log.Error($"界面逻辑解析失败[{info.LuaPath}]:{ex.Message}", LogTag.UI);
                return NullUIFormLogic.Instance;
            }
        }
    }
}
