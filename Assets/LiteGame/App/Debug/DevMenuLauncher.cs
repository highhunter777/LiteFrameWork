#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>开发入口引导：开发页（DevMenuUI）的生成入口。
    /// 启动开关由 <see cref="DevStartupConfig"/> 发布静态镜像；dev 资产不在收集组，加载仅编辑器形态。
    /// release 构建零残留（条件编译）。</summary>
    public sealed class DevMenuLauncher : MonoBehaviour
    {
        /// <summary>开发页（dev 覆盖件）预制体路径（单源；构建器与守卫同引用）。</summary>
        public const string DevMenuPrefabPath = "Assets/LiteGame/DevTool/DevMenuUI.prefab";

        private bool _devMenuSpawned;

        private void Update()
        {
            if (DevStartupConfig.DevMenuEnabled) EnsureDevMenu();
        }

        /// <summary>开发页（dev 覆盖件）：开出「开发」入口——测试模式（训练场）/ 直连对局 / 关闭，
        /// 大厅页可随时跳。幂等：只建一次；dev 预制体不在收集组 ⇒ 仅编辑器形态。</summary>
        private void EnsureDevMenu()
        {
            if (_devMenuSpawned) return;
            _devMenuSpawned = true;
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(DevMenuPrefabPath);
            if (prefab == null)
            {
                Log.Warning($"开发页：预制体缺失——{DevMenuPrefabPath}", LogTag.UI);
                return;
            }
            var instance = Object.Instantiate(prefab);
            instance.name = "DevMenuUI";
            Object.DontDestroyOnLoad(instance);   // 常驻：进对局后入口仍在
            Log.Info($"开发页：入口已开出（{DevMenuPrefabPath}）", LogTag.UI);
#else
            Log.Warning("开发页：仅编辑器形态可用（dev 预制体不进收集组）", LogTag.UI);
#endif
        }
    }
}
#endif
