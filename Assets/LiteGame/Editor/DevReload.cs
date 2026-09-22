using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// DevReload（M3 §2.7，手册步骤 7）：菜单 Ctrl+Alt+R 秒级迭代。**顺序钉死**——
    /// env.Dispose 后旧 LuaTable 引用全失效，Bridge/注册表缓存不清 = 静默用旧对象：
    /// ⓪ 全关 → ⓪′ 逻辑全体落空（`DropAllLogic`：池中件也释放 Lua 引用，**必须在 env.Dispose 之前**，
    /// 否则下次复用会拿已 Dispose 的 LuaFunction 打进死环境）→ ① 清 Bridge Lua 缓存 → ② 清三注册表
    /// （Fill 重复抛，重填前置；Generation 前进作失效纪元）→ ③ env.Dispose 重建（事件桥随 Shutdown
    /// 退订、Init 重订）→ ④ 全量重预载（改动的 .lua 经 Unity 重导入进入模拟清单）→ ⑤ 重跑 main.lua
    /// → ⑥ 重填注册表（**走 §2.4 同一条 RegistryFiller 填充路径，不写第二份**）→ 报告统一判定
    /// （失败逐条 Log.Error）。仅 Play 模式可用（env 存活才有意义）；Editor asmdef 天然不进包。
    /// </summary>
    public static class LuaDevReload
    {
        [MenuItem("LiteGame/Lua/Reload Env %&r")]
        private static void Reload()
        {
            if (!Application.isPlaying)
            {
                Log.Warning("DevReload 仅 Play 模式可用（LuaEnv 需存活）", "DevReload");
                return;
            }
            ReloadAsync().Forget();                        // 一行转发，禁止 async void（M0 指导 §6）
        }

        private static async UniTaskVoid ReloadAsync()
        {
            var lua = Object.FindFirstObjectByType<LiteGame.LuaComponent>();
            if (lua == null)
            {
                Log.Error("DevReload 未找到 LuaComponent", "DevReload");
                return;
            }

            var container = Object.FindFirstObjectByType<LiteGame.GameEntry>().TakeContainer();
            var config = container.Resolve<LiteGame.IConfigService>();
            var events = container.Resolve<LiteFramework.IEventCenter>();
            var uiService = container.Resolve<LiteGame.UIService>();
            var ui = container.Resolve<LiteGame.IUILuaRegistry>();
            var content = container.Resolve<LiteGame.IContentLuaRegistry>();
            var strategy = container.Resolve<LiteGame.IStrategyLuaRegistry>();

            await uiService.CloseAllOpen();                // ⓪ 重载后全关（§2.3 定案：旧 env 的适配器随 Dispose 失效）
            uiService.DropAllLogic();                       // ⓪′ 逻辑落空：释放所有界面（含池中件）持有的 Lua 引用
            Bridge.Data.ClearLuaCaches();                  // ① 旧 LuaTable 引用先放手（§2.5 缓存位）
            ui.Clear();                                    // ② 注册表清空
            content.Clear();
            strategy.Clear();
            lua.Shutdown();                                // ③ 事件桥退订 → env.Dispose（旧对象全失效时点）

            var preloader = new LiteGame.LuaPreloader();
            await preloader.PreloadAllAsync();             // ④ 重预载：改动后的 .lua 进缓存
            lua.Init(preloader, events);                   //    env 重建 + 服务桥/事件桥重绑
            lua.DoMain();                                  // ⑤ 重跑 main.lua

            var filler = new LiteGame.RegistryFiller(config, lua, ui, content, strategy);
            var report = filler.FillAll(CancellationToken.None);   // ⑥ 同一条填充路径（§2.4 ③）
            if (report.HasFailures)
            {
                foreach (var f in report.Failures)
                    Log.Error($"DevReload 注册表填充失败 [{f.kind}] {f.key}:{f.reason}", "DevReload");
            }
            Log.Info($"DevReload 完成：预载 {preloader.Count}、填充 {report.Filled}/{report.Total}、失败 {report.Failed}", "DevReload");
        }
    }
}
