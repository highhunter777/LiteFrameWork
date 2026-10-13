using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;
using XLua;
using cfg;

namespace LiteGame
{
    /// <summary>
    /// 服务桥根类型：Lua 全局表四件（data/ui/content/text）的宿主。启动时绑定成 Lua 全局表，
    /// **白名单只此四件**——ILuaRegistry/玩法系统一律不导出（设计方案 §4.3 服务桥纪律）。
    /// 产品侧另有联机门面（meta）经 App 的 <c>ProductBridges/MetaBridge</c> 装订（批C-③——
    /// 依赖方向不可反向：LuaBridge 不引用 App，见 asmdef 图）；两路 env 重建（Preload/DevReload）
    /// 共用 <c>ProductBridges.BindGlobals</c> 单源。
    /// 绑定形态：不走生成 wrapper——门面方法以白名单委托（Func&lt;int, LuaTable&gt;）注册，
    /// 行字段在 Runtime 层手工展开（Lua 不触碰 Luban 类型，§1d）；注册表实例经 BindRegistries
    /// 装配期注入（装配根）。
    /// </summary>
    public static class Bridge
    {
        private static Func<Tables> s_tables;                  // 装配期绑定：装配点调 Bind，不碰容器
        private static IUILuaRegistry s_uiRegistry;
        private static IContentLuaRegistry s_contentRegistry;
        private static UIService s_uiService;
        private static UINavigationController s_nav;           // Lua 侧打开的单写者后端（三通道收口）
        private static Func<ILocalizationService> s_localeProvider;   // LText 门面后端（懒建工厂——表装载在 Preload）

        /// <summary>绑定查表入口（经 IConfigService.Tables；未加载时其 Tables 访问器会抛）。</summary>
        public static void Bind(Func<Tables> tables)
            => s_tables = tables ?? throw new ArgumentNullException(nameof(tables));

        /// <summary>装配期注入注册表（ui/content 骨架门面的查询底座）。</summary>
        public static void BindRegistries(IUILuaRegistry ui, IContentLuaRegistry content)
        {
            s_uiRegistry = ui ?? throw new ArgumentNullException(nameof(ui));
            s_contentRegistry = content ?? throw new ArgumentNullException(nameof(content));
        }

        /// <summary>装配期注入 UI 壳（真实门面 Show/Close/IsOpen 的后端）。</summary>
        public static void BindUIService(UIService uiService)
        {
            s_uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
        }

        /// <summary>装配期注入导航单写者（Lua 侧 Show 的后端——打开收口到单写者队列，三通道口径）。</summary>
        public static void BindNavigation(UINavigationController nav)
        {
            s_nav = nav ?? throw new ArgumentNullException(nameof(nav));
        }

        /// <summary>装配期注入语言服务工厂（text 门面后端；与页面索引 BindLocale 共用同一实例——UiShellModule）。</summary>
        public static void BindText(Func<ILocalizationService> localeProvider)
        {
            s_localeProvider = localeProvider ?? throw new ArgumentNullException(nameof(localeProvider));
        }

        /// <summary>
        /// 绑定 Bridge 四门面为 Lua 全局表：
        /// 由装配点/编辑器工具经 <c>LuaComponent.Init</c> 注入——宿主（LiteClient.Scripting.XLua）不认识 Bridge，
        /// 依赖方向 = 产品 → 框架。门面方法全走白名单委托；只导出四门面（data/ui/content/text），
        /// ILuaRegistry/玩法系统一律不导出；禁止 CS. 直引业务类型。
        /// 产品侧扩展门面（meta）经 <c>ProductBridges.BindGlobals</c> 同 env 装订（见类头注释）。
        /// </summary>
        public static void BindGlobals(LuaEnv env)
        {
            var data = env.NewTable();
            data.Set("GetItem", new Func<int, LuaTable>(id => Data.GetItemLua(env, id)));
            data.Set("GetUIForm", new Func<int, LuaTable>(id => Data.GetUIFormLua(env, id)));
            var ui = env.NewTable();
            ui.Set("GetLogic", new Func<int, LuaTable>(Ui.GetLogic));
            ui.Set("Show", new Action<int, LuaTable>(Ui.Show));          // 真实门面（委托需 Generate Code）
            ui.Set("Close", new Action<int>(Ui.Close));
            ui.Set("IsOpen", new Func<int, bool>(Ui.IsOpen));
            var content = env.NewTable();
            content.Set("GetProcessor", new Func<int, LuaTable>(Content.GetProcessor));
            var text = env.NewTable();
            text.Set("Get", new Func<string, string>(Text.Get));
            text.Set("Format", new Func<string, object, object, object, object, string>(Text.Format));
            text.Set("SetLocale", new Action<string>(Text.SetLocale));
            text.Set("Locale", new Func<string>(() => Text.Locale));
            var bridge = env.NewTable();
            bridge.Set("data", data);
            bridge.Set("ui", ui);
            bridge.Set("content", content);
            bridge.Set("text", text);
            env.Global.Set("Bridge", bridge);
        }

