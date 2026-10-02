using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEditor;
using UnityEngine;
using LiteClient;

namespace LiteGame.Editor
{
    /// <summary>
    /// 增量重填调试口（《UI性能优化规划》缺口 2 的验收手段，Play only）：
    /// **与 <see cref="LuaDevReload"/> 并列**——后者是"重建 env + 全关界面"的重锤（Editor 迭代用），
    /// 本菜单走 <see cref="LuaRegistryRefillService"/> 的轻路径（不动 env，只重预载 + 清 require 缓存 + 重填）。
    /// 自检断言（失败当场抛 + Log.Error，不静默）：
    ///   ① 三注册表失效纪元前进；② 报告无失败项；③ 待更新界面数不增（池中件已换表）。
    /// </summary>
    public static class LuaDevRefill
    {
        [MenuItem("LiteGame/Lua/Refill Registries %&f")]
        private static void Refill()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DevRefill] 仅 Play 模式可用（LuaEnv 需存活）");
                return;
            }
            RefillAsync().Forget();                        // 一行转发，禁止 async void
        }

        private static async UniTaskVoid RefillAsync()
        {
            var lua = Object.FindFirstObjectByType<LuaComponent>();
            if (lua == null) { Debug.LogError("[DevRefill] 未找到 LuaComponent"); return; }

            var entry = Object.FindFirstObjectByType<GameEntry>();
            if (entry == null) { Debug.LogError("[DevRefill] 未找到 GameEntry（当前场景不是启动场景？）"); return; }

            var container = entry.TakeContainer();
            var service = container.Resolve<LuaRegistryRefillService>();
            var ui = container.Resolve<UIService>();

            int genBefore = service.Generations;
            int staleBefore = ui.StaleLogicCount;

            var report = await service.RefillAsync(CancellationToken.None);

            int genAfter = service.Generations;
            int staleAfter = ui.StaleLogicCount;

            bool genAdvanced = genAfter > genBefore;
            bool noFailures = !report.HasFailures;
            bool staleNotGrew = staleAfter <= staleBefore;

            string line = $"[DevRefill] 纪元 {genBefore}→{genAfter}；填充 {report.Filled}/{report.Total}（失败 {report.Failed}）；" +
                          $"待更新 {staleBefore}→{staleAfter} → {(genAdvanced && noFailures && staleNotGrew ? "PASS" : "FAIL")}";
            if (genAdvanced && noFailures && staleNotGrew)
            {
                Debug.Log(line);
                return;
            }

            Debug.LogError(line);
            throw new System.InvalidOperationException(line);   // 失败当场暴露（不静默）
        }
    }
}
