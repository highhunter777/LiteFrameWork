using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteGame;
using UnityEditor;
using UnityEngine;
using YooAsset;

namespace LiteGame.Editor
{
    /// <summary>
    /// 收集组校验口（《UI性能优化规划》缺口 1 的验收手段）：Play 下经 <see cref="AssetService"/> 逐件加载
    /// UI 全量 prefab（25 控件 + 界面），**不经 AssetDatabase 分支**——这是没有真机也能验「收集组覆盖到位」的办法。
    /// 两层断言：① tag `ui` 清单条目数 ② 逐件真实加载成功数（清单有 ≠ 可寻址可加载）。
    /// 仅 Play 模式（AssetService 需已初始化）；日志出口用 Debug.Log（编辑态 LiteFramework.Log 未安装）。
    /// </summary>
    public static class UICollectCheckMenu
    {
        private const string WidgetDir = "Assets/UI/Widgets";   // UI 控件模板目录（顶层 Assets/UI）
        private const string ScreenDir = "Assets/UI/Screens";
        private const string Tag = "ui";

        [MenuItem("LiteGame/UI/校验收集组")]
        private static void Run() => CheckAsync().Forget();

        private static async UniTaskVoid CheckAsync()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[UICollectCheck] 仅 Play 模式可用（AssetService 需已初始化）");
                return;
            }

            var paths = CollectPrefabPaths();
            if (paths.Count == 0)
            {
                Debug.LogError("[UICollectCheck] 一个 prefab 都没找到——目录/收集组核对失败");
                return;
            }

            int loaded = 0;
            foreach (var path in paths)
            {
                try
                {
                    var go = await AssetService.LoadAssetAsync<GameObject>(path);
                    if (go == null) Debug.LogError($"[UICollectCheck] 加载返回 null：{path}");
                    else loaded++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UICollectCheck] 加载失败 {path}：{ex.GetType().Name}:{ex.Message}");
                }
            }

            int tagCount = -1;
            if (YooAssets.TryGetPackage(AssetService.DefaultPackageName, out var pkg))
            {
                var infos = pkg.GetAssetInfos(Tag);
                tagCount = infos?.Length ?? 0;
            }
            else
            {
                Debug.LogError($"[UICollectCheck] 未找到包：{AssetService.DefaultPackageName}");
            }

            bool pass = loaded == paths.Count && tagCount == paths.Count;
            string line = $"[UICollectCheck] 可加载 {loaded}/{paths.Count}，tag={Tag} 清单 {tagCount} 条 → {(pass ? "PASS" : "FAIL")}";
            if (pass) Debug.Log(line);
            else Debug.LogError(line);
        }

        /// <summary>待验路径 = 两个收集目录下的全部 prefab（升序，结果可复现）。</summary>
        private static List<string> CollectPrefabPaths()
        {
            var list = new List<string>(32);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { WidgetDir, ScreenDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) list.Add(path);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }
}