        private static Tables Tables() => s_tables();

        /// <summary>数据门面：查表。C# 侧强类型直返；Lua 侧经 GetXxxLua 手工展开并按行缓存。</summary>
        public static class Data
        {
            // ---- C# 侧（强类型直返；行对象由 Luban DataMap 自缓存，不重复建 C# 缓存）----

            /// <summary>查道具表（ItemConfig：类型 + 刷新/拾取/携带/使用 + 各类型效果数值）。</summary>
            public static cfg.itemconfig GetItem(int id) => Tables().Tbitemconfig.Get(id);

            /// <summary>查 UI 界面注册表（LuaPath/Prefab/层级/全屏）。</summary>
            public static cfg.uiform GetUIForm(int id) => Tables().Tbuiform.Get(id);

            /// <summary>查内容条目（解释器消费）。</summary>
            public static cfg.contententry GetContentEntry(int id) => Tables().Tbcontententry.Get(id);

            // ---- Lua 侧入口（§2.5）：行字段手工展开为 LuaTable + 按行缓存 ----
            // DevReload 时旧 env 的 LuaTable 引用全失效——ClearLuaCaches 由 §2.7 编排调用（顺序钉死）。

            private static readonly Dictionary<int, LuaTable> s_itemLua = new Dictionary<int, LuaTable>(16);
            private static readonly Dictionary<int, LuaTable> s_uiFormLua = new Dictionary<int, LuaTable>(16);

            /// <summary>Lua 侧查道具表：同 id 恒返回同一 LuaTable 实例（缓存命中）。
            /// 全字段展开（19 列）——道具玩法（拾取/使用/投掷）在 Lua 侧落地时按需取用。</summary>
            public static LuaTable GetItemLua(LuaEnv env, int id)
            {
                if (s_itemLua.TryGetValue(id, out var hit)) return hit;
                var row = GetItem(id);
                var t = env.NewTable();
                t.Set("Id", row.Id);
                t.Set("Type", row.Type);
                t.Set("Name", row.Name);
                t.Set("SpawnInterval", row.SpawnInterval);
                t.Set("MaxAlive", row.MaxAlive);
                t.Set("PickupRadius", row.PickupRadius);
                t.Set("CarryLimit", row.CarryLimit);
                t.Set("UseDuration", row.UseDuration);
                t.Set("HealAmount", row.HealAmount);
                t.Set("ShieldAmount", row.ShieldAmount);
                t.Set("GrenadeDamage", row.GrenadeDamage);
                t.Set("GrenadeRadius", row.GrenadeRadius);
                t.Set("GrenadeFalloff", row.GrenadeFalloff);
                t.Set("EmpRadius", row.EmpRadius);
                t.Set("EmpDuration", row.EmpDuration);
                t.Set("RadarRadius", row.RadarRadius);
                t.Set("RadarDuration", row.RadarDuration);
                t.Set("ProjectileSpeed", row.ProjectileSpeed);
                t.Set("TravelDistance", row.TravelDistance);
                s_itemLua[id] = t;
                return t;
            }

