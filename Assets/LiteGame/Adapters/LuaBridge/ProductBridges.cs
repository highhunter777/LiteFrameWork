using System;
using XLua; // lint-allow R12：产品桥装订口（框架四门面+产品门面同 env 单源）——本件住 Lua 桥适配器，装配根注入流程端口

namespace LiteGame
{
    /// <summary>
    /// 产品桥装订面（单源）：Lua env 重建时绑定**全部**全局门面——框架四件（data/ui/content/text，
    /// <see cref="Bridge.BindGlobals"/>）+ 产品门面（联机 meta <see cref="MetaBridge"/>、设置
    /// <see cref="ProductSettings"/>、房间会话 <see cref="RoomBridge"/>——房主开局批）。
    /// Preload 首建与 DevReload 重建两条路共用本口——新增产品门面在此加一行，
    /// 禁止在流程里散落第二处 env 绑定（白名单纪律的可枚举点）。
    /// 流程经 <see cref="ILuaProductBridge"/> 端口消费，模块不反向认识本件。
    ///
    /// **设置门面的 Lua 面在这里、不在设置域**：门面本体（值域/生效面）住 <c>Modules/Settings</c>，
    /// 而 <c>XLua</c> 是适配器依赖——绑定代码随适配器走，设置域因此不认识脚本环境。
    /// </summary>
    public sealed class ProductBridges : ILuaProductBridge
    {
        private readonly RoomBridge _room;   // 与流程共用同一实例（Battle 阶段装订的会话必须能被 env 重建后的门面读到）

        public ProductBridges(RoomBridge room)
            => _room = room ?? throw new ArgumentNullException(nameof(room));

        public void BindGlobals(LuaEnv env)
        {
            Bridge.BindGlobals(env);              // 框架四门面（data/ui/content/text）
            MetaBridge.BindGlobals(env);          // 联机门面（meta——产品模块）
            BindSettingsGlobals(env);             // 设置门面（settings——产品模块）
            _room.BindGlobals(env);               // 房间会话门面（room——本适配器）
        }

        /// <summary>装订 <c>Bridge.settings</c> 进全局 Bridge 表（在框架门面与 meta 之后）。
        /// 读取点经 <see cref="ProductSettings.Current"/>——未装订即 fail-fast（与 meta 门面同口径）。</summary>
        private static void BindSettingsGlobals(LuaEnv env)
        {
            var settings = env.NewTable();

            // 既有平铺面（保持兼容：页面与既有用例不破）
            settings.Set("Volume", new Func<float>(() => ProductSettings.Current.Volume));
            settings.Set("SetVolume", new Action<float>(v => ProductSettings.Current.SetVolume(v)));
            settings.Set("SoundEnabled", new Func<bool>(() => ProductSettings.Current.SoundEnabled));
            settings.Set("SetSoundEnabled", new Action<bool>(on => ProductSettings.Current.SetSoundEnabled(on)));
            settings.Set("Quality", new Func<int>(() => ProductSettings.Current.Quality));
            settings.Set("SetQuality", new Action<int>(tier => ProductSettings.Current.SetQuality(tier)));
            settings.Set("Save", new Action(() => ProductSettings.Current.Save()));
            settings.Set("ResetDefaults", new Action(() => ProductSettings.Current.ResetDefaults()));

            // 显示组（桌面专属项由 Supports* 查询，页面据此隐藏——不在 Lua 写平台分支）
            var display = env.NewTable();
            display.Set("Mode", new Func<int>(() => ProductSettings.Current.DisplayMode));
            display.Set("SetMode", new Action<int>(v => ProductSettings.Current.SetDisplayMode(v)));
            display.Set("SupportsMode", new Func<bool>(() => ProductSettings.Current.SupportsWindowSettings));
            display.Set("ResolutionCount", new Func<int>(() => ProductSettings.Current.ResolutionCount()));
            display.Set("ResolutionLabel", new Func<int, string>(i => ProductSettings.Current.ResolutionLabel(i)));
            display.Set("ResolutionIndex", new Func<int>(() => ProductSettings.Current.ResolutionIndex));
            display.Set("SetResolutionIndex", new Action<int>(i => ProductSettings.Current.SetResolutionIndex(i)));
            display.Set("SupportsResolution", new Func<bool>(() => ProductSettings.Current.SupportsWindowSettings));
            display.Set("FrameRateOptionCount", new Func<int>(() => ProductSettings.Current.FrameRateOptionCount));
            display.Set("FrameRateOption", new Func<int, int>(i => ProductSettings.Current.FrameRateOption(i)));
            display.Set("FrameRate", new Func<int>(() => ProductSettings.Current.FrameRate));
            display.Set("SetFrameRate", new Action<int>(v => ProductSettings.Current.SetFrameRate(v)));
            display.Set("VSync", new Func<int>(() => ProductSettings.Current.VSync));
            display.Set("SetVSync", new Action<int>(v => ProductSettings.Current.SetVSync(v)));
            display.Set("SupportsVSync", new Func<bool>(() => ProductSettings.Current.SupportsWindowSettings));
            settings.Set("display", display);

            // 控制组（视角手感；键位覆盖为 P2，届时在此加子表）
            var control = env.NewTable();
            control.Set("LookSensitivity", new Func<float>(() => ProductSettings.Current.LookSensitivity));
            control.Set("SetLookSensitivity", new Action<float>(v => ProductSettings.Current.SetLookSensitivity(v)));
            control.Set("AdsSensitivity", new Func<float>(() => ProductSettings.Current.AdsSensitivity));
            control.Set("SetAdsSensitivity", new Action<float>(v => ProductSettings.Current.SetAdsSensitivity(v)));
            control.Set("InvertLookY", new Func<bool>(() => ProductSettings.Current.InvertLookY));
            control.Set("SetInvertLookY", new Action<bool>(on => ProductSettings.Current.SetInvertLookY(on)));
            settings.Set("control", control);

            // 常规组（页签记忆；语言走 Bridge.text、账号/服务器是只读事实，不在此）
            var general = env.NewTable();
            general.Set("LastTab", new Func<int>(() => ProductSettings.Current.LastTab));
            general.Set("SetLastTab", new Action<int>(i => ProductSettings.Current.SetLastTab(i)));
            settings.Set("general", general);

            env.Global.Get<LuaTable>("Bridge").Set("settings", settings);
        }
    }
}