            /// <summary>Lua 侧查 UI 界面行：同 id 恒返回同一 LuaTable 实例。</summary>
            public static LuaTable GetUIFormLua(LuaEnv env, int id)
            {
                if (s_uiFormLua.TryGetValue(id, out var hit)) return hit;
                var row = GetUIForm(id);
                var t = env.NewTable();
                t.Set("Id", row.Id);
                t.Set("LuaPath", row.LuaPath);
                t.Set("Prefab", row.Prefab);
                t.Set("Layer", row.Layer);
                t.Set("FullScreen", row.FullScreen);
                s_uiFormLua[id] = t;
                return t;
            }

            /// <summary>DevReload 专用（§2.7）：env 重建后旧 LuaTable 引用全失效，缓存必须清空。</summary>
            public static void ClearLuaCaches()
            {
                s_itemLua.Clear();
                s_uiFormLua.Clear();
            }
        }

        /// <summary>ui 门面（真实语义）：Show/Close/IsOpen(id)——**Lua 不碰 GameObject**。
        /// Show 经导航单写者排队（产品打开三通道收口口径），fire-and-forget（Lua 无 await），错误经日志观测。</summary>
        public static class Ui
        {
            /// <summary>打开界面：id → tbuiform 行（经单写者队列）；data 为 Lua 表（包装成 LuaUIData 传入生命周期）。</summary>
            public static void Show(int id, LuaTable data)
            {
                var aw = s_nav.GoAsync(id, data == null ? null : new LuaUIData(data)).GetAwaiter();
                aw.OnCompleted(() =>
                {
                    try { aw.GetResult(); }
                    catch (Exception ex) { Log.Error($"Bridge.ui.Show[{id}]:{ex.Message}", "UI"); }
                });
            }

            /// <summary>关闭界面（经出栈拦截 + 离场转场）。fire-and-forget 显式化（CS4014 收口）。</summary>
            public static void Close(int id) => s_uiService.CloseAsync(id).Forget();

            /// <summary>是否打开（Active/Covered/Paused 任一）。</summary>
            public static bool IsOpen(int id) => s_uiService.IsOpen(id);

            /// <summary>界面 id → 逻辑表查询（骨架口；适配器链路经 UIService 解析器，不常走此口）。</summary>
            public static LuaTable GetLogic(int id) => s_uiRegistry.Get(Data.GetUIForm(id).LuaPath);
        }

        /// <summary>content 骨架门面（注册表查询入口）。</summary>
        public static class Content
        {
            /// <summary>内容条目 id → Entry → Content 注册表处理器表。</summary>
            public static LuaTable GetProcessor(int id) => s_contentRegistry.Get(Data.GetContentEntry(id).Entry);
        }

        /// <summary>
        /// text 门面（LText 受控面，批C-②）：Get/Format 取文案（Toast/动态拼接用——**控件文案仍走
        /// <c>self.ui:SetTextKey</c>**，语言切换自动刷新）；SetLocale/Locale 供设置页语言切换。
        /// 参数上限 4 个具名位（与 Lua shim 的 LText 参数口径一致，MaxLuaFormatArgs）。
        /// </summary>
        public static class Text
        {
            private static ILocalizationService Service()
                => s_localeProvider != null
                    ? s_localeProvider()
                    : throw new InvalidOperationException("Bridge.text 需先 BindText（装配点未注入语言服务）");

            /// <summary>取安全文本（缺 key 走缺 key 策略——开发期显 [key]、发布期占位）。</summary>
            public static string Get(string key) => Service().Get(key);

            /// <summary>取文本并填参（{0}..{3} 编号占位；nil 位跳过）。</summary>
            public static string Format(string key, object a0 = null, object a1 = null,
                object a2 = null, object a3 = null)
            {
                if (a0 == null) return Service().Format(key);
                if (a1 == null) return Service().Format(key, a0);
                if (a2 == null) return Service().Format(key, a0, a1);
                if (a3 == null) return Service().Format(key, a0, a1, a2);
                return Service().Format(key, a0, a1, a2, a3);
            }

            /// <summary>切换语言（同值无操作；切换经页面索引自动重刷已绑定文案）。</summary>
            public static void SetLocale(string locale) => Service().SetLocale(locale);

            /// <summary>当前语言（BCP-47）。</summary>
            public static string Locale => Service().Locale;
        }
    }
}
